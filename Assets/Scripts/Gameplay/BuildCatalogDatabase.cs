using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    [CreateAssetMenu(fileName = "BuildCatalogDatabase", menuName = "MP Settlers/Build Catalog Database")]
    public class BuildCatalogDatabase : ScriptableObject
    {
        [SerializeField] private List<BuildCatalogItem> items = new();

        public IReadOnlyList<BuildCatalogItem> Items => items;

        public BuildCatalogItem GetItemById(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return items.FirstOrDefault(item => item.id == itemId);
        }

        public List<BuildCatalogItem> GetItemsForCategory(BuildCategory category)
        {
            return items.Where(item => item.category == category).ToList();
        }

        public void SetItems(List<BuildCatalogItem> catalogItems)
        {
            items = catalogItems ?? new List<BuildCatalogItem>();
        }
    }
}
