using MPSettlers.CameraSystem;
using MPSettlers.Characters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static UnityEngine.LowLevelPhysics2D.PhysicsBody;

namespace MPSettlers.Gameplay
{
    public partial class SandboxGameManager
    {
        private const float DropStackMergeRadius = 5f;

        private void HandleInteract()
        {
            if (targetedPickup != null)
            {
                CollectPickup(targetedPickup);
                return;
            }

            if (targetedPlacedObject != null && TryOpenContainerStorage(targetedPlacedObject))
            {
                return;
            }

            if (targetedRenewable != null && targetedRenewable.TryHarvest(out int amount))
            {
                AddResource(targetedRenewable.ResourceType, amount);

                // Award skill XP based on resource type
                long harvestXp = Mathf.Max(1, amount) * 15L;
                switch (targetedRenewable.ResourceType)
                {
                    case ResourceType.Wood: AwardSkillXp(SkillType.Woodcutting, harvestXp); break;
                    case ResourceType.Stone: AwardSkillXp(SkillType.Mining, harvestXp); break;
                    case ResourceType.Food: AwardSkillXp(SkillType.Farming, harvestXp); break;
                }

                SetStatusMessage($"+{amount} {targetedRenewable.ResourceType.ToString().ToLowerInvariant()}");
                SaveWorld();
            }
        }

        private void CollectPickup(InventoryPickup pickup)
        {
            if (pickup == null)
            {
                return;
            }

            // ── Resolve catalog item: try pickup ID first, then prefab name fallback ──
            BuildCatalogItem pickupItem = null;

            if (!string.IsNullOrWhiteSpace(pickup.ItemId) &&
                catalogLookup.TryGetValue(pickup.ItemId, out BuildCatalogItem resolvedById))
            {
                pickupItem = resolvedById;
            }
            else
            {
                // Fallback: match by prefab/gameObject name (handles scene-placed objects
                // where InventoryPickup.itemId was never set)
                string objectName = pickup.gameObject.name;
                pickupItem = catalogLookup.Values.FirstOrDefault(
                    candidate => string.Equals(candidate.prefabName, objectName, StringComparison.OrdinalIgnoreCase));
            }

            // Canonical ID: always use the catalog item's id when available
            string canonicalId = pickupItem != null ? pickupItem.id : pickup.ItemId;

            if (string.IsNullOrWhiteSpace(canonicalId))
            {
                SetStatusMessage($"Cannot collect {pickup.DisplayName} — unknown item.");
                return;
            }

            EquipmentInfo eqInfo = pickupItem != null ? GetEquipmentInfo(pickupItem) : null;

            string displayName = pickupItem != null ? pickupItem.displayName : pickup.DisplayName;

            switch (pickup.InventoryType)
            {
                case PickupInventoryType.Food:
                    AddInventoryCount(storedFoodInventory, canonicalId, 1);
                    SetStatusMessage($"Stored {displayName}.");
                    break;

                case PickupInventoryType.Weapon:
                    AddInventoryCount(storedWeaponInventory, canonicalId, 1);

                    if (eqInfo != null && eqInfo.isAmmo)
                        SetStatusMessage($"Collected ammo: {displayName}.");
                    else
                        SetStatusMessage($"Collected {displayName}.");

                    break;

                default:
                    // PickupInventoryType.None — auto-detect from catalog inference
                    if (eqInfo != null && (eqInfo.isWeapon || eqInfo.isArmor || eqInfo.isAmmo))
                    {
                        AddInventoryCount(storedWeaponInventory, canonicalId, 1);
                        SetStatusMessage($"Collected {displayName}.");
                    }
                    else
                    {
                        SetStatusMessage($"Picked up {displayName}.");
                    }
                    break;
            }

            // ── Decrement stack or remove from world ──────────────────
            if (pickup.StackCount > 1)
            {
                // Stack has more items — decrement and keep the world object
                pickup.DecrementStack();
                int remaining = pickup.StackCount;
                SetStatusMessage($"Collected 1 {displayName}. ({remaining} remaining on ground)");
            }
            else
            {
                // Last item in stack (or non-stacked pickup) — remove from world
                PlacedWorldObject placedWorldObject = pickup.GetComponent<PlacedWorldObject>();
                if (placedWorldObject != null)
                {
                    placedObjects.Remove(placedWorldObject.UniqueId);
                }

                Destroy(pickup.gameObject);
            }

            SaveWorld();
        }

        private void ConsumeStoredFood()
        {
            List<KeyValuePair<string, int>> availableItems = storedFoodInventory
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .ToList();

            if (availableItems.Count == 0)
            {
                SetStatusMessage("No stored food to consume.");
                return;
            }

            if (health >= MaxHealth)
            {
                SetStatusMessage("Health is already full.");
                return;
            }

            string itemId = availableItems[0].Key;
            RemoveInventoryCount(storedFoodInventory, itemId, 1);
            int previousHealth = health;
            health = Mathf.Clamp(health + FoodHealAmount, 0, MaxHealth);
            SetStatusMessage($"Consumed {GetDisplayNameForItemId(itemId)} (+{health - previousHealth} health).");
            SaveWorld();
        }

        private void TryDropSelectedInventoryItem()
        {
            List<JournalInventoryEntry> entries = GetJournalInventoryEntries();
            JournalInventoryEntry? selected = GetSelectedInventoryEntry(entries);
            if (selected == null || selected.Value.quantity <= 0)
            {
                SetStatusMessage("No item selected to drop.");
                return;
            }

            string itemId = selected.Value.itemId;
            string displayName = selected.Value.displayName;

            // Handle raw resources (not catalog items — just decrement the resource counter)
            if (string.Equals(itemId, "resource_wood", StringComparison.Ordinal))
            {
                if (wood <= 0) return;
                wood--;
                SetStatusMessage("Dropped 1 wood.");
                SaveWorld();
                return;
            }

            if (string.Equals(itemId, "resource_stone", StringComparison.Ordinal))
            {
                if (stone <= 0) return;
                stone--;
                SetStatusMessage("Dropped 1 stone.");
                SaveWorld();
                return;
            }

            if (string.Equals(itemId, "carry_food", StringComparison.Ordinal))
            {
                if (food <= 0) return;
                food--;
                SetStatusMessage("Dropped 1 food.");
                SaveWorld();
                return;
            }

            // Catalog item: must exist in catalog to spawn as a world pickup
            if (!catalogLookup.TryGetValue(itemId, out BuildCatalogItem catalogItem))
            {
                SetStatusMessage($"Cannot drop {displayName} — unknown catalog item.");
                return;
            }

            // Remove 1 from the correct inventory
            if (storedFoodInventory.ContainsKey(itemId))
            {
                RemoveInventoryCount(storedFoodInventory, itemId, 1);
            }
            else if (storedWeaponInventory.ContainsKey(itemId))
            {
                RemoveInventoryCount(storedWeaponInventory, itemId, 1);
            }
            else
            {
                SetStatusMessage($"Cannot drop {displayName} — not in inventory.");
                return;
            }

            // Try to merge with a nearby existing dropped pickup of the same item
            if (playerTransform != null)
            {
                InventoryPickup nearbyStack = FindNearbyDroppedStack(itemId, playerTransform.position, DropStackMergeRadius);
                if (nearbyStack != null)
                {
                    nearbyStack.AddToStack(1);
                    SetStatusMessage($"Dropped {displayName}. (stack: {nearbyStack.StackCount})");
                    SaveWorld();
                    return;
                }
            }

            // No nearby stack found — spawn a new pickup on the ground in front of the player
            if (playerTransform != null && catalogItem.prefab != null)
            {
                Vector3 dropPosition = playerTransform.position + playerTransform.forward * 2f;
                dropPosition = GetGroundedPosition(dropPosition);

                SpawnCatalogItem(
                    catalogItem,
                    null,
                    dropPosition,
                    Quaternion.identity,
                    1f,
                    registerForSave: true,
                    placedByPlayer: true,
                    alignToGround: true);
            }

            SetStatusMessage($"Dropped {displayName}.");
            SaveWorld();
        }

        private InventoryPickup FindNearbyDroppedStack(string itemId, Vector3 origin, float radius)
        {
            float closestDistance = float.MaxValue;
            InventoryPickup closest = null;

            foreach (PlacedWorldObject placed in placedObjects.Values)
            {
                if (placed == null)
                    continue;

                InventoryPickup pickup = placed.GetComponent<InventoryPickup>();
                if (pickup == null)
                    continue;

                if (!string.Equals(pickup.ItemId, itemId, StringComparison.Ordinal))
                    continue;

                float distance = Vector3.Distance(placed.transform.position, origin);
                if (distance <= radius && distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = pickup;
                }
            }

            return closest;
        }

        private void HandleInGameMenuNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame)
            {
                ShiftInGameMenuTab(-1);
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame)
            {
                ShiftInGameMenuTab(1);
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Overview;
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Skills;
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Inventory;
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Settings;
            }

            // X key drops the selected inventory item to the ground
            if (Keyboard.current.xKey.wasPressedThisFrame && selectedInGameMenuTab == InGameMenuTab.Inventory)
            {
                TryDropSelectedInventoryItem();
            }
        }

        private void ShiftInGameMenuTab(int delta)
        {
            InGameMenuTab[] tabs =
            {
                InGameMenuTab.Overview,
                InGameMenuTab.Skills,
                InGameMenuTab.Inventory,
                InGameMenuTab.Settings
            };

            int currentIndex = Array.IndexOf(tabs, selectedInGameMenuTab);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            currentIndex = (currentIndex + delta) % tabs.Length;
            if (currentIndex < 0)
            {
                currentIndex += tabs.Length;
            }

            selectedInGameMenuTab = tabs[currentIndex];
        }

        private void OpenInGameMenu()
        {
            buildPanelOpen = false;
            deleteMode = false;
            placementActive = false;
            pointerMode = true;
            selectedInGameMenuTab = InGameMenuTab.Overview;
            CleanupGhost();
            inGameMenuOpen = true;

            // Apply immediately so the cursor unlocks and camera/look stops
            // on the same frame the key is pressed (before LateUpdate).
            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
                playerMovement.InputSuppressed = true;

            if (pauseGameWhenJournalOpen)
            {
                timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? timeScaleBeforePause : Time.timeScale;
                Time.timeScale = 0f;
            }
        }

        private void CloseInGameMenu()
        {
            inGameMenuOpen = false;
            pointerMode = false;

            // Restore gameplay mode immediately.
            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
                playerMovement.InputSuppressed = false;

            if (pauseGameWhenJournalOpen && Mathf.Approximately(Time.timeScale, 0f))
            {
                Time.timeScale = timeScaleBeforePause;
            }
        }

        private void OpenEscMenu()
        {
            if (inGameMenuOpen || buildPanelOpen)
                return;

            escMenuOpen = true;
            pointerMode = true;

            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
                playerMovement.InputSuppressed = true;
        }

        private void CloseEscMenu()
        {
            escMenuOpen = false;
            pointerMode = false;

            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
                playerMovement.InputSuppressed = false;
        }

        private void EscMenuOpenSettings()
        {
            CloseEscMenu();
            selectedInGameMenuTab = InGameMenuTab.Settings;
            OpenInGameMenu();
        }
    }
}
