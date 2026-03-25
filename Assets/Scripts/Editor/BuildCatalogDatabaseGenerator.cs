using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MPSettlers.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MPSettlers.EditorTools
{
    [InitializeOnLoad]
    public static class BuildCatalogDatabaseGenerator
    {
        private const string CatalogAssetPath = "Assets/Resources/Generated/BuildCatalogDatabase.asset";

        private static readonly (string rootPath, BuildCategory category)[] PackMappings =
        {
            ("Assets/Downloaded/FantasyMedievalTown_LITE/Prefabs", BuildCategory.Town),
            ("Assets/Downloaded/LowPolyFarmLite/Prefabs", BuildCategory.Farm),
            ("Assets/Downloaded/LowPolyFoodLite/Prefabs", BuildCategory.Food),
            ("Assets/Downloaded/LowPolyRPGWeapons_Lite/Prefabs", BuildCategory.Weapons)
        };

        static BuildCatalogDatabaseGenerator()
        {
            EditorApplication.delayCall += EnsureCatalogGenerated;
        }

        [MenuItem("Tools/MP Settlers/Rebuild Build Catalog")]
        public static void RebuildCatalog()
        {
            BuildCatalogDatabase catalog = LoadOrCreateCatalogAsset();
            List<BuildCatalogItem> items = new();

            foreach ((string rootPath, BuildCategory category) in PackMappings)
            {
                if (!AssetDatabase.IsValidFolder(rootPath))
                {
                    continue;
                }

                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { rootPath });
                foreach (string prefabGuid in prefabGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                    if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab == null)
                    {
                        continue;
                    }

                    BuildCatalogItem item = CreateCatalogItem(prefab, category);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
            }

            items = items
                .OrderBy(item => item.category)
                .ThenBy(item => item.displayName)
                .ToList();

            catalog.SetItems(items);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureCatalogGenerated()
        {
            EditorApplication.delayCall -= EnsureCatalogGenerated;

            BuildCatalogDatabase existingCatalog = AssetDatabase.LoadAssetAtPath<BuildCatalogDatabase>(CatalogAssetPath);
            if (existingCatalog == null || existingCatalog.Items == null || existingCatalog.Items.Count == 0)
            {
                RebuildCatalog();
                return;
            }

            int expectedItemCount = PackMappings
                .Where(mapping => AssetDatabase.IsValidFolder(mapping.rootPath))
                .Sum(mapping => AssetDatabase.FindAssets("t:Prefab", new[] { mapping.rootPath }).Length);

            if (existingCatalog.Items.Count != expectedItemCount)
            {
                RebuildCatalog();
            }
        }

        private static BuildCatalogDatabase LoadOrCreateCatalogAsset()
        {
            BuildCatalogDatabase catalog = AssetDatabase.LoadAssetAtPath<BuildCatalogDatabase>(CatalogAssetPath);
            if (catalog != null)
            {
                return catalog;
            }

            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Generated");

            catalog = ScriptableObject.CreateInstance<BuildCatalogDatabase>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(folder))
            {
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        private static BuildCatalogItem CreateCatalogItem(GameObject prefab, BuildCategory category)
        {
            if (prefab == null)
            {
                return null;
            }

            string prefabName = prefab.name;
            BuildCatalogItem item = new()
            {
                prefab = prefab,
                prefabName = prefabName,
                displayName = ObjectNames.NicifyVariableName(prefabName),
                category = category,
                id = $"{category}:{prefabName}"
            };

            item.kind = ResolveItemKind(category, prefabName);
            item.cost = ResolveCost(category, prefabName);
            item.pickupInventoryType = ResolvePickupType(category);
            ResolveRenewableSettings(item, prefabName);
            return item;
        }

        private static ItemKind ResolveItemKind(BuildCategory category, string prefabName)
        {
            if (category == BuildCategory.Food || category == BuildCategory.Weapons)
            {
                return ItemKind.Pickup;
            }

            return prefabName switch
            {
                "Tree_04" => ItemKind.RenewableNode,
                "Rock_04" => ItemKind.RenewableNode,
                "TomatoPlant_01" => ItemKind.RenewableNode,
                "Cabbage_01" => ItemKind.RenewableNode,
                _ => ItemKind.Structure
            };
        }

        private static PickupInventoryType ResolvePickupType(BuildCategory category)
        {
            return category switch
            {
                BuildCategory.Food => PickupInventoryType.Food,
                BuildCategory.Weapons => PickupInventoryType.Weapon,
                _ => PickupInventoryType.None
            };
        }

        private static BuildCost ResolveCost(BuildCategory category, string prefabName)
        {
            if (prefabName == "Floor_01_LITE")
            {
                return new BuildCost { wood = 1, stone = 1 };
            }

            if (prefabName == "Fence_01_LITE")
            {
                return new BuildCost { wood = 2 };
            }

            if (prefabName == "House_01_LITE")
            {
                return new BuildCost { wood = 12, stone = 8 };
            }

            if (prefabName == "Tree_04")
            {
                return new BuildCost { wood = 2 };
            }

            if (prefabName == "Rock_04")
            {
                return new BuildCost { stone = 2 };
            }

            if (prefabName == "TomatoPlant_01" || prefabName == "Cabbage_01")
            {
                return new BuildCost { food = 1 };
            }

            return category switch
            {
                BuildCategory.Town => new BuildCost { wood = 4, stone = 2 },
                BuildCategory.Farm => new BuildCost { wood = 2, stone = 1 },
                BuildCategory.Food => new BuildCost { food = 1 },
                BuildCategory.Weapons => new BuildCost { wood = 2, stone = 2 },
                _ => new BuildCost()
            };
        }

        private static void ResolveRenewableSettings(BuildCatalogItem item, string prefabName)
        {
            if (item.kind != ItemKind.RenewableNode)
            {
                item.renewableYieldAmount = 0;
                item.seedOnNewWorld = false;
                item.showGrowthLabel = false;
                item.renewableVisualMode = RenewableVisualMode.Generic;
                return;
            }

            item.seedOnNewWorld = true;

            switch (prefabName)
            {
                case "Tree_04":
                    item.renewableResourceType = ResourceType.Wood;
                    item.renewableYieldAmount = 4;
                    item.renewableRegrowSeconds = 60f;
                    item.renewableVisualMode = RenewableVisualMode.Tree;
                    item.showGrowthLabel = true;
                    break;

                case "Rock_04":
                    item.renewableResourceType = ResourceType.Stone;
                    item.renewableYieldAmount = 3;
                    item.renewableRegrowSeconds = 75f;
                    item.renewableVisualMode = RenewableVisualMode.Rock;
                    item.showGrowthLabel = false;
                    break;

                case "TomatoPlant_01":
                case "Cabbage_01":
                    item.renewableResourceType = ResourceType.Food;
                    item.renewableYieldAmount = 2;
                    item.renewableRegrowSeconds = 45f;
                    item.renewableVisualMode = RenewableVisualMode.Crop;
                    item.showGrowthLabel = true;
                    break;
            }
        }
    }
}
