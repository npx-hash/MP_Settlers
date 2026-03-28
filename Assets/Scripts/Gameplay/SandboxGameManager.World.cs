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
        private void LoadOrInitializeWorld()
        {
            ClearRuntimeObjects();

            if (catalogDatabase == null)
            {
                return;
            }

            string savePath = GetSavePath();
            if (File.Exists(savePath))
            {
                try
                {
                    WorldSaveData saveData = JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(savePath));
                    ApplySaveData(saveData);
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"MP Settlers failed to load save data. Starting a fresh sandbox. {exception.Message}");
                }
            }

            health = MaxHealth;
            wood = 0;
            stone = 0;
            food = 0;
            expandedEnvironmentSeeded = false;
            storedFoodInventory.Clear();
            storedWeaponInventory.Clear();
            foreach (string key in EquipSlotKey.All)
            {
                equippedSlots[key] = new EquippedSlot();
            }
            Array.Clear(favoriteHotbarItemIds, 0, favoriteHotbarItemIds.Length);
            selectedHotbarIndex = 0;

            SeedStarterNodes();
            SeedExpandedEnvironment();
            expandedEnvironmentSeeded = true;
            SaveWorld();
        }

        private void ApplySaveData(WorldSaveData saveData)
        {
            WorldSaveData safeData = saveData ?? new WorldSaveData();

            health = safeData.health <= 0 ? MaxHealth : Mathf.Clamp(safeData.health, 0, MaxHealth);
            wood = Mathf.Max(0, safeData.wood);
            stone = Mathf.Max(0, safeData.stone);
            food = Mathf.Max(0, safeData.food);
            expandedEnvironmentSeeded = safeData.expandedEnvironmentSeeded;
            selectedHotbarIndex = Mathf.Clamp(safeData.selectedHotbarIndex, 0, HotbarSlotCount - 1);

            RestoreInventory(storedFoodInventory, safeData.inventory?.storedFoodItems);
            RestoreInventory(storedWeaponInventory, safeData.inventory?.storedWeapons);
            RestoreHotbar(safeData.favoriteHotbarItemIds);

            containerStorageByObjectId.Clear();

            foreach (string key in EquipSlotKey.All)
                equippedSlots[key] = new EquippedSlot();

            if (safeData.equipment?.slots != null)
            {
                foreach (EquippedSlotSaveData savedSlot in safeData.equipment.slots)
                {
                    if (string.IsNullOrWhiteSpace(savedSlot.catalogItemId))
                        continue;

                    if (!catalogLookup.TryGetValue(savedSlot.catalogItemId, out BuildCatalogItem item))
                        continue;

                    EquipmentInfo eqInfo = GetEquipmentInfo(item);

                    if (eqInfo == null)
                        continue;

                    EquippedSlot slot = new EquippedSlot
                    {
                        catalogItemId = savedSlot.catalogItemId,
                        weaponType = eqInfo.weaponType,
                        armorSlot = eqInfo.armorSlot,
                        ammoType = eqInfo.ammoType,
                        stats = eqInfo.stats ?? new EquipmentStats()
                    };

                    equippedSlots[savedSlot.slotKey] = slot;
                    MountEquipmentOnPlayer(savedSlot.slotKey, item, eqInfo);
                }
            }

            playerSkills.Restore(safeData.skills);

            if (safeData.placedObjects != null)
            {
                foreach (PlacedObjectSaveData placedObject in safeData.placedObjects)
                {
                    if (placedObject == null)
                    {
                        continue;
                    }

                    if (!catalogLookup.TryGetValue(placedObject.catalogItemId, out BuildCatalogItem item))
                    {
                        continue;
                    }

                    float growth = placedObject.renewableNodeState != null
                        ? Mathf.Clamp01(placedObject.renewableNodeState.growthNormalized)
                        : 1f;

                    GameObject spawnedObject = SpawnCatalogItem(item, placedObject.uniqueId, placedObject.position, placedObject.rotation, growth, registerForSave: true, placedByPlayer: placedObject.placedByPlayer);

                    if (spawnedObject != null && placedObject.pickupStackCount > 1)
                    {
                        InventoryPickup restoredPickup = spawnedObject.GetComponent<InventoryPickup>();
                        if (restoredPickup != null)
                        {
                            restoredPickup.SetStackCount(placedObject.pickupStackCount);
                        }
                    }

                    if (IsContainerCatalogItem(item))
                    {
                        ContainerRuntimeStorage runtimeStorage = GetOrCreateContainerStorage(placedObject.uniqueId);
                        RestoreContainerStorage(runtimeStorage, placedObject.containerStorage);
                    }
                }
            }

            if ((safeData.placedObjects == null || safeData.placedObjects.Count == 0) && !safeData.starterNodesSeeded)
            {
                SeedStarterNodes();
            }

            if (!expandedEnvironmentSeeded)
            {
                SeedExpandedEnvironment();
                expandedEnvironmentSeeded = true;
            }

            if (((safeData.placedObjects == null || safeData.placedObjects.Count == 0) && !safeData.starterNodesSeeded) || !safeData.expandedEnvironmentSeeded)
            {
                SaveWorld();
            }
        }

        private void RestoreInventory(Dictionary<string, int> targetInventory, List<InventoryEntryData> entries)
        {
            targetInventory.Clear();
            if (entries == null)
            {
                return;
            }

            foreach (InventoryEntryData entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.count <= 0)
                {
                    continue;
                }

                targetInventory[entry.itemId] = entry.count;
            }
        }

        private void RestoreHotbar(List<string> itemIds)
        {
            Array.Clear(favoriteHotbarItemIds, 0, favoriteHotbarItemIds.Length);
            if (itemIds == null)
            {
                return;
            }

            for (int i = 0; i < HotbarSlotCount && i < itemIds.Count; i++)
            {
                string itemId = itemIds[i];
                if (string.IsNullOrWhiteSpace(itemId) || !catalogLookup.ContainsKey(itemId))
                {
                    continue;
                }

                favoriteHotbarItemIds[i] = itemId;
            }
        }

        private void SaveWorld()
        {
            if (catalogDatabase == null)
            {
                return;
            }

            WorldSaveData saveData = new()
            {
                wood = wood,
                stone = stone,
                food = food,
                health = health,
                starterNodesSeeded = true,
                expandedEnvironmentSeeded = expandedEnvironmentSeeded,
                selectedHotbarIndex = selectedHotbarIndex,
                inventory = new InventorySaveData
                {
                    storedFoodItems = storedFoodInventory
                        .Where(entry => entry.Value > 0)
                        .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                        .ToList(),
                    storedWeapons = storedWeaponInventory
                        .Where(entry => entry.Value > 0)
                        .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                        .ToList()
                },
                favoriteHotbarItemIds = favoriteHotbarItemIds.ToList(),
                placedObjects = placedObjects.Values
                    .Where(placedObject => placedObject != null)
                    .Select(CapturePlacedObjectState)
                    .ToList()
            };

            saveData.equipment = new EquipmentSaveData
            {
                slots = equippedSlots
                    .Where(pair => !pair.Value.IsEmpty)
                    .Select(pair => new EquippedSlotSaveData
                    {
                        slotKey = pair.Key,
                        catalogItemId = pair.Value.catalogItemId
                    })
                    .ToList()
            };

            saveData.storage = new StorageSaveData();
            saveData.skills = playerSkills.Capture();

            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(GetSavePath(), json);
        }

        private PlacedObjectSaveData CapturePlacedObjectState(PlacedWorldObject placedObject)
        {
            RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
            InventoryPickup pickup = placedObject.GetComponent<InventoryPickup>();
            return new PlacedObjectSaveData
            {
                uniqueId = placedObject.UniqueId,
                catalogItemId = placedObject.CatalogItemId,
                position = placedObject.transform.position,
                rotation = placedObject.transform.rotation,
                placedByPlayer = placedObject.PlacedByPlayer,
                renewableNodeState = renewableNode != null ? renewableNode.CaptureState() : null,
                containerStorage = CaptureContainerStorage(placedObject),
                pickupStackCount = pickup != null ? pickup.StackCount : 0
            };
        }

        private string GetSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveFileName);
        }

        private void ClearRuntimeObjects()
        {
            CloseContainerStorage();
            placedObjects.Clear();
            containerStorageByObjectId.Clear();
            foreach (PlacedWorldObject placedWorldObject in FindObjectsByType<PlacedWorldObject>(FindObjectsInactive.Include))
            {
                if (placedWorldObject != null)
                {
                    Destroy(placedWorldObject.gameObject);
                }
            }
        }

        private void SeedStarterNodes()
        {
            Vector3 origin = playerTransform != null ? playerTransform.position : Vector3.zero;
            TrySeed("Tree_04", origin + new Vector3(6f, 0f, 8f), 20f);
            TrySeed("Tree_04", origin + new Vector3(-8f, 0f, 4f), -30f);
            TrySeed("Rock_04", origin + new Vector3(8f, 0f, -6f), 0f);
            TrySeed("Rock_04", origin + new Vector3(-6f, 0f, -8f), 30f);
            TrySeed("TomatoPlant_01", origin + new Vector3(4f, 0f, 11f), 0f);
            TrySeed("Cabbage_01", origin + new Vector3(-4f, 0f, 11f), 0f);

            // Give player starter seeds so they can begin farming immediately
            if (seedToCropMap.ContainsKey("seed:tomato"))
                AddInventoryCount(storedFoodInventory, "seed:tomato", 3);
            if (seedToCropMap.ContainsKey("seed:cabbage"))
                AddInventoryCount(storedFoodInventory, "seed:cabbage", 3);
        }

        private void SeedExpandedEnvironment()
        {
            Vector3 origin = playerTransform != null ? playerTransform.position : Vector3.zero;
            float terrainSpacing = 20f;

            Vector3[] terrainOffsets =
            {
                new(terrainSpacing, 0f, 0f),
                new(-terrainSpacing, 0f, 0f),
                new(0f, 0f, terrainSpacing),
                new(0f, 0f, -terrainSpacing),
                new(terrainSpacing, 0f, terrainSpacing),
                new(-terrainSpacing, 0f, terrainSpacing),
                new(terrainSpacing, 0f, -terrainSpacing),
                new(-terrainSpacing, 0f, -terrainSpacing)
            };

            foreach (Vector3 offset in terrainOffsets)
            {
                TrySeed("Terrain_01", origin + offset, 0f);
            }

            SeedEnvironmentCluster(origin + new Vector3(16f, 0f, 14f), 15f, "Tree_04", "Rock_04", "Tree_04");
            SeedEnvironmentCluster(origin + new Vector3(-17f, 0f, 12f), -20f, "Tree_04", "Rock_04", "Cabbage_01");
            SeedEnvironmentCluster(origin + new Vector3(18f, 0f, -15f), 30f, "Rock_04", "Tree_04", "TomatoPlant_01");
            SeedEnvironmentCluster(origin + new Vector3(-16f, 0f, -18f), -35f, "Tree_04", "Rock_04", "Tree_04");
            SeedEnvironmentCluster(origin + new Vector3(0f, 0f, 24f), 10f, "Tree_04", "Cabbage_01", "TomatoPlant_01");
            SeedEnvironmentCluster(origin + new Vector3(0f, 0f, -24f), -10f, "Rock_04", "Tree_04", "Rock_04");
        }

        private void SeedEnvironmentCluster(Vector3 center, float yaw, params string[] prefabNames)
        {
            Vector3[] localOffsets =
            {
                new(-3.5f, 0f, 2.5f),
                new(3.75f, 0f, 1.25f),
                new(0.5f, 0f, -3.75f)
            };

            for (int i = 0; i < prefabNames.Length && i < localOffsets.Length; i++)
            {
                TrySeed(prefabNames[i], center + localOffsets[i], yaw + (i * 17f));
            }
        }

        private void TrySeed(string prefabName, Vector3 approximatePosition, float yaw)
        {
            BuildCatalogItem item = catalogLookup.Values.FirstOrDefault(candidate => candidate.prefabName == prefabName);
            if (item == null)
            {
                return;
            }

            Vector3 groundedPosition = GetGroundedPosition(approximatePosition);
            SpawnCatalogItem(item, Guid.NewGuid().ToString("N"), groundedPosition, Quaternion.Euler(0f, yaw, 0f), 1f, registerForSave: true, placedByPlayer: false, alignToGround: true);
        }

        private GameObject SpawnCatalogItem(
            BuildCatalogItem item,
            string uniqueId,
            Vector3 position,
            Quaternion rotation,
            float growthNormalized,
            bool registerForSave,
            bool placedByPlayer = false,
            bool alignToGround = false)
        {
            if (item == null || item.prefab == null)
            {
                return null;
            }

            GameObject instanceObject = Instantiate(item.prefab, position, rotation);
            instanceObject.name = item.prefab.name;

            PlacedWorldObject placedWorldObject = instanceObject.GetComponent<PlacedWorldObject>();
            if (placedWorldObject == null)
            {
                placedWorldObject = instanceObject.AddComponent<PlacedWorldObject>();
            }

            placedWorldObject.Initialize(item, string.IsNullOrWhiteSpace(uniqueId) ? Guid.NewGuid().ToString("N") : uniqueId, placedByPlayer);
            if (alignToGround)
            {
                AlignObjectToGround(instanceObject);
            }

            RenewableNode renewableNode = instanceObject.GetComponent<RenewableNode>();
            InventoryPickup pickup = instanceObject.GetComponent<InventoryPickup>();

            if (item.kind == ItemKind.RenewableNode)
            {
                if (renewableNode == null)
                {
                    renewableNode = instanceObject.AddComponent<RenewableNode>();
                }

                renewableNode.Initialize(item, growthNormalized);
            }
            else if (renewableNode != null)
            {
                Destroy(renewableNode);
            }

            if (item.kind == ItemKind.Pickup)
            {
                if (pickup == null)
                {
                    pickup = instanceObject.AddComponent<InventoryPickup>();
                }

                pickup.Initialize(item);
            }
            else if (pickup != null)
            {
                Destroy(pickup);
            }

            if (registerForSave)
            {
                placedObjects[placedWorldObject.UniqueId] = placedWorldObject;
            }

            return instanceObject;
        }

        private Vector3 GetGroundedPosition(Vector3 approximatePosition)
        {
            Vector3 rayStart = approximatePosition + Vector3.up * 64f;
            Ray ray = new(rayStart, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 256f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                Transform hitTransform = hit.collider.transform;
                if (playerTransform != null && hitTransform.IsChildOf(playerTransform))
                {
                    continue;
                }

                return hit.point;
            }

            approximatePosition.y = 0f;
            return approximatePosition;
        }

        private void AlignObjectToGround(GameObject instanceObject)
        {
            if (instanceObject == null)
            {
                return;
            }

            if (!TryGetRenderableBounds(instanceObject, out Bounds bounds))
            {
                return;
            }

            float bottomOffset = instanceObject.transform.position.y - bounds.min.y;
            instanceObject.transform.position += Vector3.up * bottomOffset;
        }

        private bool TryGetRenderableBounds(GameObject instanceObject, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer rendererComponent in instanceObject.GetComponentsInChildren<Renderer>(true))
            {
                if (rendererComponent == null || !rendererComponent.enabled)
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

        private void SetLayerRecursively(Transform targetTransform, int layer)
        {
            targetTransform.gameObject.layer = layer;
            foreach (Transform child in targetTransform)
            {
                SetLayerRecursively(child, layer);
            }
        }

        private void UpdateTargetedObject()
        {
            targetedRenewable = null;
            targetedPickup = null;
            targetedPlacedObject = null;

            if (pointerMode || inGameMenuOpen || mainCamera == null)
            {
                return;
            }

            if (!TryGetCameraRaycast(InteractDistance, out RaycastHit hit))
            {
                return;
            }

            targetedRenewable = hit.collider.GetComponentInParent<RenewableNode>();
            targetedPickup = hit.collider.GetComponentInParent<InventoryPickup>();
            targetedPlacedObject = hit.collider.GetComponentInParent<PlacedWorldObject>();
        }
    }
}
