using UnityEngine;

namespace MPSettlers.Gameplay
{
    [DisallowMultipleComponent]
    public class InventoryPickup : MonoBehaviour
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private PickupInventoryType inventoryType;

        private PlacedWorldObject placedWorldObject;

        public string ItemId => itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;
        public PickupInventoryType InventoryType => inventoryType;

        private void Awake()
        {
            placedWorldObject = GetComponent<PlacedWorldObject>();
        }

        public void Initialize(BuildCatalogItem item)
        {
            if (item == null)
            {
                return;
            }

            placedWorldObject = GetComponent<PlacedWorldObject>();
            itemId = item.id;
            displayName = item.displayName;
            inventoryType = item.pickupInventoryType;
        }

        public string GetInteractionLabel()
        {
            return $"Collect {DisplayName}";
        }
    }
}
