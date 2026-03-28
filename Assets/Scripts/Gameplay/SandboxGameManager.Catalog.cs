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
                itemsByCategory[item.category].Add(item);
            }

            foreach (List<BuildCatalogItem> categoryItems in itemsByCategory.Values)
            {
                categoryItems.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.Ordinal));
            }

            RegisterSeedItems();

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
            RegisterSeed("seed:tomato", "Tomato Seeds", "farm:TomatoPlant_01");
            RegisterSeed("seed:cabbage", "Cabbage Seeds", "farm:Cabbage_01");
        }

        private void RegisterSeed(string seedId, string seedDisplayName, string cropCatalogId)
        {
            if (!catalogLookup.ContainsKey(cropCatalogId))
                return;

            seedToCropMap[seedId] = cropCatalogId;

            // Register a virtual catalog entry for the seed so GetDisplayNameForItemId works
            // and inventory UI can show it properly. Seeds are food-type inventory items.
            if (!catalogLookup.ContainsKey(seedId))
            {
                BuildCatalogItem cropItem = catalogLookup[cropCatalogId];
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
    }
}
