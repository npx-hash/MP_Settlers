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
        private void LoadCatalog()
        {
            catalogDatabase = Resources.Load<BuildCatalogDatabase>(CatalogResourcePath);
            if (catalogDatabase == null)
            {
                Debug.LogWarning("MP Settlers could not load BuildCatalogDatabase from Resources/Generated.");
            }
        }

        private void BuildCatalogIndexes()
        {
            catalogLookup.Clear();
            catalogPrefabLookup.Clear();
            itemsByCategory.Clear();

            foreach (BuildCategory category in Enum.GetValues(typeof(BuildCategory)))
            {
                itemsByCategory[category] = new List<BuildCatalogItem>();
            }

            if (catalogDatabase == null)
            {
                return;
            }

            foreach (BuildCatalogItem item in catalogDatabase.Items)
            {
                if (item == null || item.prefab == null || string.IsNullOrWhiteSpace(item.id))
                {
                    continue;
                }

                catalogLookup[item.id] = item;
                if (!string.IsNullOrWhiteSpace(item.prefabName))
                {
                    catalogPrefabLookup[item.prefabName] = item;
                }

                itemsByCategory[item.category].Add(item);
            }

            foreach (List<BuildCatalogItem> categoryItems in itemsByCategory.Values)
            {
                categoryItems.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.Ordinal));
            }

            RegisterSeedItems();
            EnsureSelectedCategoryAvailable();

            int previousIndex = selectedIndex;
            EnsureSelectionInRange();
            if (selectedIndex != previousIndex)
            {
                CenterBuildScrollOnSelection();
            }

            if (placementActive)
            {
                EnsureGhost(GetSelectedItem());
            }
        }

        private void RegisterSeedItems()
        {
            seedToCropMap.Clear();

            // Define seed→crop mappings. Each seed is a virtual inventory item
            // that, when planted, spawns its associated crop catalog entry at growth 0.
            RegisterSeed("seed:tomato", "Tomato Seeds", "TomatoPlant_01");
            RegisterSeed("seed:cabbage", "Cabbage Seeds", "Cabbage_01");
        }

        private void RegisterSeed(string seedId, string seedDisplayName, string cropPrefabName)
        {
            if (!TryGetCatalogItemByPrefabName(cropPrefabName, out BuildCatalogItem cropItem))
                return;

            seedToCropMap[seedId] = cropItem.id;

            // Register a virtual catalog entry for the seed so GetDisplayNameForItemId works
            // and inventory UI can show it properly. Seeds are food-type inventory items.
            if (!catalogLookup.ContainsKey(seedId))
            {
                BuildCatalogItem seedItem = new()
                {
                    id = seedId,
                    prefabName = seedId,
                    displayName = seedDisplayName,
                    category = BuildCategory.Farm,
                    kind = ItemKind.Pickup,
                    prefab = cropItem.prefab, // reuse crop prefab for icon/ghost
                    cost = new BuildCost(),
                    pickupInventoryType = PickupInventoryType.Food,
                    renewableResourceType = ResourceType.Food,
                    renewableYieldAmount = 0,
                    renewableRegrowSeconds = 0f,
                    renewableVisualMode = RenewableVisualMode.Crop,
                    showGrowthLabel = false,
                    seedOnNewWorld = false
                };
                catalogLookup[seedId] = seedItem;
            }
        }

        private bool IsSeedItem(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId) && seedToCropMap.ContainsKey(itemId);
        }

        private bool TryGetCatalogItemByPrefabName(string prefabName, out BuildCatalogItem item)
        {
            item = null;
            return !string.IsNullOrWhiteSpace(prefabName) &&
                   catalogPrefabLookup.TryGetValue(prefabName, out item);
        }

        private BuildCategory[] GetAvailableBuildCategories()
        {
            return BuildCategoryOrder
                .Where(category => itemsByCategory.TryGetValue(category, out List<BuildCatalogItem> items) && items.Count > 0)
                .ToArray();
        }

        private void EnsureSelectedCategoryAvailable()
        {
            if (GetItemsForSelectedCategory().Count > 0)
            {
                return;
            }

            BuildCategory[] availableCategories = GetAvailableBuildCategories();
            if (availableCategories.Length > 0)
            {
                selectedCategory = availableCategories[0];
            }
        }
    }
}
