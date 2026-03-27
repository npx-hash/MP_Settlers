using UnityEngine;

namespace MPSettlers.Gameplay
{
    [DisallowMultipleComponent]
    public class InventoryPickup : MonoBehaviour
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private PickupInventoryType inventoryType;
        [SerializeField] private int stackCount = 1;

        private PlacedWorldObject placedWorldObject;

        public string ItemId => itemId;
        public int StackCount => stackCount;

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : !string.IsNullOrWhiteSpace(itemId)
                    ? itemId
                    : gameObject.name;

        public PickupInventoryType InventoryType => inventoryType;

        private void Awake()
        {
            placedWorldObject = GetComponent<PlacedWorldObject>();
        }

        private void OnValidate()
        {
            if (placedWorldObject == null)
            {
                placedWorldObject = GetComponent<PlacedWorldObject>();
            }
        }

        public bool HasValidItemData()
        {
            return !string.IsNullOrWhiteSpace(itemId);
        }

        public void Initialize(BuildCatalogItem item)
        {
            if (item == null)
            {
                return;
            }

            placedWorldObject = GetComponent<PlacedWorldObject>();

            if (placedWorldObject != null && !string.IsNullOrWhiteSpace(placedWorldObject.CatalogItemId))
                itemId = placedWorldObject.CatalogItemId;
            else
                itemId = item.id;

            displayName = item.displayName;
            inventoryType = item.pickupInventoryType;
        }

        public void ForceRuntimeItemId(string runtimeItemId)
        {
            if (!string.IsNullOrWhiteSpace(runtimeItemId))
            {
                itemId = runtimeItemId;
            }
        }

        public void ForceRuntimeDisplayName(string runtimeDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(runtimeDisplayName))
            {
                displayName = runtimeDisplayName;
            }
        }

        public void ForceRuntimeInventoryType(PickupInventoryType runtimeInventoryType)
        {
            inventoryType = runtimeInventoryType;
        }

        public void AddToStack(int amount)
        {
            stackCount = Mathf.Max(1, stackCount + amount);
        }

        public bool DecrementStack()
        {
            stackCount--;
            return stackCount <= 0;
        }

        public void SetStackCount(int count)
        {
            stackCount = Mathf.Max(1, count);
        }

        public string GetInteractionLabel()
        {
            if (stackCount > 1)
                return $"Collect {DisplayName} x{stackCount}";
            return $"Collect {DisplayName}";
        }
    }
}