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
        private void InitializeCraftingRecipes()
        {
            craftingRecipes = new List<CraftingRecipe>
            {
                new() { id = "craft_wooden_fence", displayName = "Wooden Fence", description = "A simple wooden fence section.",
                    resultItemId = "town:WoodFence_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 3 } },
                new() { id = "craft_wooden_gate", displayName = "Wooden Gate", description = "A gate for your settlement.",
                    resultItemId = "town:WoodGate", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 5, stone = 1 } },
                new() { id = "craft_stone_wall", displayName = "Stone Wall", description = "A sturdy stone wall segment.",
                    resultItemId = "town:StoneWall_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 1, stone = 5 } },
                new() { id = "craft_campfire", displayName = "Campfire", description = "Provides light and warmth.",
                    resultItemId = "town:Campfire", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 4, stone = 2 } },
                new() { id = "craft_torch", displayName = "Torch", description = "A standing torch for your settlement.",
                    resultItemId = "town:Torch_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 2 } },
                new() { id = "craft_barrel", displayName = "Storage Barrel", description = "Extra storage for your settlement.",
                    resultItemId = "town:Barrel_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 4, stone = 1 } },
                new() { id = "craft_sword", displayName = "Iron Sword", description = "A basic melee weapon.",
                    resultItemId = "weapons:Sword01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 2, stone = 6 } },
                new() { id = "craft_axe", displayName = "Battle Axe", description = "Heavy hitting but slow.",
                    resultItemId = "weapons:Ax01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 3, stone = 5 } },
                new() { id = "craft_shield", displayName = "Wooden Shield", description = "Blocks incoming damage.",
                    resultItemId = "weapons:Shield01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 5, stone = 2 } },
                new() { id = "craft_dagger", displayName = "Dagger", description = "Fast and light.",
                    resultItemId = "weapons:Dagger01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 1, stone = 3 } },
                new() { id = "craft_spear", displayName = "Spear", description = "Good reach, balanced stats.",
                    resultItemId = "weapons:Spear01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 4, stone = 3 } },
                new() { id = "craft_arrows", displayName = "Arrows (x5)", description = "Ammunition for bows.",
                    resultItemId = "weapons:Arrow01", resultCount = 5, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 3, stone = 1 } },
                new() { id = "craft_cooked_food", displayName = "Cooked Rations", description = "Prepared food from raw ingredients.",
                    resultItemId = "food:Bread_01", resultCount = 2, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 1, food = 3 } },
                // ── Seed recipes ──────────────────────────────────────
                new() { id = "craft_tomato_seeds", displayName = "Tomato Seeds (x3)", description = "Plant in ground to grow tomatoes over time.",
                    resultItemId = "seed:tomato", resultCount = 3, resultIsStructure = false,
                    cost = new CraftingIngredient { food = 2 } },
                new() { id = "craft_cabbage_seeds", displayName = "Cabbage Seeds (x3)", description = "Plant in ground to grow cabbages over time.",
                    resultItemId = "seed:cabbage", resultCount = 3, resultIsStructure = false,
                    cost = new CraftingIngredient { food = 2 } },
            };

            // Validate recipes against actual catalog — remove any whose resultItemId doesn't exist
            craftingRecipes.RemoveAll(r => !catalogLookup.ContainsKey(r.resultItemId));
        }

        private bool CanAffordRecipe(CraftingRecipe recipe)
        {
            if (recipe?.cost == null) return true;
            CraftingIngredient c = recipe.cost;
            if (c.wood > wood) return false;
            if (c.stone > stone) return false;
            if (c.food > food) return false;
            if (!string.IsNullOrWhiteSpace(c.requiredItemId) && c.requiredItemCount > 0)
            {
                int itemCount = GetTotalItemCount(c.requiredItemId);
                if (itemCount < c.requiredItemCount) return false;
            }
            return true;
        }

        private int GetTotalItemCount(string itemId)
        {
            int count = 0;
            if (storedFoodInventory.TryGetValue(itemId, out int fc)) count += fc;
            if (storedWeaponInventory.TryGetValue(itemId, out int wc)) count += wc;
            return count;
        }

        private void SpendCraftingCost(CraftingIngredient cost)
        {
            if (cost == null) return;

            wood = Mathf.Max(0, wood - cost.wood);
            stone = Mathf.Max(0, stone - cost.stone);
            food = Mathf.Max(0, food - cost.food);

            if (!string.IsNullOrWhiteSpace(cost.requiredItemId) && cost.requiredItemCount > 0)
            {
                int remaining = cost.requiredItemCount;
                remaining = SpendItemFromInventory(storedFoodInventory, cost.requiredItemId, remaining);
                SpendItemFromInventory(storedWeaponInventory, cost.requiredItemId, remaining);
            }
        }

        private int SpendItemFromInventory(Dictionary<string, int> inventory, string itemId, int remaining)
        {
            if (remaining <= 0) return 0;
            if (!inventory.TryGetValue(itemId, out int available) || available <= 0) return remaining;
            int spend = Mathf.Min(remaining, available);
            RemoveInventoryCount(inventory, itemId, spend);
            return remaining - spend;
        }

        private void CraftRecipe(CraftingRecipe recipe)
        {
            if (recipe == null) return;
            if (!CanAffordRecipe(recipe))
            {
                SetStatusMessage("Not enough materials to craft this.");
                return;
            }

            if (!catalogLookup.TryGetValue(recipe.resultItemId, out BuildCatalogItem resultItem))
            {
                SetStatusMessage("Recipe result not found in catalog.");
                return;
            }

            SpendCraftingCost(recipe.cost);

            if (recipe.resultIsStructure)
            {
                // Structure recipes go through the build/placement flow
                SetStatusMessage($"Crafted {recipe.displayName} — use Build menu to place it.");
                // Refund the cost into a "free" placement by giving resources back
                // Actually, for structures we just let them build with the build system.
                // The cost was already spent — mark it as available in build mode.
                // Simplest approach: add as a free-place credit by just opening build panel.
                // Better: directly add to inventory as a placeable item credit.
                // Most coherent: the build system already charges BuildCost. Since we just
                // spent crafting cost, refund the build cost equivalent so the player can
                // place it for free. But this is complex. Simplest: give the resources back
                // and let build system charge them. That means crafting structures is pointless.
                //
                // Best approach: add the item to weapon inventory as a "structure kit" that
                // the player can then place.
                AddInventoryCount(storedWeaponInventory, recipe.resultItemId, recipe.resultCount);
            }
            else
            {
                // Item/pickup recipes go to the appropriate inventory
                if (resultItem.pickupInventoryType == PickupInventoryType.Food)
                {
                    AddInventoryCount(storedFoodInventory, recipe.resultItemId, recipe.resultCount);
                }
                else
                {
                    AddInventoryCount(storedWeaponInventory, recipe.resultItemId, recipe.resultCount);
                }
            }

            AwardSkillXp(SkillType.Crafting, 20L * recipe.resultCount);
            SetStatusMessage($"Crafted {recipe.resultCount}x {recipe.displayName}!");
            SaveWorld();
        }

        private string GetRecipeCostDisplay(CraftingRecipe recipe)
        {
            if (recipe?.cost == null) return "Free";
            CraftingIngredient c = recipe.cost;
            List<string> parts = new();
            if (c.wood > 0) parts.Add($"{c.wood} wood");
            if (c.stone > 0) parts.Add($"{c.stone} stone");
            if (c.food > 0) parts.Add($"{c.food} food");
            if (!string.IsNullOrWhiteSpace(c.requiredItemId) && c.requiredItemCount > 0)
            {
                string name = GetDisplayNameForItemId(c.requiredItemId);
                parts.Add($"{c.requiredItemCount}x {name}");
            }
            return parts.Count == 0 ? "Free" : string.Join("  ·  ", parts);
        }

        private string FormatInventorySummary(Dictionary<string, int> inventory, int maxEntries)
        {
            if (inventory == null || inventory.Count == 0 || maxEntries <= 0)
            {
                return "Empty";
            }

            List<string> parts = new List<string>();

            foreach (KeyValuePair<string, int> pair in inventory
                .Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key))
            {
                string label = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                parts.Add($"{label} x{pair.Value}");
            }

            if (parts.Count == 0)
            {
                return "Empty";
            }

            int shownCount = Mathf.Min(maxEntries, parts.Count);
            string summary = string.Join(", ", parts.Take(shownCount));

            if (parts.Count > shownCount)
            {
                summary += $" +{parts.Count - shownCount} more";
            }

            return summary;
        }

        private bool TryOpenContainerStorage(PlacedWorldObject placedObject)
        {
            if (placedObject == null || !TryGetContainerDefinition(placedObject, out _, out _))
            {
                return false;
            }

            activeContainerObject = placedObject;
            containerStorageOpen = true;
            pointerMode = true;
            containerTransferAmount = 1;

            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
            {
                playerMovement.InputSuppressed = true;
            }

            SetStatusMessage($"Opened {GetContainerDisplayName(placedObject)}.");
            return true;
        }

        private void CloseContainerStorage()
        {
            containerStorageOpen = false;
            activeContainerObject = null;
            pointerMode = false;

            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
            {
                playerMovement.InputSuppressed = false;
            }
        }

        private bool TryGetActiveContainer(out PlacedWorldObject placedObject, out ContainerRuntimeStorage storage, out int capacity)
        {
            placedObject = activeContainerObject;
            storage = null;
            capacity = 0;

            if (!containerStorageOpen || placedObject == null || string.IsNullOrWhiteSpace(placedObject.UniqueId))
            {
                return false;
            }

            if (!TryGetContainerDefinition(placedObject, out _, out capacity))
            {
                return false;
            }

            storage = GetOrCreateContainerStorage(placedObject.UniqueId);
            return storage != null;
        }

        private bool TryGetContainerDefinition(PlacedWorldObject placedObject, out string label, out int capacity)
        {
            label = string.Empty;
            capacity = 0;

            if (placedObject == null)
            {
                return false;
            }

            string catalogId = placedObject.CatalogItemId ?? string.Empty;
            string prefabName = string.Empty;
            if (catalogLookup.TryGetValue(catalogId, out BuildCatalogItem catalogItem))
            {
                prefabName = catalogItem.prefabName ?? string.Empty;
            }

            if (string.Equals(catalogId, "Town:Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prefabName, "Barrel_01_LITE", StringComparison.OrdinalIgnoreCase))
            {
                label = "Barrel";
                capacity = BarrelStorageCapacity;
                return true;
            }

            if (string.Equals(catalogId, "Farm:Box_01", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prefabName, "Box_01", StringComparison.OrdinalIgnoreCase))
            {
                label = "Box";
                capacity = BoxStorageCapacity;
                return true;
            }

            return false;
        }

        private bool IsContainerCatalogItem(BuildCatalogItem item)
        {
            if (item == null)
            {
                return false;
            }

            return string.Equals(item.id, "Town:Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.prefabName, "Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.id, "Farm:Box_01", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.prefabName, "Box_01", StringComparison.OrdinalIgnoreCase);
        }

        private string GetContainerDisplayName(PlacedWorldObject placedObject)
        {
            return TryGetContainerDefinition(placedObject, out string label, out _) ? label : "Container";
        }

        private ContainerRuntimeStorage GetOrCreateContainerStorage(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                return null;
            }

            if (!containerStorageByObjectId.TryGetValue(uniqueId, out ContainerRuntimeStorage storage) || storage == null)
            {
                storage = new ContainerRuntimeStorage();
                containerStorageByObjectId[uniqueId] = storage;
            }

            return storage;
        }

        private int GetContainerUsedCapacity(ContainerRuntimeStorage storage)
        {
            if (storage == null)
            {
                return 0;
            }

            return Mathf.Max(0, storage.wood)
                   + Mathf.Max(0, storage.stone)
                   + Mathf.Max(0, storage.food)
                   + storage.storedFoodItems.Values.Where(v => v > 0).Sum()
                   + storage.storedWeapons.Values.Where(v => v > 0).Sum();
        }

        private ContainerStorageSaveData CaptureContainerStorage(PlacedWorldObject placedObject)
        {
            if (placedObject == null || !TryGetContainerDefinition(placedObject, out _, out _))
            {
                return null;
            }

            ContainerRuntimeStorage runtime = GetOrCreateContainerStorage(placedObject.UniqueId);
            return new ContainerStorageSaveData
            {
                wood = Mathf.Max(0, runtime.wood),
                stone = Mathf.Max(0, runtime.stone),
                food = Mathf.Max(0, runtime.food),
                storedFoodItems = runtime.storedFoodItems
                    .Where(entry => entry.Value > 0)
                    .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                    .ToList(),
                storedWeapons = runtime.storedWeapons
                    .Where(entry => entry.Value > 0)
                    .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                    .ToList()
            };
        }

        private void RestoreContainerStorage(ContainerRuntimeStorage runtimeStorage, ContainerStorageSaveData saveData)
        {
            if (runtimeStorage == null)
            {
                return;
            }

            runtimeStorage.wood = Mathf.Max(0, saveData?.wood ?? 0);
            runtimeStorage.stone = Mathf.Max(0, saveData?.stone ?? 0);
            runtimeStorage.food = Mathf.Max(0, saveData?.food ?? 0);
            RestoreInventory(runtimeStorage.storedFoodItems, saveData?.storedFoodItems);
            RestoreInventory(runtimeStorage.storedWeapons, saveData?.storedWeapons);
        }

        private void DepositResourceToContainer(ResourceType resourceType, ContainerRuntimeStorage containerStorage, int capacity)
        {
            if (containerStorage == null)
            {
                return;
            }

            int free = Mathf.Max(0, capacity - GetContainerUsedCapacity(containerStorage));
            if (free <= 0)
            {
                SetStatusMessage("Container is full.");
                return;
            }

            int requested = Mathf.Max(1, containerTransferAmount);
            int moved = 0;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    moved = Mathf.Min(requested, Mathf.Min(free, wood));
                    wood -= moved;
                    containerStorage.wood += moved;
                    break;
                case ResourceType.Stone:
                    moved = Mathf.Min(requested, Mathf.Min(free, stone));
                    stone -= moved;
                    containerStorage.stone += moved;
                    break;
                case ResourceType.Food:
                    moved = Mathf.Min(requested, Mathf.Min(free, food));
                    food -= moved;
                    containerStorage.food += moved;
                    break;
            }

            if (moved > 0)
            {
                SetStatusMessage($"Deposited {moved} {resourceType.ToString().ToLowerInvariant()}.");
                SaveWorld();
            }
        }

        private void WithdrawResourceFromContainer(ResourceType resourceType, ContainerRuntimeStorage containerStorage)
        {
            if (containerStorage == null)
            {
                return;
            }

            int requested = Mathf.Max(1, containerTransferAmount);
            int moved = 0;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    moved = Mathf.Min(requested, containerStorage.wood);
                    containerStorage.wood -= moved;
                    wood += moved;
                    break;
                case ResourceType.Stone:
                    moved = Mathf.Min(requested, containerStorage.stone);
                    containerStorage.stone -= moved;
                    stone += moved;
                    break;
                case ResourceType.Food:
                    moved = Mathf.Min(requested, containerStorage.food);
                    containerStorage.food -= moved;
                    food += moved;
                    break;
            }

            if (moved > 0)
            {
                SetStatusMessage($"Withdrew {moved} {resourceType.ToString().ToLowerInvariant()}.");
                SaveWorld();
            }
        }

        private void DepositItemToContainer(string itemId, Dictionary<string, int> sourceInventory, Dictionary<string, int> containerInventory, int capacity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || sourceInventory == null || containerInventory == null)
            {
                return;
            }

            if (!sourceInventory.TryGetValue(itemId, out int available) || available <= 0)
            {
                return;
            }

            if (!TryGetActiveContainer(out _, out ContainerRuntimeStorage storage, out _))
            {
                return;
            }

            int free = Mathf.Max(0, capacity - GetContainerUsedCapacity(storage));
            if (free <= 0)
            {
                SetStatusMessage("Container is full.");
                return;
            }

            int move = Mathf.Min(available, Mathf.Min(containerTransferAmount, free));
            if (move <= 0)
            {
                return;
            }

            RemoveInventoryCount(sourceInventory, itemId, move);
            AddInventoryCount(containerInventory, itemId, move);
            SetStatusMessage($"Deposited {move}x {GetDisplayNameForItemId(itemId)}.");
            SaveWorld();
        }

        private void WithdrawItemFromContainer(string itemId, Dictionary<string, int> containerInventory, Dictionary<string, int> targetInventory)
        {
            if (string.IsNullOrWhiteSpace(itemId) || containerInventory == null || targetInventory == null)
            {
                return;
            }

            if (!containerInventory.TryGetValue(itemId, out int available) || available <= 0)
            {
                return;
            }

            int move = Mathf.Min(available, Mathf.Max(1, containerTransferAmount));
            RemoveInventoryCount(containerInventory, itemId, move);
            AddInventoryCount(targetInventory, itemId, move);
            SetStatusMessage($"Withdrew {move}x {GetDisplayNameForItemId(itemId)}.");
            SaveWorld();
        }

        private void HandleContainerStorageNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame)
            {
                CloseContainerStorage();
            }
        }
    }
}
