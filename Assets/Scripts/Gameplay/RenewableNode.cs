using UnityEngine;

namespace MPSettlers.Gameplay
{
    [DisallowMultipleComponent]
    public class RenewableNode : MonoBehaviour
    {
        [SerializeField] private ResourceType resourceType;
        [SerializeField] private int yieldAmount = 1;
        [SerializeField] private float regrowSeconds = 45f;
        [SerializeField] private RenewableVisualMode visualMode = RenewableVisualMode.Generic;
        [SerializeField] private bool showGrowthLabel;
        [SerializeField, Range(0f, 1f)] private float growthNormalized = 1f;

        private PlacedWorldObject placedWorldObject;
        private Vector3 baseScale = Vector3.one;

        public ResourceType ResourceType => resourceType;
        public int YieldAmount => yieldAmount;
        public float GrowthNormalized => growthNormalized;
        public bool IsHarvestable => growthNormalized >= 0.999f;
        public bool ShowGrowthLabel => showGrowthLabel;
        public float RemainingRegrowSeconds => IsHarvestable ? 0f : Mathf.Max(0f, regrowSeconds * (1f - growthNormalized));

        private void Awake()
        {
            placedWorldObject = GetComponent<PlacedWorldObject>();
            baseScale = transform.localScale;
            ApplyVisualState();
        }

        private void Update()
        {
            if (IsHarvestable || regrowSeconds <= 0f)
            {
                return;
            }

            growthNormalized = Mathf.Clamp01(growthNormalized + (Time.deltaTime / regrowSeconds));
            ApplyVisualState();
        }

        public void Initialize(BuildCatalogItem item, float initialGrowth)
        {
            if (item == null)
            {
                return;
            }

            placedWorldObject = GetComponent<PlacedWorldObject>();
            baseScale = transform.localScale;
            resourceType = item.renewableResourceType;
            yieldAmount = Mathf.Max(1, item.renewableYieldAmount);
            regrowSeconds = Mathf.Max(1f, item.renewableRegrowSeconds);
            visualMode = item.renewableVisualMode;
            showGrowthLabel = item.showGrowthLabel;
            growthNormalized = Mathf.Clamp01(initialGrowth);
            ApplyVisualState();
        }

        public bool TryHarvest(out int collectedAmount)
        {
            collectedAmount = 0;
            if (!IsHarvestable)
            {
                return false;
            }

            collectedAmount = yieldAmount;
            growthNormalized = 0f;
            ApplyVisualState();
            return true;
        }

        public RenewableNodeState CaptureState()
        {
            return new RenewableNodeState
            {
                growthNormalized = growthNormalized,
                depleted = !IsHarvestable
            };
        }

        public string GetStatusLabel()
        {
            if (IsHarvestable)
            {
                return visualMode switch
                {
                    RenewableVisualMode.Tree => "Ready to chop",
                    RenewableVisualMode.Rock => "Ready to mine",
                    RenewableVisualMode.Crop => "Ready to harvest",
                    _ => "Ready"
                };
            }

            if (!showGrowthLabel)
            {
                return string.Empty;
            }

            int percent = Mathf.RoundToInt(growthNormalized * 100f);
            return $"Growing {percent}%";
        }

        private void ApplyVisualState()
        {
            if (placedWorldObject == null)
            {
                placedWorldObject = GetComponent<PlacedWorldObject>();
            }

            switch (visualMode)
            {
                case RenewableVisualMode.Tree:
                    placedWorldObject?.SetRenderersEnabled(true);
                    placedWorldObject?.SetCollidersEnabled(true);
                    transform.localScale = new Vector3(baseScale.x, Mathf.Lerp(baseScale.y * 0.18f, baseScale.y, growthNormalized), baseScale.z);
                    break;

                case RenewableVisualMode.Crop:
                    placedWorldObject?.SetRenderersEnabled(true);
                    placedWorldObject?.SetCollidersEnabled(true);
                    float cropScale = Mathf.Lerp(0.2f, 1f, growthNormalized);
                    transform.localScale = baseScale * cropScale;
                    break;

                case RenewableVisualMode.Rock:
                    bool rockVisible = IsHarvestable;
                    placedWorldObject?.SetRenderersEnabled(rockVisible);
                    placedWorldObject?.SetCollidersEnabled(rockVisible);
                    transform.localScale = baseScale;
                    break;

                default:
                    placedWorldObject?.SetRenderersEnabled(true);
                    placedWorldObject?.SetCollidersEnabled(true);
                    transform.localScale = Vector3.Lerp(baseScale * 0.2f, baseScale, growthNormalized);
                    break;
            }
        }
    }
}
