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
        private void HandleGlobalShortcuts()
        {
            bool inGameMenuTogglePressed = ConsumePendingShortcut(ref pendingInGameMenuToggle) ||
                                           (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);

            if (inGameMenuTogglePressed)
            {
                if (containerStorageOpen)
                {
                    CloseContainerStorage();
                    return;
                }

                if (inGameMenuOpen)
                {
                    CloseInGameMenu();
                }
                else
                {
                    if (escMenuOpen)
                        CloseEscMenu();
                    OpenInGameMenu();
                }
            }

            if (inGameMenuOpen && (ConsumePendingShortcut(ref pendingInGameMenuClose) ||
                                   (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)))
            {
                CloseInGameMenu();
                return;
            }

            // ESC menu: close if open, open if nothing else is open
            bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (containerStorageOpen && escPressed)
            {
                CloseContainerStorage();
                return;
            }

            if (escMenuOpen && (ConsumePendingShortcut(ref pendingEscMenuClose) || escPressed))
            {
                CloseEscMenu();
                return;
            }

            if (!IsAnyMenuOpen && !placementActive && !deleteMode &&
                (ConsumePendingShortcut(ref pendingEscMenuOpen) || escPressed))
            {
                OpenEscMenu();
                return;
            }

            // Consume stale pending esc shortcuts that didn't apply
            ConsumePendingShortcut(ref pendingEscMenuOpen);
            ConsumePendingShortcut(ref pendingEscMenuClose);

            if (escMenuOpen)
                return;

            bool buildTogglePressed = ConsumePendingShortcut(ref pendingBuildToggle) ||
                                      (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);

            if (!inGameMenuOpen && buildTogglePressed)
            {
                if (buildPanelOpen)
                {
                    CloseBuildPanel();
                }
                else if (placementActive)
                {
                    CancelPlacement();
                    OpenBuildPanel();
                }
                else
                {
                    OpenBuildPanel();
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (buildPanelOpen && Keyboard.current.lKey.wasPressedThisFrame)
            {
                devBuildMode = !devBuildMode;
                SetStatusMessage(devBuildMode ? "DEV Build Mode enabled." : "DEV Build Mode disabled.");
            }

            if (buildPanelOpen && Keyboard.current.fKey.wasPressedThisFrame)
            {
                FavoriteSelectedBuildItem();
            }

            if (Keyboard.current.xKey.wasPressedThisFrame)
            {
                // Context-sensitive X key:
                // 1. If in inventory/journal on the Inventory tab → drop selected item (handled in HandleInGameMenuNavigation)
                // 2. If in placement/delete mode and looking at a placed object → delete that object directly
                // 3. Otherwise → toggle delete mode
                if (inGameMenuOpen || containerStorageOpen || escMenuOpen)
                {
                    // Let HandleInGameMenuNavigation handle X in journal context
                }
                else if ((placementActive || deleteMode) && targetedPlacedObject != null)
                {
                    TryDeleteTarget();
                }
                else
                {
                    deleteMode = !deleteMode;
                    if (deleteMode)
                    {
                        CloseBuildPanel();
                        placementActive = false;
                        CleanupGhost();
                    }
                }
            }

            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                ConsumeStoredFood();
            }

            if (!placementActive && !buildPanelOpen && !inGameMenuOpen && !escMenuOpen
                && Keyboard.current.pKey.wasPressedThisFrame)
            {
                TryBeginPlantingFirstSeed();
            }

            if (buildPanelOpen && (TryGetPreviousPressedThisFrame() || TryGetNextPressedThisFrame()))
            {
                CycleSelection(TryGetNextPressedThisFrame() ? 1 : -1);
            }
        }

        private void HandleBuildMenuNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            int verticalDelta = 0;
            int horizontalDelta = 0;

            if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame)
            {
                verticalDelta = -1;
            }
            else if (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame)
            {
                verticalDelta = 1;
            }

            if (Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                horizontalDelta = -1;
            }
            else if (Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                horizontalDelta = 1;
            }

            if (horizontalDelta != 0)
            {
                ShiftCategory(horizontalDelta);
            }

            if (verticalDelta != 0)
            {
                ShiftBuildItem(verticalDelta);
            }

            if (TryGetMouseWheelDelta(out int wheelDelta))
            {
                ShiftBuildItem(wheelDelta);
            }

            bool closePressed = ConsumePendingShortcut(ref pendingMenuClose) ||
                                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

            if (closePressed)
            {
                CloseBuildPanel();
                return;
            }

            bool confirmSelection =
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                TryGetAttackPressedThisFrame();

            if (confirmSelection && GetSelectedItem() != null)
            {
                SelectItem(selectedCategory, selectedIndex);
            }
        }

        private void HandleHotbarInput()
        {
            int previousSlotIndex = selectedHotbarIndex;

            int directSelection = GetDirectHotbarSlotSelection();
            if (directSelection >= 0)
            {
                selectedHotbarIndex = directSelection;
            }

            if (directSelection < 0 && TryGetMouseWheelDelta(out int wheelDelta))
            {
                CycleHotbarSelection(wheelDelta);
            }
            else if (directSelection < 0 && TryGetPreviousPressedThisFrame())
            {
                CycleHotbarSelection(-1);
            }
            else if (directSelection < 0 && TryGetNextPressedThisFrame())
            {
                CycleHotbarSelection(1);
            }

            if (selectedHotbarIndex != previousSlotIndex && placementActive)
            {
                TrySwapPlacementToSelectedHotbarItem();
            }

            if (placementActive || deleteMode || buildPanelOpen || pointerMode)
            {
                return;
            }

            if (TryGetSubmitPressedThisFrame() || TryGetAttackPressedThisFrame())
            {
                TryBeginPlacementFromSelectedHotbar();
            }

            if (!placementActive && !deleteMode && !buildPanelOpen && !inGameMenuOpen)
            {
                if (TryGetAttackPressedThisFrame())
                    TrySwingWeapon();

                if (Mouse.current != null)
                {
                    bool rightHeld = Mouse.current.rightButton.isPressed;

                    if (!placementActive)
                        TryRaiseShield(rightHeld);
                }
            }
        }

        private void FavoriteSelectedBuildItem()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatusMessage("No build item selected to favorite.");
                return;
            }

            int existingSlot = Array.IndexOf(favoriteHotbarItemIds, item.id);
            if (existingSlot >= 0)
            {
                selectedHotbarIndex = existingSlot;
                SetStatusMessage($"{item.displayName} is already in slot {existingSlot + 1}.");
                return;
            }

            int emptySlot = Array.FindIndex(favoriteHotbarItemIds, string.IsNullOrWhiteSpace);
            if (emptySlot >= 0)
            {
                favoriteHotbarItemIds[emptySlot] = item.id;
                selectedHotbarIndex = emptySlot;
                SaveWorld();
                SetStatusMessage($"Favorited {item.displayName} to slot {emptySlot + 1}.");
                return;
            }

            favoriteHotbarItemIds[selectedHotbarIndex] = item.id;
            SaveWorld();
            SetStatusMessage($"Replaced slot {selectedHotbarIndex + 1} with {item.displayName}.");
        }

        private void CycleHotbarSelection(int delta)
        {
            selectedHotbarIndex = (selectedHotbarIndex + delta) % HotbarSlotCount;
            if (selectedHotbarIndex < 0)
            {
                selectedHotbarIndex += HotbarSlotCount;
            }
        }

        private void TryBeginPlacementFromSelectedHotbar()
        {
            BuildCatalogItem item = GetSelectedHotbarItem();
            if (item == null)
            {
                return;
            }

            // If the hotbar item is a seed, enter planting mode instead of build placement
            if (IsSeedItem(item.id))
            {
                BeginPlantingSeed(item.id);
                return;
            }

            SelectCatalogItemById(item.id);
        }

        private void TrySwapPlacementToSelectedHotbarItem()
        {
            BuildCatalogItem item = GetSelectedHotbarItem();
            if (item == null)
            {
                return;
            }

            SelectCatalogItemById(item.id);
        }

        private void HandlePlacementInput()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    placementYaw += 90f;
                }

                if (Keyboard.current.gKey.wasPressedThisFrame)
                {
                    placementSnapEnabled = !placementSnapEnabled;
                }

                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CancelPlacement();
                    return;
                }
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (buildPanelOpen && (TryGetPreviousPressedThisFrame() || TryGetNextPressedThisFrame()))
            {
                CycleSelection(TryGetNextPressedThisFrame() ? 1 : -1);
            }

            if (!pointerMode && TryGetAttackPressedThisFrame())
            {
                if (plantingMode)
                    TryPlantSeed();
                else
                    TryPlaceCurrentSelection();
            }

            if (!pointerMode && TryGetSubmitPressedThisFrame())
            {
                if (plantingMode)
                    TryPlantSeed();
                else
                    TryPlaceCurrentSelection();
            }
        }

        private void HandleDeleteInput()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                deleteMode = false;
                pointerMode = buildPanelOpen;
                return;
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                deleteMode = false;
                pointerMode = buildPanelOpen;
                return;
            }

            if (!pointerMode && TryGetAttackPressedThisFrame())
            {
                TryDeleteTarget();
            }

            // X key also deletes the targeted object directly (handled by HandleGlobalShortcuts,
            // but LMB/attack is the primary path here for consistency)
        }

        private void TryDeleteTarget()
        {
            if (targetedPlacedObject == null)
            {
                return;
            }

            if (!catalogLookup.TryGetValue(targetedPlacedObject.CatalogItemId, out BuildCatalogItem item))
            {
                return;
            }

            RefundCost(item.cost);
            containerStorageByObjectId.Remove(targetedPlacedObject.UniqueId);
            placedObjects.Remove(targetedPlacedObject.UniqueId);
            Destroy(targetedPlacedObject.gameObject);
            SaveWorld();
            SetStatusMessage($"Deleted {item.displayName} and refunded {item.cost.ToDisplayString()}.");
        }

        private void UpdatePlacementGhost()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                placementHasValidTarget = false;
                CleanupGhost();
                return;
            }

            EnsureGhost(item);

            if (!TryGetCameraRaycast(PlacementDistance, out RaycastHit hit))
            {
                placementHasValidTarget = false;
                if (ghostInstance != null)
                {
                    ghostInstance.SetActive(false);
                }

                return;
            }

            if (ghostInstance != null)
            {
                ghostInstance.SetActive(true);
            }

            Quaternion rotation = Quaternion.Euler(0f, placementYaw, 0f);
            Vector3 rawPosition = hit.point;
            if (placementSnapEnabled)
            {
                rawPosition.x = Mathf.Round(rawPosition.x / PlacementSnapSize) * PlacementSnapSize;
                rawPosition.z = Mathf.Round(rawPosition.z / PlacementSnapSize) * PlacementSnapSize;
            }

            if (ghostInstance != null)
            {
                ghostInstance.transform.rotation = rotation;
                ghostInstance.transform.position = rawPosition;

                if (TryGetRenderableBounds(ghostInstance, out Bounds ghostBounds))
                {
                    float bottomOffset = ghostInstance.transform.position.y - ghostBounds.min.y;
                    rawPosition.y = hit.point.y + bottomOffset;
                    ghostInstance.transform.position = rawPosition;
                }
            }

            float playerDistance = playerTransform != null
                ? Vector2.Distance(new Vector2(rawPosition.x, rawPosition.z), new Vector2(playerTransform.position.x, playerTransform.position.z))
                : MinimumPlacementDistanceFromPlayer + 1f;

            placementHasValidTarget = playerDistance >= MinimumPlacementDistanceFromPlayer;
            pendingPlacementPosition = rawPosition;
            pendingPlacementRotation = rotation;

            UpdateGhostTint(placementHasValidTarget);
        }

        private void UpdateGhostTint(bool isValid)
        {
            Color tint = isValid ? new Color(0.35f, 1f, 0.45f, 0.92f) : new Color(1f, 0.35f, 0.35f, 0.92f);
            foreach (Renderer rendererComponent in ghostRenderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                foreach (Material material in rendererComponent.materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", tint);
                    }

                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", tint);
                    }
                }
            }
        }

        private void TryPlaceCurrentSelection()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                return;
            }

            if (!placementHasValidTarget)
            {
                SetStatusMessage("Move the ghost farther from the player.");
                return;
            }

            if (!devBuildMode && !CanAfford(item.cost))
            {
                SetStatusMessage($"Not enough resources for {item.displayName}. Press L in the build menu for DEV Build.");
                return;
            }

            GameObject placedObject = SpawnCatalogItem(
                item,
                Guid.NewGuid().ToString("N"),
                pendingPlacementPosition,
                pendingPlacementRotation,
                1f,
                registerForSave: true,
                placedByPlayer: true);

            if (placedObject == null)
            {
                return;
            }

            if (!devBuildMode)
            {
                SpendCost(item.cost);
            }

            AwardSkillXp(SkillType.Building, 25L);
            SaveWorld();
            SetStatusMessage($"Placed {item.displayName}.");

            bool canContinue = devBuildMode || CanAfford(item.cost);
            if (canContinue)
            {
                CleanupGhost();
                EnsureGhost(item);
            }
            else
            {
                placementActive = false;
                CleanupGhost();
            }
        }

        private void CancelPlacement()
        {
            placementActive = false;
            plantingMode = false;
            plantingSeedId = null;
            CleanupGhost();
            pointerMode = false;
        }

        private void EnsureGhost(BuildCatalogItem item)
        {
            if (item == null || item.prefab == null)
            {
                CleanupGhost();
                return;
            }

            if (ghostInstance != null && string.Equals(activeGhostCatalogId, item.id, StringComparison.Ordinal))
            {
                return;
            }

            CleanupGhost();

            ghostInstance = Instantiate(item.prefab);
            if (ghostInstance == null)
            {
                activeGhostCatalogId = string.Empty;
                return;
            }

            ghostInstance.name = $"Ghost_{item.prefab.name}";
            activeGhostCatalogId = item.id;
            PrepareGhostForPreview();
            UpdateGhostTint(true);
        }

        private void PrepareGhostForPreview()
        {
            if (ghostInstance == null)
            {
                return;
            }

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                SetLayerRecursively(ghostInstance.transform, ignoreRaycastLayer);
            }

            foreach (MonoBehaviour behaviour in ghostInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Collider colliderComponent in ghostInstance.GetComponentsInChildren<Collider>(true))
            {
                colliderComponent.enabled = false;
            }

            ghostRenderers = ghostInstance.GetComponentsInChildren<Renderer>(true);
            if (ghostRenderers == null)
            {
                ghostRenderers = Array.Empty<Renderer>();
            }

            foreach (Renderer rendererComponent in ghostRenderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                rendererComponent.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rendererComponent.receiveShadows = false;
                rendererComponent.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                Material[] materials = rendererComponent.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    materials[i] = new Material(materials[i]);
                }

                rendererComponent.materials = materials;
            }
        }

        private void CleanupGhost()
        {
            if (ghostInstance != null)
            {
                Destroy(ghostInstance);
            }

            ghostInstance = null;
            ghostRenderers = Array.Empty<Renderer>();
            activeGhostCatalogId = string.Empty;
            placementHasValidTarget = false;
        }

        private void OpenBuildPanel()
        {
            CloseInGameMenu();
            buildPanelOpen = true;
            deleteMode = false;
            pointerMode = false;
            placementActive = false;
            CleanupGhost();
        }

        private void CloseBuildPanel()
        {
            buildPanelOpen = false;
            pointerMode = false;
        }

        private void SelectCategory(BuildCategory category)
        {
            if (selectedCategory == category)
            {
                return;
            }

            selectedCategory = category;
            buildListScroll = Vector2.zero;

            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            selectedIndex = items.Count > 0 ? 0 : -1;
        }

        private void ShiftCategory(int delta)
        {
            BuildCategory[] categories =
            {
                BuildCategory.Town,
                BuildCategory.Farm,
                BuildCategory.Food,
                BuildCategory.Weapons
            };

            int currentIndex = Array.IndexOf(categories, selectedCategory);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            currentIndex = (currentIndex + delta) % categories.Length;
            if (currentIndex < 0)
            {
                currentIndex += categories.Length;
            }

            SelectCategory(categories[currentIndex]);
            CenterBuildScrollOnSelection();
        }

        private void ShiftBuildItem(int delta)
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                buildListScroll.y = 0f;
                return;
            }

            selectedIndex = (selectedIndex + delta) % items.Count;
            if (selectedIndex < 0)
            {
                selectedIndex += items.Count;
            }

            CenterBuildScrollOnSelection();
        }

        private void SelectItem(BuildCategory category, int index)
        {
            if (!itemsByCategory.TryGetValue(category, out List<BuildCatalogItem> items) || items == null || items.Count == 0)
            {
                return;
            }

            if (index < 0 || index >= items.Count)
            {
                return;
            }

            selectedCategory = category;
            selectedIndex = index;

            EnsureSelectionInRange();
            CenterBuildScrollOnSelection();

            placementActive = true;
            deleteMode = false;
            buildPanelOpen = false;
            pointerMode = false;
            placementYaw = 0f;

            BuildCatalogItem selectedItem = GetSelectedItem();
            if (selectedItem != null)
            {
                EnsureGhost(selectedItem);
            }
        }

        private void SelectCatalogItemById(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !catalogLookup.TryGetValue(itemId, out BuildCatalogItem item))
            {
                return;
            }

            if (!itemsByCategory.TryGetValue(item.category, out List<BuildCatalogItem> categoryItems))
            {
                return;
            }

            int index = categoryItems.FindIndex(candidate => candidate.id == itemId);
            if (index < 0)
            {
                return;
            }

            SelectItem(item.category, index);
        }

        private void CycleSelection(int delta)
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            int previousIndex = selectedIndex;
            selectedIndex = (selectedIndex + delta) % items.Count;
            if (selectedIndex < 0)
            {
                selectedIndex += items.Count;
            }

            if (selectedIndex == previousIndex)
            {
                return;
            }

            CenterBuildScrollOnSelection();

            if (placementActive)
            {
                EnsureGhost(GetSelectedItem());
            }
        }

        private void CenterBuildScrollOnSelection()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items == null || items.Count == 0)
            {
                buildListScroll.y = 0f;
                return;
            }

            UiLayout layout = GetUiLayout();
            float rowHeight = layout.buildItemHeight;
            if (rowHeight <= 0f)
            {
                return;
            }

            int index = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            float rowTop = index * rowHeight;
            float rowBottom = rowTop + rowHeight;

            Rect panelRect = layout.buildPanelRect;
            float previewHeight = Mathf.Clamp(panelRect.width * 0.44f, 140f, 260f);
            float detailsHeight = Mathf.Clamp(90f * currentUiScale, 78f, 124f);
            float footerHeight = Mathf.Clamp(50f * currentUiScale, 42f, 64f);
            float viewportHeight = Mathf.Max(
                130f,
                panelRect.height - previewHeight - detailsHeight - footerHeight - Mathf.Clamp(96f * currentUiScale, 80f, 120f));

            float totalContent = items.Count * rowHeight;
            float maxScroll = Mathf.Max(0f, totalContent - viewportHeight);

            if (rowTop >= buildListScroll.y && rowBottom <= buildListScroll.y + viewportHeight)
            {
                return;
            }

            float centered = rowTop - (viewportHeight - rowHeight) * 0.5f;
            buildListScroll.y = Mathf.Clamp(centered, 0f, maxScroll);
        }

        private void EnsureSelectionInRange()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items == null || items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                return;
            }

            if (!string.IsNullOrEmpty(activeGhostCatalogId))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (string.Equals(items[i].id, activeGhostCatalogId, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                        return;
                    }
                }
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
        }
    }
}
