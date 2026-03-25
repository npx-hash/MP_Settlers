using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    [DisallowMultipleComponent]
    public class PlacedWorldObject : MonoBehaviour
    {
        [SerializeField] private string uniqueId;
        [SerializeField] private string catalogItemId;
        [SerializeField] private BuildCategory category;
        [SerializeField] private ItemKind itemKind;
        [SerializeField] private bool placedByPlayer;

        private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();
        private Collider[] cachedColliders = System.Array.Empty<Collider>();

        public string UniqueId => uniqueId;
        public string CatalogItemId => catalogItemId;
        public BuildCategory Category => category;
        public ItemKind ItemKind => itemKind;
        public bool PlacedByPlayer => placedByPlayer;

        public void Initialize(BuildCatalogItem item, string id, bool isPlacedByPlayer)
        {
            if (item == null)
            {
                return;
            }

            uniqueId = id;
            catalogItemId = item.id;
            category = item.category;
            itemKind = item.kind;
            placedByPlayer = isPlacedByPlayer;

            RefreshCaches();
            EnsureInteractionCollider();
        }

        public void RefreshCaches()
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider>(true);
        }

        public void SetRenderersEnabled(bool isEnabled)
        {
            foreach (Renderer rendererComponent in cachedRenderers)
            {
                if (rendererComponent != null)
                {
                    rendererComponent.enabled = isEnabled;
                }
            }
        }

        public void SetCollidersEnabled(bool isEnabled)
        {
            foreach (Collider colliderComponent in cachedColliders)
            {
                if (colliderComponent != null)
                {
                    colliderComponent.enabled = isEnabled;
                }
            }
        }

        public bool TryGetCombinedWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer rendererComponent in cachedRenderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererComponent.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererComponent.bounds);
                }
            }

            return hasBounds;
        }

        private void EnsureInteractionCollider()
        {
            if (cachedColliders.Length > 0)
            {
                return;
            }

            if (!TryGetCombinedLocalBounds(out Bounds localBounds))
            {
                return;
            }

            BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            boxCollider.center = localBounds.center;
            boxCollider.size = localBounds.size;

            RefreshCaches();
        }

        private bool TryGetCombinedLocalBounds(out Bounds localBounds)
        {
            localBounds = default;
            bool hasBounds = false;

            foreach (Renderer rendererComponent in GetComponentsInChildren<Renderer>(true))
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                Bounds rendererBounds = GetRendererLocalBounds(rendererComponent);
                if (!hasBounds)
                {
                    localBounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(rendererBounds);
                }
            }

            return hasBounds;
        }

        private Bounds GetRendererLocalBounds(Renderer rendererComponent)
        {
            Bounds sourceBounds = rendererComponent switch
            {
                SkinnedMeshRenderer skinnedMeshRenderer => skinnedMeshRenderer.localBounds,
                _ => rendererComponent.localBounds
            };

            Vector3 center = sourceBounds.center;
            Vector3 extents = sourceBounds.extents;
            Vector3[] corners =
            {
                center + new Vector3(extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, -extents.y, -extents.z)
            };

            Matrix4x4 toRootMatrix = transform.worldToLocalMatrix * rendererComponent.transform.localToWorldMatrix;
            Vector3 firstCorner = toRootMatrix.MultiplyPoint3x4(corners[0]);
            Bounds localBounds = new(firstCorner, Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                localBounds.Encapsulate(toRootMatrix.MultiplyPoint3x4(corners[i]));
            }

            return localBounds;
        }
    }
}
