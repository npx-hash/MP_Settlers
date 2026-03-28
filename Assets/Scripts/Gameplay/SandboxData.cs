using System;
using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    public enum ResourceType
    {
        Wood,
        Stone,
        Food
    }

    public enum BuildCategory
    {
        Town,
        Farm,
        Food,
        Weapons
    }

    public enum ItemKind
    {
        Structure,
        Pickup,
        RenewableNode
    }

    public enum PickupInventoryType
    {
        None,
        Food,
        Weapon
    }

    public enum RenewableVisualMode
    {
        Generic,
        Tree,
        Rock,
        Crop
    }

    [Serializable]
    public class BuildCost
    {
        public int wood;
        public int stone;
        public int food;

        public bool IsFree => wood <= 0 && stone <= 0 && food <= 0;

        public string ToDisplayString()
        {
            List<string> parts = new();
            if (wood > 0)
            {
                parts.Add($"{wood} wood");
            }

            if (stone > 0)
            {
                parts.Add($"{stone} stone");
            }

            if (food > 0)
            {
                parts.Add($"{food} food");
            }

            return parts.Count == 0 ? "Free" : string.Join(" / ", parts);
        }
    }

    [Serializable]
    public class BuildCatalogItem
    {
        public string id;
        public string prefabName;
        public string displayName;
        public BuildCategory category;
        public ItemKind kind;
        public GameObject prefab;
        public BuildCost cost = new();
        public PickupInventoryType pickupInventoryType;
        public ResourceType renewableResourceType;
        public int renewableYieldAmount = 1;
        public float renewableRegrowSeconds = 45f;
        public RenewableVisualMode renewableVisualMode = RenewableVisualMode.Generic;
        public bool showGrowthLabel;
        public bool seedOnNewWorld;
    }

    [Serializable]
    public class RenewableNodeState
    {
        public float growthNormalized = 1f;
        public bool depleted;
    }

    [Serializable]
    public class PlacedObjectSaveData
    {
        public string uniqueId;
        public string catalogItemId;
        public Vector3 position;
        public Quaternion rotation;
        public bool placedByPlayer;
        public RenewableNodeState renewableNodeState;
        public ContainerStorageSaveData containerStorage;
        public int pickupStackCount;
    }

    [Serializable]
    public class ContainerStorageSaveData
    {
        public int wood;
        public int stone;
        public int food;
        public List<InventoryEntryData> storedFoodItems = new();
        public List<InventoryEntryData> storedWeapons = new();
    }

    [Serializable]
    public class InventoryEntryData
    {
        public string itemId;
        public int count;
    }

    [Serializable]
    public class InventorySaveData
    {
        public List<InventoryEntryData> storedFoodItems = new();
        public List<InventoryEntryData> storedWeapons = new();
    }

    [Serializable]
    public class StorageSaveData
    {
        public int wood;
        public int stone;
        public int food;
        public List<InventoryEntryData> storedFoodItems = new();
        public List<InventoryEntryData> storedWeapons = new();
    }

    [Serializable]
    public class CraftingIngredient
    {
        public int wood;
        public int stone;
        public int food;
        public string requiredItemId;
        public int requiredItemCount;

        public string ToDisplayString()
        {
            List<string> parts = new();
            if (wood > 0) parts.Add($"{wood} wood");
            if (stone > 0) parts.Add($"{stone} stone");
            if (food > 0) parts.Add($"{food} food");
            if (!string.IsNullOrWhiteSpace(requiredItemId) && requiredItemCount > 0)
                parts.Add($"{requiredItemCount}x item");
            return parts.Count == 0 ? "Free" : string.Join(", ", parts);
        }
    }

    [Serializable]
    public class CraftingRecipe
    {
        public string id;
        public string displayName;
        public string description;
        public string resultItemId;
        public int resultCount = 1;
        public CraftingIngredient cost = new();
        public bool resultIsStructure;
    }

    [Serializable]
    public class WorldSaveData
    {
        public int wood;
        public int stone;
        public int food;
        public int health = 100;
        public bool starterNodesSeeded;
        public bool expandedEnvironmentSeeded;
        public InventorySaveData inventory = new();
        public List<string> favoriteHotbarItemIds = new();
        public int selectedHotbarIndex;
        public List<PlacedObjectSaveData> placedObjects = new();
        public EquipmentSaveData equipment = new EquipmentSaveData();
        public StorageSaveData storage = new StorageSaveData();
        public SkillsSaveData skills = new SkillsSaveData();
    }
}
