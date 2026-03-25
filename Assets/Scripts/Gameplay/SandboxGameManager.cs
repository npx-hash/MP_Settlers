using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MPSettlers.CameraSystem;
using MPSettlers.Characters;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MPSettlers.Gameplay
{
    [DefaultExecutionOrder(-900)]
    public class SandboxGameManager : MonoBehaviour
    {
        private enum InGameMenuTab
        {
            Overview,
            Skills,
            Inventory,
            Settings
        }

        private readonly struct JournalInventoryEntry
        {
            public readonly string itemId;
            public readonly string displayName;
            public readonly int quantity;
            public readonly string categoryLabel;
            public readonly bool isFavorited;

            public JournalInventoryEntry(string itemId, string displayName, int quantity, string categoryLabel, bool isFavorited)
            {
                this.itemId = itemId;
                this.displayName = displayName;
                this.quantity = quantity;
                this.categoryLabel = categoryLabel;
                this.isFavorited = isFavorited;
            }
        }

        private const string CatalogResourcePath = "Generated/BuildCatalogDatabase";
        private const string SaveFileName = "mp_settlers_world.json";
        private const string ActionMapName = "Player";
        private const string AttackActionName = "Attack";
        private const string InteractActionName = "Interact";
        private const string PreviousActionName = "Previous";
        private const string NextActionName = "Next";
        private const float InteractDistance = 6.5f;
        private const float PlacementDistance = 48f;
        private const float PlacementSnapSize = 1f;
        private const float MinimumPlacementDistanceFromPlayer = 1.75f;
        private const int PreviewLayer = 30;
        private const int HotbarSlotCount = 10;
        private const int MaxHealth = 100;
        private const int FoodHealAmount = 25;

        private static SandboxGameManager instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<SandboxGameManager>() != null)
            {
                return;
            }

            GameObject runtimeObject = new("MP Settlers Sandbox");
            runtimeObject.AddComponent<SandboxGameManager>();
        }

        private readonly Dictionary<string, BuildCatalogItem> catalogLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<BuildCategory, List<BuildCatalogItem>> itemsByCategory = new();
        private readonly Dictionary<string, PlacedWorldObject> placedObjects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> storedFoodInventory = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> storedWeaponInventory = new(StringComparer.Ordinal);
        private readonly string[] favoriteHotbarItemIds = new string[HotbarSlotCount];

        private BuildCatalogDatabase catalogDatabase;
        private PlayerMovementController playerMovement;
        private PlayerInput playerInput;
        private Transform playerTransform;
        private Camera mainCamera;
        private ThirdPersonFollowCamera followCamera;

        private InputAction attackAction;
        private InputAction interactAction;
        private InputAction previousAction;
        private InputAction nextAction;

        private int wood;
        private int stone;
        private int food;
        private int health = MaxHealth;
        private bool expandedEnvironmentSeeded;

        private bool buildPanelOpen;
        private bool inGameMenuOpen;
        private bool deleteMode;
        private bool devBuildMode;
        private bool pointerMode;
        private bool pendingBuildToggle;
        private bool pendingMenuClose;
        private bool pendingInGameMenuToggle;
        private bool pendingInGameMenuClose;
        private string selectedInventoryItemId;
        private bool placementActive;
        private bool placementSnapEnabled = true;
        private bool placementHasValidTarget;
        private Vector3 pendingPlacementPosition;
        private Quaternion pendingPlacementRotation = Quaternion.identity;
        private float placementYaw;
        private BuildCategory selectedCategory = BuildCategory.Town;
        private int selectedIndex;
        private int selectedHotbarIndex;
        private Vector2 buildListScroll;
        private string activeGhostCatalogId;
        private GameObject ghostInstance;
        private Renderer[] ghostRenderers = Array.Empty<Renderer>();

        private RenewableNode targetedRenewable;
        private InventoryPickup targetedPickup;
        private PlacedWorldObject targetedPlacedObject;

        private bool wasGrounded;
        private float mostNegativeAirVelocity;
        private string statusMessage = string.Empty;
        private float statusMessageUntil;
        private InGameMenuTab selectedInGameMenuTab = InGameMenuTab.Overview;
        private bool showCrosshair = true;
        private bool showActionHints = true;
        private bool calmCameraEnabled = true;
        private Vector2 inventoryGridScroll;

        private GUIStyle windowStyle;
        private GUIStyle headingStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallMutedStyle;
        private GUIStyle buttonStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle selectedTabButtonStyle;
        private GUIStyle catalogItemButtonStyle;
        private GUIStyle selectedCatalogItemButtonStyle;
        private GUIStyle hotbarSlotStyle;
        private GUIStyle hotbarSelectedSlotStyle;
        private GUIStyle hotbarKeyStyle;
        private GUIStyle resourceBadgeStyle;
        private GUIStyle promptLabelStyle;
        private GUIStyle journalShellStyle;
        private GUIStyle journalSectionStyle;
        private GUIStyle journalTitleStyle;
        private GUIStyle journalSubtitleStyle;
        private GUIStyle journalTabStyle;
        private GUIStyle journalActiveTabStyle;
        private GUIStyle journalBodyStyle;
        private GUIStyle modeLabelStyle;
        private GUIStyle keyHintStyle;
        private GUIStyle statLabelStyle;
        private GUIStyle valueLabelStyle;
        private GUIStyle settingsRowLabelStyle;
        private GUIStyle toggleOnStyle;
        private GUIStyle toggleOffStyle;
        private GUIStyle questDoneStyle;
        private GUIStyle questPendingStyle;
        private GUIStyle hudCardStyle;
        private GUIStyle escHintStyle;
        private float currentUiScale = -1f;
        private bool IsAnyMenuOpen => buildPanelOpen || inGameMenuOpen;

        private Texture2D panelTexture;
        private Texture2D buttonTexture;
        private Texture2D buttonSelectedTexture;
        private Texture2D hotbarTexture;
        private Texture2D hotbarSelectedTexture;
        private Texture2D chipTexture;
        private Texture2D crosshairTexture;
        private Texture2D journalShellTexture;
        private Texture2D journalCardTexture;
        private Texture2D journalTabTexture;
        private Texture2D journalTabActiveTexture;
        private Texture2D journalAccentTexture;
        private Texture2D statBarBgTexture;
        private Texture2D statBarHpTexture;
        private Texture2D statBarStaminaTexture;
        private Texture2D statBarHungerTexture;
        private Texture2D statBarThirstTexture;
        private Texture2D amberAccentTexture;
        private Texture2D borderDarkTexture;
        private Texture2D hudCardTexture;
        private Texture2D modeBadgeTexture;
        private Texture2D toggleOnTexture;
        private Texture2D toggleOffTexture;
        private Texture2D questCheckTexture;
        private Texture2D skillBarBgTexture;
        private Texture2D skillBarFillTexture;
        private GameObject previewStageRoot;
        private GameObject previewInstance;
        private Camera previewCamera;
        private Light previewLight;
        private RenderTexture previewRenderTexture;
        private string previewCatalogId;
        private bool previewRenderQueued;

        private readonly struct UiLayout
        {
            public readonly float margin;
            public readonly float gutter;
            public readonly float hotbarWidth;
            public readonly float slotWidth;
            public readonly float slotHeight;
            public readonly float resourceBadgeWidth;
            public readonly float resourceBadgeHeight;
            public readonly float buildItemHeight;
            public readonly Rect playerPanelRect;
            public readonly Rect helpPanelRect;
            public readonly Rect modePanelRect;
            public readonly Rect hotbarRect;
            public readonly Rect buildPanelRect;
            public readonly Rect contextRect;
            public readonly Rect statusRect;

            public UiLayout(int screenWidth, int screenHeight)
            {
                float width = Mathf.Max(640f, screenWidth);
                float height = Mathf.Max(360f, screenHeight);

                margin = Mathf.Clamp(Mathf.Min(width, height) * 0.015f, 10f, 20f);
                gutter = Mathf.Clamp(margin * 0.65f, 6f, 12f);

                float playerWidth = Mathf.Clamp(width * 0.18f, 190f, 250f);
                float playerHeight = Mathf.Clamp(height * 0.12f, 88f, 112f);
                playerPanelRect = new Rect(margin, margin, playerWidth, playerHeight);

                float helpWidth = Mathf.Clamp(width * 0.2f, 210f, 280f);
                float helpHeight = Mathf.Clamp(height * 0.09f, 72f, 92f);
                helpPanelRect = new Rect(margin, playerPanelRect.yMax + gutter, helpWidth, helpHeight);

                float modeWidth = Mathf.Clamp(width * 0.22f, 220f, 290f);
                float modeHeight = Mathf.Clamp(height * 0.11f, 84f, 104f);

                float hotbarSpacing = Mathf.Clamp(width * 0.004f, 4f, 8f);
                hotbarWidth = Mathf.Min(width - (margin * 2f), 960f);
                slotWidth = Mathf.Clamp((hotbarWidth - (hotbarSpacing * (HotbarSlotCount - 1))) / HotbarSlotCount, 46f, 76f);
                slotHeight = Mathf.Clamp(height * 0.065f, 58f, 76f);
                resourceBadgeHeight = Mathf.Clamp(height * 0.03f, 24f, 30f);
                resourceBadgeWidth = Mathf.Max(74f, (hotbarWidth - (hotbarSpacing * 4f)) / 5f);
                float hotbarHeight = resourceBadgeHeight + gutter + slotHeight + 12f;
                hotbarRect = new Rect((width - hotbarWidth) * 0.5f, height - hotbarHeight - margin, hotbarWidth, hotbarHeight);

                float contextWidth = Mathf.Min(width - (margin * 2f), hotbarWidth);
                float contextHeight = Mathf.Clamp(height * 0.08f, 52f, 84f);
                contextRect = new Rect((width - contextWidth) * 0.5f, hotbarRect.y - contextHeight - gutter, contextWidth, contextHeight);

                float statusWidth = Mathf.Min(width - (margin * 2f), hotbarWidth * 0.8f);
                float statusHeight = Mathf.Clamp(height * 0.045f, 34f, 48f);
                statusRect = new Rect((width - statusWidth) * 0.5f, contextRect.y - statusHeight - gutter, statusWidth, statusHeight);

                float reservedBottom = (height - contextRect.y) + margin;
                float buildWidth = Mathf.Clamp(width * 0.25f, 310f, 430f);
                buildWidth = Mathf.Min(buildWidth, width - (margin * 2f));
                float availableHeight = height - reservedBottom - margin;
                float buildHeight = Mathf.Clamp(availableHeight, 260f, 680f);
                buildPanelRect = new Rect(width - buildWidth - margin, margin, buildWidth, buildHeight);

                float centeredModeX = (width - modeWidth) * 0.5f;
                float maxModeX = buildPanelRect.x - gutter - modeWidth;
                float minModeX = playerPanelRect.xMax + gutter;
                float resolvedModeX = Mathf.Clamp(centeredModeX, minModeX, Mathf.Max(minModeX, maxModeX));
                modePanelRect = new Rect(resolvedModeX, margin, modeWidth, modeHeight);

                buildItemHeight = Mathf.Clamp(height * 0.055f, 44f, 56f);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            LoadCatalog();
            BuildCatalogIndexes();
            FindSceneReferences(force: true);
            CacheActions();
            wasGrounded = playerMovement != null && playerMovement.IsGrounded;
            LoadOrInitializeWorld();
            EnsureCameraTarget();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            CleanupGhost();
            CleanupGuiTextures();
            CleanupPreviewResources();
        }

        private void OnApplicationQuit()
        {
            SaveWorld();
        }

        private void Update()
        {
            FindSceneReferences(force: false);
            EnsureCameraTarget();
            UpdatePlayerInputSuppression();
            UpdateCursorMode();
            UpdateFallDamage();
            UpdateTargetedObject();
            HandleGlobalShortcuts();

            if (inGameMenuOpen)
            {
                HandleInGameMenuNavigation();
                return;
            }

            if (buildPanelOpen)
            {
                HandleBuildMenuNavigation();
                return;
            }

            HandleHotbarInput();

            if (placementActive)
            {
                UpdatePlacementGhost();
                HandlePlacementInput();
                return;
            }

            if (deleteMode)
            {
                HandleDeleteInput();
                return;
            }

            if (TryGetInteractTriggered())
            {
                HandleInteract();
            }
        }

        private void OnGUI()
        {
            CaptureGuiShortcutFallbacks();
            EnsureGuiStyles();
            DrawCrosshair();
            if (inGameMenuOpen)
            {
                DrawInGameMenu();
            }
            else
            {
                DrawHud();
                DrawHotbarHud();
                DrawBuildPanel();
                DrawContextPrompt();
            }

            DrawStatusMessage();
        }

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

            EnsureSelectionInRange();
        }

        private void FindSceneReferences(bool force)
        {
            if (!force && playerMovement != null && playerInput != null && mainCamera != null)
            {
                return;
            }

            playerMovement = FindAnyObjectByType<PlayerMovementController>();
            playerTransform = playerMovement != null ? playerMovement.transform : null;
            playerInput = playerTransform != null ? playerTransform.GetComponent<PlayerInput>() : FindAnyObjectByType<PlayerInput>();
            mainCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            followCamera = mainCamera != null ? mainCamera.GetComponent<ThirdPersonFollowCamera>() : null;

            if (playerMovement != null && playerTransform != null)
            {
                wasGrounded = playerMovement.IsGrounded;
            }

            CacheActions();
        }

        private void EnsureCameraTarget()
        {
            if (followCamera != null && playerTransform != null && followCamera.Target == null)
            {
                followCamera.Target = playerTransform;
            }

            ApplyCameraFeel();
            ExcludePreviewLayerFromMainCamera();
        }

        private void CacheActions()
        {
            if (playerInput == null || playerInput.actions == null)
            {
                attackAction = null;
                interactAction = null;
                previousAction = null;
                nextAction = null;
                return;
            }

            InputActionMap actionMap = playerInput.actions.FindActionMap(ActionMapName, false);
            if (actionMap == null)
            {
                attackAction = null;
                interactAction = null;
                previousAction = null;
                nextAction = null;
                return;
            }

            if (playerInput.currentActionMap != actionMap)
            {
                playerInput.SwitchCurrentActionMap(ActionMapName);
            }

            attackAction = actionMap.FindAction(AttackActionName, false);
            interactAction = actionMap.FindAction(InteractActionName, false);
            previousAction = actionMap.FindAction(PreviousActionName, false);
            nextAction = actionMap.FindAction(NextActionName, false);
        }

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

                    SpawnCatalogItem(item, placedObject.uniqueId, placedObject.position, placedObject.rotation, growth, registerForSave: true, placedByPlayer: placedObject.placedByPlayer);
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

        private void SeedStarterNodes()
        {
            Vector3 origin = playerTransform != null ? playerTransform.position : Vector3.zero;
            TrySeed("Tree_04", origin + new Vector3(6f, 0f, 8f), 20f);
            TrySeed("Tree_04", origin + new Vector3(-8f, 0f, 4f), -30f);
            TrySeed("Rock_04", origin + new Vector3(8f, 0f, -6f), 0f);
            TrySeed("Rock_04", origin + new Vector3(-6f, 0f, -8f), 30f);
            TrySeed("TomatoPlant_01", origin + new Vector3(4f, 0f, 11f), 0f);
            TrySeed("Cabbage_01", origin + new Vector3(-4f, 0f, 11f), 0f);
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

        private void ClearRuntimeObjects()
        {
            placedObjects.Clear();
            foreach (PlacedWorldObject placedWorldObject in FindObjectsByType<PlacedWorldObject>(FindObjectsInactive.Include))
            {
                if (placedWorldObject != null)
                {
                    Destroy(placedWorldObject.gameObject);
                }
            }
        }

        private void UpdateCursorMode()
        {
            if (followCamera != null)
            {
                followCamera.SetUiCursorMode(pointerMode || inGameMenuOpen);
            }
        }

        private void UpdatePlayerInputSuppression()
        {
            if (playerMovement != null)
            {
                playerMovement.InputSuppressed = IsAnyMenuOpen;
            }
        }

        private void UpdateFallDamage()
        {
            if (playerMovement == null)
            {
                return;
            }

            bool isGrounded = playerMovement.IsGrounded;
            if (!isGrounded)
            {
                mostNegativeAirVelocity = Mathf.Min(mostNegativeAirVelocity, playerMovement.VerticalVelocity);
            }
            else if (!wasGrounded)
            {
                float landingVelocity = mostNegativeAirVelocity;
                if (landingVelocity < -12f)
                {
                    int damage = Mathf.Clamp(Mathf.RoundToInt((Mathf.Abs(landingVelocity) - 12f) * 5f), 0, 50);
                    if (damage > 0)
                    {
                        ApplyDamage(damage);
                    }
                }

                mostNegativeAirVelocity = 0f;
            }

            if (isGrounded && wasGrounded)
            {
                mostNegativeAirVelocity = 0f;
            }

            wasGrounded = isGrounded;
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

        private void HandleGlobalShortcuts()
        {
            bool inGameMenuTogglePressed = ConsumePendingShortcut(ref pendingInGameMenuToggle) ||
                                           (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);

            if (inGameMenuTogglePressed)
            {
                if (inGameMenuOpen)
                {
                    CloseInGameMenu();
                }
                else
                {
                    OpenInGameMenu();
                }
            }

            if (inGameMenuOpen && (ConsumePendingShortcut(ref pendingInGameMenuClose) ||
                                   (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)))
            {
                CloseInGameMenu();
                return;
            }

            bool buildTogglePressed = ConsumePendingShortcut(ref pendingBuildToggle) ||
                                      (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);

            if (!inGameMenuOpen && buildTogglePressed)
            {
                if (buildPanelOpen)
                {
                    CloseBuildPanel();
                }
                else if (placementActive)
                {
                    CancelPlacement();
                    OpenBuildPanel();
                }
                else
                {
                    OpenBuildPanel();
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (buildPanelOpen && Keyboard.current.lKey.wasPressedThisFrame)
            {
                devBuildMode = !devBuildMode;
                SetStatusMessage(devBuildMode ? "DEV Build Mode enabled." : "DEV Build Mode disabled.");
            }

            if (buildPanelOpen && Keyboard.current.fKey.wasPressedThisFrame)
            {
                FavoriteSelectedBuildItem();
            }

            if (Keyboard.current.xKey.wasPressedThisFrame)
            {
                deleteMode = !deleteMode;
                if (deleteMode)
                {
                    CloseBuildPanel();
                    placementActive = false;
                    CleanupGhost();
                }
            }

            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                ConsumeStoredFood();
            }

            if (buildPanelOpen && (TryGetPreviousPressedThisFrame() || TryGetNextPressedThisFrame()))
            {
                CycleSelection(TryGetNextPressedThisFrame() ? 1 : -1);
            }
        }

        private void HandleBuildMenuNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            int verticalDelta = 0;
            int horizontalDelta = 0;

            if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame)
            {
                verticalDelta = -1;
            }
            else if (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame)
            {
                verticalDelta = 1;
            }

            if (Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                horizontalDelta = -1;
            }
            else if (Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                horizontalDelta = 1;
            }

            if (horizontalDelta != 0)
            {
                ShiftCategory(horizontalDelta);
            }

            if (verticalDelta != 0)
            {
                ShiftBuildItem(verticalDelta);
            }

            if (TryGetMouseWheelDelta(out int wheelDelta))
            {
                ShiftBuildItem(wheelDelta);
            }

            bool closePressed = ConsumePendingShortcut(ref pendingMenuClose) ||
                                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

            if (closePressed)
            {
                CloseBuildPanel();
                return;
            }

            bool confirmSelection =
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                TryGetAttackPressedThisFrame();

            if (confirmSelection && GetSelectedItem() != null)
            {
                SelectItem(selectedCategory, selectedIndex);
            }
        }

        private void CaptureGuiShortcutFallbacks()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.B)
            {
                pendingBuildToggle = true;
                currentEvent.Use();
                return;
            }

            if (currentEvent.keyCode == KeyCode.Tab)
            {
                pendingInGameMenuToggle = true;
                currentEvent.Use();
                return;
            }

            if (buildPanelOpen && currentEvent.keyCode == KeyCode.Escape)
            {
                pendingMenuClose = true;
                currentEvent.Use();
                return;
            }

            if (inGameMenuOpen && currentEvent.keyCode == KeyCode.Escape)
            {
                pendingInGameMenuClose = true;
                currentEvent.Use();
            }
        }

        private bool ConsumePendingShortcut(ref bool pendingShortcut)
        {
            if (!pendingShortcut)
            {
                return false;
            }

            pendingShortcut = false;
            return true;
        }

        private void HandleHotbarInput()
        {
            int previousSlotIndex = selectedHotbarIndex;

            int directSelection = GetDirectHotbarSlotSelection();
            if (directSelection >= 0)
            {
                selectedHotbarIndex = directSelection;
            }

            if (directSelection < 0 && TryGetMouseWheelDelta(out int wheelDelta))
            {
                CycleHotbarSelection(wheelDelta);
            }
            else if (directSelection < 0 && TryGetPreviousPressedThisFrame())
            {
                CycleHotbarSelection(-1);
            }
            else if (directSelection < 0 && TryGetNextPressedThisFrame())
            {
                CycleHotbarSelection(1);
            }

            if (selectedHotbarIndex != previousSlotIndex && placementActive)
            {
                TrySwapPlacementToSelectedHotbarItem();
            }

            if (placementActive || deleteMode || buildPanelOpen || pointerMode)
            {
                return;
            }

            if (TryGetSubmitPressedThisFrame() || TryGetAttackPressedThisFrame())
            {
                TryBeginPlacementFromSelectedHotbar();
            }
        }

        private void FavoriteSelectedBuildItem()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatusMessage("No build item selected to favorite.");
                return;
            }

            int existingSlot = Array.IndexOf(favoriteHotbarItemIds, item.id);
            if (existingSlot >= 0)
            {
                selectedHotbarIndex = existingSlot;
                SetStatusMessage($"{item.displayName} is already in slot {existingSlot + 1}.");
                return;
            }

            int emptySlot = Array.FindIndex(favoriteHotbarItemIds, string.IsNullOrWhiteSpace);
            if (emptySlot >= 0)
            {
                favoriteHotbarItemIds[emptySlot] = item.id;
                selectedHotbarIndex = emptySlot;
                SaveWorld();
                SetStatusMessage($"Favorited {item.displayName} to slot {emptySlot + 1}.");
                return;
            }

            favoriteHotbarItemIds[selectedHotbarIndex] = item.id;
            SaveWorld();
            SetStatusMessage($"Replaced slot {selectedHotbarIndex + 1} with {item.displayName}.");
        }

        private void CycleHotbarSelection(int delta)
        {
            selectedHotbarIndex = (selectedHotbarIndex + delta) % HotbarSlotCount;
            if (selectedHotbarIndex < 0)
            {
                selectedHotbarIndex += HotbarSlotCount;
            }
        }

        private int GetDirectHotbarSlotSelection()
        {
            if (Keyboard.current == null)
            {
                return -1;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                return 0;
            }

            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                return 1;
            }

            if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                return 2;
            }

            if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                return 3;
            }

            if (Keyboard.current.digit5Key.wasPressedThisFrame)
            {
                return 4;
            }

            if (Keyboard.current.digit6Key.wasPressedThisFrame)
            {
                return 5;
            }

            if (Keyboard.current.digit7Key.wasPressedThisFrame)
            {
                return 6;
            }

            if (Keyboard.current.digit8Key.wasPressedThisFrame)
            {
                return 7;
            }

            if (Keyboard.current.digit9Key.wasPressedThisFrame)
            {
                return 8;
            }

            if (Keyboard.current.digit0Key.wasPressedThisFrame)
            {
                return 9;
            }

            return -1;
        }

        private bool TryGetMouseWheelDelta(out int delta)
        {
            delta = 0;
            if (Mouse.current == null)
            {
                return false;
            }

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f)
            {
                return false;
            }

            delta = scrollY > 0f ? -1 : 1;
            return true;
        }

        private void TryBeginPlacementFromSelectedHotbar()
        {
            BuildCatalogItem item = GetSelectedHotbarItem();
            if (item == null)
            {
                return;
            }

            SelectCatalogItemById(item.id);
        }

        private void TrySwapPlacementToSelectedHotbarItem()
        {
            BuildCatalogItem item = GetSelectedHotbarItem();
            if (item == null)
            {
                return;
            }

            SelectCatalogItemById(item.id);
        }

        private void HandlePlacementInput()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    placementYaw += 90f;
                }

                if (Keyboard.current.gKey.wasPressedThisFrame)
                {
                    placementSnapEnabled = !placementSnapEnabled;
                }

                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CancelPlacement();
                    return;
                }
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (buildPanelOpen && (TryGetPreviousPressedThisFrame() || TryGetNextPressedThisFrame()))
            {
                CycleSelection(TryGetNextPressedThisFrame() ? 1 : -1);
            }

            if (!pointerMode && TryGetAttackPressedThisFrame())
            {
                TryPlaceCurrentSelection();
            }

            if (!pointerMode && TryGetSubmitPressedThisFrame())
            {
                TryPlaceCurrentSelection();
            }
        }

        private void HandleDeleteInput()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                deleteMode = false;
                pointerMode = buildPanelOpen;
                return;
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                deleteMode = false;
                pointerMode = buildPanelOpen;
                return;
            }

            if (!pointerMode && TryGetAttackPressedThisFrame())
            {
                TryDeleteTarget();
            }
        }

        private void HandleInteract()
        {
            if (targetedPickup != null)
            {
                CollectPickup(targetedPickup);
                return;
            }

            if (targetedRenewable != null && targetedRenewable.TryHarvest(out int amount))
            {
                AddResource(targetedRenewable.ResourceType, amount);
                SetStatusMessage($"+{amount} {targetedRenewable.ResourceType.ToString().ToLowerInvariant()}");
                SaveWorld();
            }
        }

        private void CollectPickup(InventoryPickup pickup)
        {
            if (pickup == null)
            {
                return;
            }

            switch (pickup.InventoryType)
            {
                case PickupInventoryType.Food:
                    AddInventoryCount(storedFoodInventory, pickup.ItemId, 1);
                    SetStatusMessage($"Stored {pickup.DisplayName}.");
                    break;

                case PickupInventoryType.Weapon:
                    AddInventoryCount(storedWeaponInventory, pickup.ItemId, 1);
                    SetStatusMessage($"Collected {pickup.DisplayName}.");
                    break;
            }

            PlacedWorldObject placedWorldObject = pickup.GetComponent<PlacedWorldObject>();
            if (placedWorldObject != null)
            {
                placedObjects.Remove(placedWorldObject.UniqueId);
                Destroy(placedWorldObject.gameObject);
            }

            SaveWorld();
        }

        private void ConsumeStoredFood()
        {
            List<KeyValuePair<string, int>> availableItems = storedFoodInventory
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .ToList();

            if (availableItems.Count == 0)
            {
                SetStatusMessage("No stored food to consume.");
                return;
            }

            if (health >= MaxHealth)
            {
                SetStatusMessage("Health is already full.");
                return;
            }

            string itemId = availableItems[0].Key;
            RemoveInventoryCount(storedFoodInventory, itemId, 1);
            int previousHealth = health;
            health = Mathf.Clamp(health + FoodHealAmount, 0, MaxHealth);
            SetStatusMessage($"Consumed {GetDisplayNameForItemId(itemId)} (+{health - previousHealth} health).");
            SaveWorld();
        }

        private void TryDeleteTarget()
        {
            if (targetedPlacedObject == null)
            {
                return;
            }

            if (!catalogLookup.TryGetValue(targetedPlacedObject.CatalogItemId, out BuildCatalogItem item))
            {
                return;
            }

            RefundCost(item.cost);
            placedObjects.Remove(targetedPlacedObject.UniqueId);
            Destroy(targetedPlacedObject.gameObject);
            SaveWorld();
            SetStatusMessage($"Deleted {item.displayName} and refunded {item.cost.ToDisplayString()}.");
        }

        private void UpdatePlacementGhost()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                placementHasValidTarget = false;
                CleanupGhost();
                return;
            }

            EnsureGhost(item);

            if (!TryGetCameraRaycast(PlacementDistance, out RaycastHit hit))
            {
                placementHasValidTarget = false;
                if (ghostInstance != null)
                {
                    ghostInstance.SetActive(false);
                }

                return;
            }

            if (ghostInstance != null)
            {
                ghostInstance.SetActive(true);
            }

            Quaternion rotation = Quaternion.Euler(0f, placementYaw, 0f);
            Vector3 rawPosition = hit.point;
            if (placementSnapEnabled)
            {
                rawPosition.x = Mathf.Round(rawPosition.x / PlacementSnapSize) * PlacementSnapSize;
                rawPosition.z = Mathf.Round(rawPosition.z / PlacementSnapSize) * PlacementSnapSize;
            }

            if (ghostInstance != null)
            {
                ghostInstance.transform.rotation = rotation;
                ghostInstance.transform.position = rawPosition;

                if (TryGetRenderableBounds(ghostInstance, out Bounds ghostBounds))
                {
                    float bottomOffset = ghostInstance.transform.position.y - ghostBounds.min.y;
                    rawPosition.y = hit.point.y + bottomOffset;
                    ghostInstance.transform.position = rawPosition;
                }
            }

            float playerDistance = playerTransform != null
                ? Vector2.Distance(new Vector2(rawPosition.x, rawPosition.z), new Vector2(playerTransform.position.x, playerTransform.position.z))
                : MinimumPlacementDistanceFromPlayer + 1f;

            placementHasValidTarget = playerDistance >= MinimumPlacementDistanceFromPlayer;
            pendingPlacementPosition = rawPosition;
            pendingPlacementRotation = rotation;

            UpdateGhostTint(placementHasValidTarget);
        }

        private void TryPlaceCurrentSelection()
        {
            BuildCatalogItem item = GetSelectedItem();
            if (item == null)
            {
                return;
            }

            if (!placementHasValidTarget)
            {
                SetStatusMessage("Move the ghost farther from the player.");
                return;
            }

            if (!devBuildMode && !CanAfford(item.cost))
            {
                SetStatusMessage($"Not enough resources for {item.displayName}. Press L in the build menu for DEV Build.");
                return;
            }

            GameObject placedObject = SpawnCatalogItem(
                item,
                Guid.NewGuid().ToString("N"),
                pendingPlacementPosition,
                pendingPlacementRotation,
                1f,
                registerForSave: true,
                placedByPlayer: true);

            if (placedObject == null)
            {
                return;
            }

            if (!devBuildMode)
            {
                SpendCost(item.cost);
            }

            SaveWorld();
            SetStatusMessage($"Placed {item.displayName}.");
            placementActive = false;
            CleanupGhost();
        }

        private void CancelPlacement()
        {
            placementActive = false;
            CleanupGhost();
            pointerMode = false;
        }

        private void EnsureGhost(BuildCatalogItem item)
        {
            if (item == null)
            {
                CleanupGhost();
                return;
            }

            if (ghostInstance != null && activeGhostCatalogId == item.id)
            {
                return;
            }

            CleanupGhost();

            ghostInstance = Instantiate(item.prefab);
            ghostInstance.name = $"Ghost_{item.prefab.name}";
            activeGhostCatalogId = item.id;
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                SetLayerRecursively(ghostInstance.transform, ignoreRaycastLayer);
            }

            foreach (MonoBehaviour behaviour in ghostInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Collider colliderComponent in ghostInstance.GetComponentsInChildren<Collider>(true))
            {
                colliderComponent.enabled = false;
            }

            ghostRenderers = ghostInstance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer rendererComponent in ghostRenderers)
            {
                rendererComponent.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rendererComponent.receiveShadows = false;
                rendererComponent.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                Material[] materials = rendererComponent.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    materials[i] = new Material(materials[i]);
                }

                rendererComponent.materials = materials;
            }

            UpdateGhostTint(true);
        }

        private void CleanupGhost()
        {
            if (ghostInstance != null)
            {
                Destroy(ghostInstance);
            }

            ghostInstance = null;
            ghostRenderers = Array.Empty<Renderer>();
            activeGhostCatalogId = string.Empty;
            placementHasValidTarget = false;
        }

        private void CycleSelection(int delta)
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            selectedIndex = (selectedIndex + delta) % items.Count;
            if (selectedIndex < 0)
            {
                selectedIndex += items.Count;
            }

            if (placementActive)
            {
                EnsureGhost(GetSelectedItem());
            }
        }

        private void HandleInGameMenuNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame)
            {
                ShiftInGameMenuTab(-1);
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame)
            {
                ShiftInGameMenuTab(1);
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Overview;
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Skills;
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Inventory;
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                selectedInGameMenuTab = InGameMenuTab.Settings;
            }
        }

        private void ShiftInGameMenuTab(int delta)
        {
            InGameMenuTab[] tabs =
            {
                InGameMenuTab.Overview,
                InGameMenuTab.Skills,
                InGameMenuTab.Inventory,
                InGameMenuTab.Settings
            };

            int currentIndex = Array.IndexOf(tabs, selectedInGameMenuTab);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            currentIndex = (currentIndex + delta) % tabs.Length;
            if (currentIndex < 0)
            {
                currentIndex += tabs.Length;
            }

            selectedInGameMenuTab = tabs[currentIndex];
        }

        private void OpenInGameMenu()
        {
            buildPanelOpen = false;
            deleteMode = false;
            placementActive = false;
            pointerMode = true;
            selectedInGameMenuTab = InGameMenuTab.Overview;
            CleanupGhost();
            inGameMenuOpen = true;
        }

        private void CloseInGameMenu()
        {
            inGameMenuOpen = false;
            pointerMode = false;
        }

        private void OpenBuildPanel()
        {
            CloseInGameMenu();
            buildPanelOpen = true;
            deleteMode = false;
            pointerMode = false;
            placementActive = false;
            CleanupGhost();
        }

        private void CloseBuildPanel()
        {
            buildPanelOpen = false;
            pointerMode = false;
        }

        private void SelectCategory(BuildCategory category)
        {
            if (selectedCategory == category)
            {
                return;
            }

            selectedCategory = category;
            selectedIndex = 0;
            EnsureSelectionInRange();
            if (placementActive)
            {
                EnsureGhost(GetSelectedItem());
            }
        }

        private void ShiftCategory(int delta)
        {
            BuildCategory[] categories =
            {
                BuildCategory.Town,
                BuildCategory.Farm,
                BuildCategory.Food,
                BuildCategory.Weapons
            };

            int currentIndex = Array.IndexOf(categories, selectedCategory);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            currentIndex = (currentIndex + delta) % categories.Length;
            if (currentIndex < 0)
            {
                currentIndex += categories.Length;
            }

            SelectCategory(categories[currentIndex]);
            CenterBuildScrollOnSelection();
        }

        private void ShiftBuildItem(int delta)
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                buildListScroll.y = 0f;
                return;
            }

            selectedIndex = (selectedIndex + delta) % items.Count;
            if (selectedIndex < 0)
            {
                selectedIndex += items.Count;
            }

            CenterBuildScrollOnSelection();
        }

        private void SelectItem(BuildCategory category, int index)
        {
            List<BuildCatalogItem> items = itemsByCategory.TryGetValue(category, out List<BuildCatalogItem> categoryItems)
                ? categoryItems
                : null;

            if (items == null || items.Count == 0 || index < 0 || index >= items.Count)
            {
                return;
            }

            selectedCategory = category;
            selectedIndex = index;
            EnsureSelectionInRange();
            placementActive = true;
            deleteMode = false;
            buildPanelOpen = false;
            pointerMode = false;
            placementYaw = 0f;
            EnsureGhost(GetSelectedItem());
        }

        private void SelectCatalogItemById(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !catalogLookup.TryGetValue(itemId, out BuildCatalogItem item))
            {
                return;
            }

            if (!itemsByCategory.TryGetValue(item.category, out List<BuildCatalogItem> categoryItems))
            {
                return;
            }

            int index = categoryItems.FindIndex(candidate => candidate.id == itemId);
            if (index < 0)
            {
                return;
            }

            SelectItem(item.category, index);
        }

        private void CenterBuildScrollOnSelection()
        {
            buildListScroll.y = Mathf.Max(0f, (selectedIndex * 56f) - 56f);
        }

        private void EnsureSelectionInRange()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
        }

        private BuildCatalogItem GetSelectedItem()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count)
            {
                return null;
            }

            return items[selectedIndex];
        }

        private BuildCatalogItem GetSelectedHotbarItem()
        {
            string itemId = favoriteHotbarItemIds[selectedHotbarIndex];
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return catalogLookup.TryGetValue(itemId, out BuildCatalogItem item) ? item : null;
        }

        private List<BuildCatalogItem> GetItemsForSelectedCategory()
        {
            return itemsByCategory.TryGetValue(selectedCategory, out List<BuildCatalogItem> items)
                ? items
                : new List<BuildCatalogItem>();
        }

        private void DrawHud()
        {
            UiLayout layout = GetUiLayout();
            BuildCatalogItem selectedHotbarItem = GetSelectedHotbarItem();
            float barHeight = Mathf.Clamp(14f * currentUiScale, 11f, 17f);

            DrawBorderedPanel(layout.playerPanelRect, () =>
            {
                GUILayout.Label("Settler", headingStyle);
                GUILayout.Space(4f);
                DrawStatBarRow("HP", health / (float)MaxHealth, statBarHpTexture, $"{health}/{MaxHealth}", barHeight);
                DrawStatBarRow("Food", Mathf.Clamp01(storedFoodInventory.Values.Sum() / 10f), statBarHungerTexture,
                    FormatInventorySummary(storedFoodInventory, 2), barHeight);
                DrawStatBarRow("Arms", Mathf.Clamp01(storedWeaponInventory.Values.Sum() / 6f), statBarStaminaTexture,
                    FormatInventorySummary(storedWeaponInventory, 2), barHeight);
            });

            if (showActionHints)
            {
                DrawPanel(layout.helpPanelRect, () =>
                {
                    GUILayout.Label("Actions", headingStyle);
                    foreach (string line in GetActionPanelLines())
                    {
                        GUILayout.Label(line, line.Contains('|') ? smallMutedStyle : labelStyle);
                    }
                });
            }

            DrawBorderedPanel(layout.modePanelRect, () =>
            {
                string modeText = deleteMode ? "DELETE" : devBuildMode ? "DEV BUILD" : "RESOURCE";
                Color modeColor = deleteMode
                    ? new Color(0.84f, 0.32f, 0.28f)
                    : devBuildMode
                        ? new Color(0.62f, 0.82f, 0.48f)
                        : new Color(0.82f, 0.64f, 0.24f);

                Rect modeRect = GUILayoutUtility.GetRect(10f, Mathf.RoundToInt(20f * currentUiScale), GUILayout.ExpandWidth(true));
                Color previousColor = GUI.color;
                GUI.color = new Color(modeColor.r, modeColor.g, modeColor.b, 0.22f);
                GUI.DrawTexture(modeRect, modeBadgeTexture);
                GUI.color = previousColor;
                modeLabelStyle.normal.textColor = modeColor;
                GUI.Label(modeRect, modeText, modeLabelStyle);

                GUILayout.Space(4f);
                GUILayout.Label(selectedHotbarItem != null
                    ? selectedHotbarItem.displayName
                    : $"Slot {GetHotbarKeyLabel(selectedHotbarIndex)} Empty", labelStyle);
                GUILayout.Label(deleteMode ? "Aim + LMB to remove" : "B build | X delete", smallMutedStyle);
            });
        }

        private void DrawPanel(Rect rect, Action drawer)
        {
            GUILayout.BeginArea(rect, windowStyle);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private UiLayout GetUiLayout()
        {
            return new UiLayout(Screen.width, Screen.height);
        }

        private Rect GetTextPanelRect(Rect anchorRect, string text, GUIStyle style)
        {
            float contentWidth = Mathf.Max(64f, anchorRect.width - (windowStyle.padding.left + windowStyle.padding.right));
            float minHeight = anchorRect.height;
            float textHeight = style.CalcHeight(new GUIContent(text), contentWidth);
            float totalHeight = Mathf.Max(minHeight, textHeight + windowStyle.padding.top + windowStyle.padding.bottom);
            float y = anchorRect.yMax - totalHeight;
            return new Rect(anchorRect.x, y, anchorRect.width, totalHeight);
        }

        private void DrawCrosshair()
        {
            if (!showCrosshair || pointerMode || inGameMenuOpen || crosshairTexture == null)
            {
                return;
            }

            float size = Mathf.Clamp(14f * currentUiScale, 11f, 18f);
            float thickness = Mathf.Max(2f, Mathf.Round(2f * currentUiScale));
            float gap = Mathf.Clamp(4f * currentUiScale, 3f, 6f);
            Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);

            Color previousColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.42f);
            DrawCrosshairLine(center.x - (size * 0.5f) + 1f, center.y - (thickness * 0.5f) + 1f, size, thickness);
            DrawCrosshairLine(center.x - (thickness * 0.5f) + 1f, center.y - (size * 0.5f) + 1f, thickness, size);

            GUI.color = new Color(0.98f, 0.97f, 0.85f, 0.95f);
            DrawCrosshairLine(center.x - (size * 0.5f), center.y - (thickness * 0.5f), (size - gap) * 0.5f, thickness);
            DrawCrosshairLine(center.x + (gap * 0.5f), center.y - (thickness * 0.5f), (size - gap) * 0.5f, thickness);
            DrawCrosshairLine(center.x - (thickness * 0.5f), center.y - (size * 0.5f), thickness, (size - gap) * 0.5f);
            DrawCrosshairLine(center.x - (thickness * 0.5f), center.y + (gap * 0.5f), thickness, (size - gap) * 0.5f);

            GUI.color = previousColor;
        }

        private void DrawCrosshairLine(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), crosshairTexture);
        }

        private void DrawHotbarHud()
        {
            UiLayout layout = GetUiLayout();
            float spacing = Mathf.Clamp(layout.gutter * 0.5f, 4f, 8f);
            float barHeight = Mathf.Clamp(10f * currentUiScale, 8f, 13f);
            float barGap = 3f;

            GUILayout.BeginArea(layout.hotbarRect);

            float totalBarWidth = layout.hotbarRect.width;
            float singleBarWidth = (totalBarWidth - (barGap * 3f)) * 0.25f;

            GUILayout.BeginHorizontal();
            Rect hpBar = GUILayoutUtility.GetRect(singleBarWidth, barHeight, GUILayout.Width(singleBarWidth));
            DrawStatBar(hpBar, health / (float)MaxHealth, statBarHpTexture, "HP", $"{health}");
            GUILayout.Space(barGap);
            Rect woodBar = GUILayoutUtility.GetRect(singleBarWidth, barHeight, GUILayout.Width(singleBarWidth));
            DrawStatBar(woodBar, Mathf.Clamp01(wood / 50f), statBarHungerTexture, "Wood", $"{wood}");
            GUILayout.Space(barGap);
            Rect stoneBar = GUILayoutUtility.GetRect(singleBarWidth, barHeight, GUILayout.Width(singleBarWidth));
            DrawStatBar(stoneBar, Mathf.Clamp01(stone / 30f), statBarThirstTexture, "Stone", $"{stone}");
            GUILayout.Space(barGap);
            Rect foodBar = GUILayoutUtility.GetRect(singleBarWidth, barHeight, GUILayout.Width(singleBarWidth));
            DrawStatBar(foodBar, Mathf.Clamp01(food / 20f), statBarStaminaTexture, "Food", $"{food}");
            GUILayout.EndHorizontal();

            GUILayout.Space(layout.gutter);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                bool isSelected = i == selectedHotbarIndex;
                GUIStyle slotStyle = isSelected ? hotbarSelectedSlotStyle : hotbarSlotStyle;
                float slotW = isSelected ? layout.slotWidth + 4f : layout.slotWidth;
                float slotH = isSelected ? layout.slotHeight + 4f : layout.slotHeight;

                GUILayout.BeginVertical(slotStyle, GUILayout.Width(slotW), GUILayout.Height(slotH));
                GUILayout.Label(GetHotbarKeyLabel(i), hotbarKeyStyle);

                string itemId = favoriteHotbarItemIds[i];
                if (catalogLookup.TryGetValue(itemId ?? string.Empty, out BuildCatalogItem item))
                {
                    GUILayout.Label(GetHotbarSlotLabel(item.displayName), isSelected ? headingStyle : labelStyle);
                    GUILayout.Label(devBuildMode ? "Free" : item.cost.ToDisplayString(), smallMutedStyle);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("---", smallMutedStyle);
                }

                GUILayout.EndVertical();

                if (i < HotbarSlotCount - 1)
                {
                    GUILayout.Space(spacing);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawBuildPanel()
        {
            if (!buildPanelOpen)
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect panelRect = layout.buildPanelRect;
            BuildCatalogItem selectedItem = GetSelectedItem();
            float previewHeight = Mathf.Clamp(panelRect.width * 0.46f, 136f, 220f);
            GUILayout.BeginArea(panelRect, windowStyle);
            GUILayout.Label("Build Catalog", headingStyle);

            GUILayout.BeginHorizontal();
            DrawCategoryButton(BuildCategory.Town);
            DrawCategoryButton(BuildCategory.Farm);
            DrawCategoryButton(BuildCategory.Food);
            DrawCategoryButton(BuildCategory.Weapons);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            float listHeight = Mathf.Max(124f, panelRect.height - previewHeight - Mathf.Clamp(245f * currentUiScale, 210f, 320f));
            buildListScroll = GUILayout.BeginScrollView(buildListScroll, GUILayout.Height(listHeight));
            for (int i = 0; i < items.Count; i++)
            {
                BuildCatalogItem item = items[i];
                bool isSelected = selectedCategory == item.category && selectedIndex == i;
                string costLabel = devBuildMode ? "FREE" : item.cost.ToDisplayString();
                if (!devBuildMode && !CanAfford(item.cost))
                {
                    costLabel = $"NEED {item.cost.ToDisplayString()}";
                }

                string label = $"{item.displayName}\n{GetBuildMenuItemSubtitle(item, costLabel)}";
                GUIStyle itemStyle = isSelected ? selectedCatalogItemButtonStyle : catalogItemButtonStyle;
                if (GUILayout.Button(label, itemStyle, GUILayout.Height(layout.buildItemHeight)))
                {
                    SelectItem(item.category, i);
                }
            }

            if (items.Count == 0)
            {
                GUILayout.Label("No catalog items found for this category.", smallMutedStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label("Selected Preview", headingStyle);
            DrawSelectedPreviewCard(selectedItem, previewHeight);
            GUILayout.Space(6f);
            if (selectedItem != null)
            {
                GUILayout.Label(selectedItem.displayName, labelStyle);
                GUILayout.Label($"{selectedItem.category}  |  {GetItemKindLabel(selectedItem.kind)}", smallMutedStyle);
                GUILayout.Label(devBuildMode ? "Cost: Free in DEV Build Mode" : $"Cost: {selectedItem.cost.ToDisplayString()}", labelStyle);
                if (!devBuildMode && !CanAfford(selectedItem.cost))
                {
                    GUILayout.Label("Missing resources. Press L for DEV Build or gather materials first.", smallMutedStyle);
                }
            }
            else
            {
                GUILayout.Label("Pick an item to start placement.", smallMutedStyle);
            }

            GUILayout.Space(6f);
            GUILayout.Label($"Mode: {(devBuildMode ? "DEV Build" : "Resource Build")}  |  L toggles", smallMutedStyle);
            GUILayout.Label("Wheel/W/S item  A/D tab  F favorite  Enter/LMB choose  B/Esc close", smallMutedStyle);
            GUILayout.EndArea();
        }

        private void DrawContextPrompt()
        {
            if (!showActionHints)
            {
                return;
            }

            string prompt = GetContextPrompt();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.contextRect, prompt, promptLabelStyle);
            GUILayout.BeginArea(rect, windowStyle);
            GUILayout.Label(prompt, promptLabelStyle);
            GUILayout.EndArea();
        }

        private void DrawStatusMessage()
        {
            if (string.IsNullOrWhiteSpace(statusMessage) || Time.unscaledTime > statusMessageUntil)
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.statusRect, statusMessage, labelStyle);
            GUILayout.BeginArea(rect, windowStyle);
            GUILayout.Label(statusMessage, labelStyle);
            GUILayout.EndArea();
        }

        private void DrawInGameMenu()
        {
            float margin = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.04f, 28f, 56f);
            Rect shellRect = new(
                margin,
                margin,
                Mathf.Max(420f, Screen.width - (margin * 2f)),
                Mathf.Max(320f, Screen.height - (margin * 2f)));

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.52f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), panelTexture);
            GUI.color = previousColor;

            GUI.Box(shellRect, GUIContent.none, journalShellStyle);

            Color amberAccent = new Color(0.82f, 0.64f, 0.24f, 0.92f);
            GUI.color = amberAccent;
            GUI.DrawTexture(new Rect(shellRect.x + 18f, shellRect.y + 54f, shellRect.width - 36f, 2f), amberAccentTexture);
            GUI.color = previousColor;

            float padding = Mathf.RoundToInt(18f * currentUiScale);
            Rect navRect = new(shellRect.x + padding, shellRect.y + padding, shellRect.width - (padding * 2f), 74f);
            Rect contentRect = new(
                shellRect.x + padding,
                navRect.yMax + 10f,
                shellRect.width - (padding * 2f),
                shellRect.height - (padding * 2f) - navRect.height - 10f);

            Rect escRect = new(shellRect.xMax - padding - 120f, shellRect.y + padding, 120f, 20f);
            GUI.Label(escRect, "Esc to close", escHintStyle);

            GUILayout.BeginArea(navRect);
            GUILayout.Label("Settler Journal", journalTitleStyle);
            GUILayout.Label("World state, tracking, and character notes", journalSubtitleStyle);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            DrawInGameMenuTabButton(InGameMenuTab.Overview, "Overview");
            DrawInGameMenuTabButton(InGameMenuTab.Skills, "Skills");
            DrawInGameMenuTabButton(InGameMenuTab.Inventory, "Inventory");
            DrawInGameMenuTabButton(InGameMenuTab.Settings, "Settings");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            switch (selectedInGameMenuTab)
            {
                case InGameMenuTab.Overview:
                    DrawOverviewTab(contentRect);
                    break;

                case InGameMenuTab.Skills:
                    DrawSkillsTab(contentRect);
                    break;

                case InGameMenuTab.Inventory:
                    DrawInventoryTab(contentRect);
                    break;

                case InGameMenuTab.Settings:
                    DrawInGameSettingsTab(contentRect);
                    break;
            }
        }

        private void DrawInGameMenuTabButton(InGameMenuTab tab, string label)
        {
            GUIStyle style = selectedInGameMenuTab == tab ? journalActiveTabStyle : journalTabStyle;
            if (GUILayout.Button(label, style, GUILayout.Height(Mathf.RoundToInt(34f * currentUiScale))))
            {
                selectedInGameMenuTab = tab;
            }
        }

        private void DrawJournalPanel(Rect rect, string title, Action drawer, string subtitle = null)
        {
            GUI.Box(rect, GUIContent.none, journalSectionStyle);
            Color previousAccent = GUI.color;
            GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.65f);
            GUI.DrawTexture(new Rect(rect.x + 12f, rect.y + 36f, rect.width - 24f, 1f), amberAccentTexture);
            GUI.color = previousAccent;

            Rect contentRect = new(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f);
            GUILayout.BeginArea(contentRect);
            GUILayout.Label(title, headingStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                GUILayout.Label(subtitle, journalSubtitleStyle);
                GUILayout.Space(6f);
            }
            else
            {
                GUILayout.Space(6f);
            }

            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawOverviewTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float topHeight = Mathf.Clamp(contentRect.height * 0.47f, 210f, 320f);
            float leftWidth = contentRect.width * 0.28f;
            float middleWidth = contentRect.width * 0.34f;
            float rightWidth = contentRect.width - leftWidth - middleWidth - (gutter * 2f);

            Rect mapRect = new(contentRect.x, contentRect.y, leftWidth, topHeight);
            Rect overviewRect = new(mapRect.xMax + gutter, contentRect.y, middleWidth, topHeight);
            Rect timersRect = new(overviewRect.xMax + gutter, contentRect.y, rightWidth, topHeight);

            float bottomHeight = contentRect.height - topHeight - gutter;
            Rect questRect = new(contentRect.x, mapRect.yMax + gutter, leftWidth, bottomHeight);
            Rect socialRect = new(questRect.xMax + gutter, mapRect.yMax + gutter, middleWidth, bottomHeight);
            Rect favoritesRect = new(socialRect.xMax + gutter, mapRect.yMax + gutter, rightWidth, bottomHeight);

            DrawJournalPanel(mapRect, "Area Map", () =>
            {
                if (TryGetTrackedTarget(out PlacedWorldObject trackedObject, out BuildCatalogItem trackedItem, out float trackedDistance))
                {
                    GUILayout.Label($"Tracking: {trackedItem.displayName}  |  {Mathf.RoundToInt(trackedDistance)}m", journalBodyStyle);
                }
                else
                {
                    GUILayout.Label("Tracking: no nearby target", journalSubtitleStyle);
                }

                float mapHeight = Mathf.Max(120f, mapRect.height - 82f);
                Rect mapCanvas = GUILayoutUtility.GetRect(10f, mapHeight, GUILayout.ExpandWidth(true), GUILayout.Height(mapHeight));
                DrawTrackingMap(mapCanvas);
            }, "Live local tracking");

            DrawJournalPanel(overviewRect, "Overview", () =>
            {
                float bar = Mathf.Clamp(13f * currentUiScale, 10f, 16f);
                DrawStatBarRow("Health", health / (float)MaxHealth, statBarHpTexture, $"{health}/{MaxHealth}", bar);
                DrawStatBarRow("Wood", Mathf.Clamp01(wood / 50f), statBarHungerTexture, $"{wood}", bar);
                DrawStatBarRow("Stone", Mathf.Clamp01(stone / 30f), statBarThirstTexture, $"{stone}", bar);
                DrawStatBarRow("Food", Mathf.Clamp01(food / 20f), statBarStaminaTexture, $"{food}", bar);
                GUILayout.Space(6f);
                GUILayout.Label($"Stored: {FormatInventorySummary(storedFoodInventory, 3)}", journalBodyStyle);
                GUILayout.Label($"Arms: {FormatInventorySummary(storedWeaponInventory, 3)}", journalBodyStyle);
                GUILayout.Space(6f);
                GUILayout.Label($"Slot {GetHotbarKeyLabel(selectedHotbarIndex)}  {GetSelectedHotbarItem()?.displayName ?? "Empty"}", journalBodyStyle);
                GUILayout.Label($"{(devBuildMode ? "DEV Build" : "Resource Build")}  |  {GetFacingLabel()}", journalSubtitleStyle);
            }, "Current character state");

            DrawJournalPanel(timersRect, "Timers", () =>
            {
                DrawTimerEntries();
            }, "Planted crops from inventory");

            DrawJournalPanel(questRect, "Quest", () =>
            {
                DrawQuestEntries();
            }, "Current progression goals");

            DrawJournalPanel(socialRect, "Social", () =>
            {
                DrawSocialEntries();
            }, "Settlement and relationship status");

            DrawJournalPanel(favoritesRect, "Favorites", () =>
            {
                DrawFavoritesEntries();
            }, "Pinned build pieces");
        }

        private void DrawSkillsTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.32f, 260f, 360f);
            Rect characterRect = new(contentRect.x, contentRect.y, leftWidth, contentRect.height * 0.48f);
            Rect equipmentRect = new(contentRect.x, characterRect.yMax + gutter, leftWidth, contentRect.height - characterRect.height - gutter);
            Rect skillsRect = new(characterRect.xMax + gutter, contentRect.y, contentRect.width - leftWidth - gutter, contentRect.height);

            DrawJournalPanel(characterRect, "Character", () =>
            {
                float bar = Mathf.Clamp(12f * currentUiScale, 9f, 15f);
                GUILayout.BeginHorizontal();

                Rect portraitRect = GUILayoutUtility.GetRect(96f, 108f, GUILayout.Width(96f), GUILayout.Height(108f));
                DrawCharacterPortraitPlaceholder(portraitRect);

                GUILayout.Space(12f);
                GUILayout.BeginVertical();
                DrawStatBarRow("HP", health / (float)MaxHealth, statBarHpTexture, $"{health}/{MaxHealth}", bar);
                GUILayout.Space(2f);
                GUILayout.Label($"Facing: {GetFacingLabel()}", journalBodyStyle);
                GUILayout.Label($"Mode: {(devBuildMode ? "DEV" : "Resource")}", journalBodyStyle);
                GUILayout.Label($"Food: {storedFoodInventory.Values.Sum()}  |  Arms: {storedWeaponInventory.Values.Sum()}", journalBodyStyle);
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
                GUILayout.Label("A field-ready settler. Equipment and traits will expand as systems come online.", journalSubtitleStyle);
            }, "Current character");

            DrawJournalPanel(equipmentRect, "Armor & Loadout", () =>
            {
                DrawEquipmentSlotGrid();
                GUILayout.Space(10f);
                GUILayout.Label("Armor slots and worn tools will populate here later.", journalSubtitleStyle);
                GUILayout.Label($"Selected Hotbar Item: {GetSelectedHotbarItem()?.displayName ?? "Empty"}", journalBodyStyle);
            }, "Equipment overview");

            DrawJournalPanel(skillsRect, "Skills", () =>
            {
                DrawSkillTrackCard("Builder", Mathf.Clamp(CountPlacedObjects(ItemKind.Structure) / 6, 0, 5), "Settlement growth, structures, housing.");
                GUILayout.Space(8f);
                DrawSkillTrackCard("Forager", Mathf.Clamp((food + storedFoodInventory.Values.Sum()) / 4, 0, 5), "Gathering, crops, regrowth, survival.");
                GUILayout.Space(8f);
                DrawSkillTrackCard("Arms", Mathf.Clamp(storedWeaponInventory.Values.Sum(), 0, 5), "Weapons, combat, defense, tactics.");
                GUILayout.Space(14f);
                GUILayout.Label("Skill Branches", headingStyle);
                GUILayout.Label("Planned paths will branch from these tracks as the game grows.", journalSubtitleStyle);
                GUILayout.Space(8f);
                DrawSkillBranchGrid();
            }, "Character progression");
        }

        private void DrawInventoryTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.2f, 200f, 260f);
            float rightWidth = Mathf.Clamp(contentRect.width * 0.24f, 240f, 320f);
            float centerWidth = contentRect.width - leftWidth - rightWidth - (gutter * 2f);

            Rect leftRect = new(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect centerRect = new(leftRect.xMax + gutter, contentRect.y, centerWidth, contentRect.height);
            Rect rightRect = new(centerRect.xMax + gutter, contentRect.y, rightWidth, contentRect.height);

            float leftTopHeight = Mathf.Clamp(leftRect.height * 0.34f, 180f, 240f);
            Rect resourcesRect = new(leftRect.x, leftRect.y, leftRect.width, leftTopHeight);
            Rect loadoutRect = new(leftRect.x, resourcesRect.yMax + gutter, leftRect.width, leftRect.height - leftTopHeight - gutter);

            List<JournalInventoryEntry> entries = GetJournalInventoryEntries();
            JournalInventoryEntry? selectedEntry = GetSelectedInventoryEntry(entries);

            DrawJournalPanel(resourcesRect, "Resources & Harvest", () =>
            {
                DrawInventoryResourceStrip();
                GUILayout.Space(10f);
                GUILayout.Label("Food", headingStyle);
                GUILayout.BeginHorizontal();
                DrawMiniItemSlot("Pouch", storedFoodInventory.Values.Sum() > 0 ? $"x{storedFoodInventory.Values.Sum()}" : "Empty");
                DrawMiniItemSlot("Rations", food > 0 ? $"x{food}" : "Empty");
                GUILayout.EndHorizontal();
            }, "Carry totals and stored food");

            DrawJournalPanel(loadoutRect, "Armor & Favorites", () =>
            {
                GUILayout.Label("Armor", headingStyle);
                GUILayout.BeginHorizontal();
                DrawMiniItemSlot("Head", "Empty");
                DrawMiniItemSlot("Chest", "Empty");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                DrawMiniItemSlot("Hands", "Empty");
                DrawMiniItemSlot("Feet", "Empty");
                GUILayout.EndHorizontal();

                GUILayout.Space(10f);
                GUILayout.Label("Build Favorites", headingStyle);
                DrawFavoritesEntries();
            }, "Quick access");

            DrawJournalPanel(centerRect, "Inventory Layout", () =>
            {
                DrawInventoryCardGrid(entries, centerRect.width);
            }, "Stored items and harvest");

            DrawJournalPanel(rightRect, "Selected Item", () =>
            {
                DrawInventorySelectionDetails(selectedEntry);
            }, "Details and favorite status");
        }

        private void DrawInGameSettingsTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.55f, 380f, 560f);
            Rect settingsRect = new(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect previewRect = new(settingsRect.xMax + gutter, contentRect.y, contentRect.width - leftWidth - gutter, contentRect.height);

            DrawJournalPanel(settingsRect, "In-Game Settings", () =>
            {
                GUILayout.Label("Gameplay-facing session toggles.", journalSubtitleStyle);
                GUILayout.Space(12f);

                GUILayout.Label("Display", headingStyle);
                GUILayout.Space(6f);
                DrawToggleRow("Crosshair", showCrosshair, () => showCrosshair = !showCrosshair);
                DrawToggleRow("Action Hints", showActionHints, () => showActionHints = !showActionHints);

                GUILayout.Space(10f);
                GUILayout.Label("Camera", headingStyle);
                GUILayout.Space(6f);
                DrawToggleRow("Calm Camera", calmCameraEnabled, () =>
                {
                    calmCameraEnabled = !calmCameraEnabled;
                    ApplyCameraFeel();
                }, "CALM", "FAST");

                GUILayout.Space(10f);
                GUILayout.Label("Gameplay", headingStyle);
                GUILayout.Space(6f);
                DrawToggleRow("DEV Build Mode", devBuildMode, () => devBuildMode = !devBuildMode, "DEV", "RES");
            }, "Session-level options");

            DrawJournalPanel(previewRect, "Preview", () =>
            {
                GUILayout.Label("Crosshair and HUD scale previews will appear here as more settings come online.", journalSubtitleStyle);
                GUILayout.Space(10f);

                float bar = Mathf.Clamp(12f * currentUiScale, 9f, 15f);
                GUILayout.Label("HUD Scale", journalBodyStyle);
                DrawStatBarRow("UI Scale", currentUiScale, skillBarFillTexture, $"{currentUiScale:F2}x", bar);
                GUILayout.Space(8f);
                GUILayout.Label($"Resolution: {Screen.width}x{Screen.height}", journalSubtitleStyle);
            }, "Visual feedback");
        }

        private void DrawTrackingMap(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Rect innerRect = new(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            Color previousColor = GUI.color;

            GUI.color = new Color(0.08f, 0.09f, 0.07f, 0.88f);
            GUI.DrawTexture(innerRect, panelTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(innerRect.center.x, innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.center.y, innerRect.width, 1f), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x + (innerRect.width * 0.25f), innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x + (innerRect.width * 0.75f), innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + (innerRect.height * 0.25f), innerRect.width, 1f), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + (innerRect.height * 0.75f), innerRect.width, 1f), crosshairTexture);

            float range = 40f;
            if (playerTransform != null)
            {
                foreach (PlacedWorldObject placedObject in placedObjects.Values)
                {
                    if (placedObject == null || !catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                    {
                        continue;
                    }

                    if (string.Equals(item.prefabName, "Terrain_01", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Vector3 delta = placedObject.transform.position - playerTransform.position;
                    Vector2 planarDelta = new(delta.x, delta.z);
                    if (planarDelta.magnitude > range)
                    {
                        continue;
                    }

                    Vector2 normalized = planarDelta / range;
                    Vector2 point = new(
                        innerRect.center.x + (normalized.x * innerRect.width * 0.45f),
                        innerRect.center.y - (normalized.y * innerRect.height * 0.45f));

                    RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                    InventoryPickup pickup = placedObject.GetComponent<InventoryPickup>();
                    Color dotColor = pickup != null
                        ? new Color(0.94f, 0.86f, 0.42f, 0.95f)
                        : renewableNode != null
                            ? new Color(0.48f, 0.82f, 0.5f, 0.95f)
                            : new Color(0.82f, 0.86f, 0.88f, 0.72f);

                    DrawMapDot(point, 5f, dotColor);
                }

                DrawMapDot(innerRect.center, 7f, new Color(0.98f, 0.96f, 0.84f, 1f));

                if (TryGetTrackedTarget(out PlacedWorldObject trackedObject, out _, out _))
                {
                    Vector3 trackedDelta = trackedObject.transform.position - playerTransform.position;
                    Vector2 trackedPlanar = new(trackedDelta.x, trackedDelta.z);
                    if (trackedPlanar.magnitude <= range)
                    {
                        Vector2 normalized = trackedPlanar / range;
                        Vector2 point = new(
                            innerRect.center.x + (normalized.x * innerRect.width * 0.45f),
                            innerRect.center.y - (normalized.y * innerRect.height * 0.45f));
                        DrawMapDot(point, 8f, new Color(0.98f, 0.67f, 0.3f, 1f));
                    }
                }
            }

            GUI.color = previousColor;
        }

        private void DrawMapDot(Vector2 center, float size, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - (size * 0.5f), center.y - (size * 0.5f), size, size), crosshairTexture);
            GUI.color = previousColor;
        }

        private void DrawInventoryResourceStrip()
        {
            GUILayout.BeginHorizontal();
            DrawMiniItemSlot("Wood", wood.ToString());
            DrawMiniItemSlot("Stone", stone.ToString());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawMiniItemSlot("Food", food.ToString());
            DrawMiniItemSlot("Weapons", storedWeaponInventory.Values.Sum().ToString());
            GUILayout.EndHorizontal();
        }

        private void DrawMiniItemSlot(string label, string value)
        {
            float width = Mathf.Clamp(84f * currentUiScale, 72f, 108f);
            float height = Mathf.Clamp(66f * currentUiScale, 58f, 82f);
            bool isEmpty = string.Equals(value, "Empty", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.Ordinal);

            GUILayout.BeginVertical(hotbarSlotStyle, GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.Label(label, smallMutedStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isEmpty ? "---" : value, isEmpty ? smallMutedStyle : headingStyle);
            GUILayout.EndVertical();
        }

        private List<JournalInventoryEntry> GetJournalInventoryEntries()
        {
            List<JournalInventoryEntry> entries = new();

            foreach (KeyValuePair<string, int> entry in storedFoodInventory
                         .Where(pair => pair.Value > 0)
                         .OrderBy(pair => GetDisplayNameForItemId(pair.Key)))
            {
                entries.Add(new JournalInventoryEntry(
                    entry.Key,
                    GetDisplayNameForItemId(entry.Key),
                    entry.Value,
                    "Food",
                    IsItemFavorited(entry.Key)));
            }

            foreach (KeyValuePair<string, int> entry in storedWeaponInventory
                         .Where(pair => pair.Value > 0)
                         .OrderBy(pair => GetDisplayNameForItemId(pair.Key)))
            {
                entries.Add(new JournalInventoryEntry(
                    entry.Key,
                    GetDisplayNameForItemId(entry.Key),
                    entry.Value,
                    "Weapon",
                    IsItemFavorited(entry.Key)));
            }

            return entries;
        }

        private JournalInventoryEntry? GetSelectedInventoryEntry(List<JournalInventoryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                selectedInventoryItemId = string.Empty;
                return null;
            }

            if (string.IsNullOrWhiteSpace(selectedInventoryItemId) ||
                entries.All(entry => !string.Equals(entry.itemId, selectedInventoryItemId, StringComparison.Ordinal)))
            {
                selectedInventoryItemId = entries[0].itemId;
            }

            foreach (JournalInventoryEntry entry in entries)
            {
                if (string.Equals(entry.itemId, selectedInventoryItemId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return entries[0];
        }

        private void DrawInventoryCardGrid(List<JournalInventoryEntry> entries, float availableWidth)
        {
            if (entries == null || entries.Count == 0)
            {
                GUILayout.Label("No stored inventory yet.", journalSubtitleStyle);
                GUILayout.Label("Gather crops or pick up crafted gear to populate this layout.", journalSubtitleStyle);
                return;
            }

            int columns = availableWidth >= 620f ? 4 : availableWidth >= 430f ? 3 : 2;
            float gap = Mathf.Clamp(10f * currentUiScale, 8f, 12f);
            float cardWidth = Mathf.Clamp((availableWidth - 56f - (gap * (columns - 1))) / columns, 94f, 150f);
            float cardHeight = Mathf.Clamp(102f * currentUiScale, 92f, 124f);

            inventoryGridScroll = GUILayout.BeginScrollView(inventoryGridScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            int index = 0;
            while (index < entries.Count)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    if (index >= entries.Count)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    DrawInventoryCard(entries[index], cardWidth, cardHeight);
                    if (column < columns - 1)
                    {
                        GUILayout.Space(gap);
                    }

                    index++;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
            GUILayout.EndScrollView();
        }

        private void DrawInventoryCard(JournalInventoryEntry entry, float width, float height)
        {
            bool isSelected = string.Equals(selectedInventoryItemId, entry.itemId, StringComparison.Ordinal);
            GUIStyle style = isSelected ? hotbarSelectedSlotStyle : hotbarSlotStyle;

            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            GUI.Box(rect, GUIContent.none, style);

            if (isSelected)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.7f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), amberAccentTexture);
                GUI.color = prev;
            }

            Rect contentRect = new(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);
            GUILayout.BeginArea(contentRect);
            GUILayout.Label(entry.categoryLabel, journalSubtitleStyle);
            GUILayout.Space(4f);
            GUILayout.Label(GetInventoryCardLabel(entry.displayName), isSelected ? headingStyle : labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"x{entry.quantity}", headingStyle);
            GUILayout.EndArea();

            if (entry.isFavorited)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.95f);
                float starSize = Mathf.RoundToInt(8f * currentUiScale);
                GUI.DrawTexture(new Rect(rect.xMax - starSize - 8f, rect.y + 8f, starSize, starSize), amberAccentTexture);
                GUI.color = prev;
            }

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                selectedInventoryItemId = entry.itemId;
            }
        }

        private string GetInventoryCardLabel(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "Unknown";
            }

            return displayName.Length <= 18 ? displayName : $"{displayName[..16]}..";
        }

        private void DrawInventorySelectionDetails(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue)
            {
                GUILayout.Label("Nothing stored yet.", journalSubtitleStyle);
                GUILayout.Label("Gather food or pick up crafted items to fill this page.", journalSubtitleStyle);
                return;
            }

            JournalInventoryEntry entry = selectedEntry.Value;
            bool isFavorited = IsItemFavorited(entry.itemId);
            string favoritedSlots = GetFavoritedSlotLabels(entry.itemId);

            GUILayout.Label(entry.displayName, headingStyle);
            GUILayout.Label(entry.categoryLabel, journalSubtitleStyle);
            GUILayout.Space(10f);

            Rect badgeRect = GUILayoutUtility.GetRect(10f, Mathf.Clamp(88f * currentUiScale, 74f, 96f), GUILayout.ExpandWidth(true));
            GUI.Box(badgeRect, GUIContent.none, hotbarSlotStyle);

            if (isFavorited)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.55f);
                GUI.DrawTexture(new Rect(badgeRect.x, badgeRect.y, badgeRect.width, 2f), amberAccentTexture);
                GUI.color = prev;
            }

            GUI.Label(new Rect(badgeRect.x + 12f, badgeRect.y + 14f, badgeRect.width - 24f, 22f), $"Stored  x{entry.quantity}", headingStyle);
            GUI.Label(new Rect(badgeRect.x + 12f, badgeRect.y + 44f, badgeRect.width - 24f, 18f),
                isFavorited ? $"Favorite  |  Slots {favoritedSlots}" : "Not pinned to build bar",
                journalSubtitleStyle);

            GUILayout.Space(12f);
            GUILayout.Label("Notes", headingStyle);
            GUILayout.Label(GetInventoryEntryDescription(entry), journalBodyStyle);

            if (catalogLookup.TryGetValue(entry.itemId, out BuildCatalogItem item))
            {
                GUILayout.Space(10f);
                GUILayout.Label("Crafting Link", headingStyle);
                GUILayout.Label($"Build Category: {item.category}", journalBodyStyle);
                GUILayout.Label($"Kind: {GetItemKindLabel(item.kind)}", journalBodyStyle);
                GUILayout.Label(item.cost.IsFree ? "Craft Cost: Free" : $"Craft Cost: {item.cost.ToDisplayString()}", journalBodyStyle);
            }
        }

        private string GetInventoryEntryDescription(JournalInventoryEntry entry)
        {
            return entry.categoryLabel switch
            {
                "Food" => "Food items can be stored here and consumed in the field for recovery when healing is available.",
                "Weapon" => "Weapons stay stored in the journal inventory until the equipment and combat layers come online.",
                _ => "A stored item in the settler journal."
            };
        }

        private bool IsItemFavorited(string itemId)
        {
            return favoriteHotbarItemIds.Any(candidate => string.Equals(candidate, itemId, StringComparison.Ordinal));
        }

        private string GetFavoritedSlotLabels(string itemId)
        {
            List<string> slots = new();
            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                if (string.Equals(favoriteHotbarItemIds[i], itemId, StringComparison.Ordinal))
                {
                    slots.Add(GetHotbarKeyLabel(i));
                }
            }

            return slots.Count > 0 ? string.Join(", ", slots) : "-";
        }

        private void DrawCharacterPortraitPlaceholder(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Rect headRect = new(rect.x + (rect.width * 0.31f), rect.y + 14f, rect.width * 0.38f, rect.width * 0.38f);
            Rect torsoRect = new(rect.x + (rect.width * 0.28f), headRect.yMax + 8f, rect.width * 0.44f, rect.height * 0.34f);
            Rect legsRect = new(rect.x + (rect.width * 0.34f), torsoRect.yMax + 4f, rect.width * 0.32f, rect.height * 0.16f);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.24f, 0.28f, 0.18f, 0.95f);
            GUI.DrawTexture(headRect, journalAccentTexture);
            GUI.DrawTexture(torsoRect, chipTexture);
            GUI.DrawTexture(legsRect, hotbarSelectedTexture);
            GUI.color = previousColor;
        }

        private void DrawEquipmentSlotGrid()
        {
            string[] slotLabels =
            {
                "Head",
                "Chest",
                "Hands",
                "Legs",
                "Feet",
                "Tool"
            };

            int columns = 3;
            int rows = Mathf.CeilToInt(slotLabels.Length / (float)columns);
            float size = Mathf.Clamp(56f * currentUiScale, 44f, 64f);

            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= slotLabels.Length)
                    {
                        break;
                    }

                    Rect slotRect = GUILayoutUtility.GetRect(size, size + 18f, GUILayout.Width(size + 24f), GUILayout.Height(size + 18f));
                    Rect boxRect = new(slotRect.x, slotRect.y, size, size);
                    GUI.Box(boxRect, GUIContent.none, hotbarSlotStyle);
                    GUI.Label(new Rect(slotRect.x, boxRect.yMax + 2f, size + 18f, 16f), slotLabels[index], journalSubtitleStyle);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }
        }

        private void DrawSkillTrackCard(string title, int tier, string description)
        {
            int maxTier = 5;
            float cardHeight = Mathf.Clamp(82f * currentUiScale, 74f, 98f);
            Rect rect = GUILayoutUtility.GetRect(10f, cardHeight, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Rect innerRect = new(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);
            GUILayout.BeginArea(innerRect);
            GUILayout.Label($"{title}  |  Tier {tier}/{maxTier}", headingStyle);
            GUILayout.Label(description, journalSubtitleStyle);
            GUILayout.Space(6f);

            float barHeight = Mathf.Clamp(10f * currentUiScale, 8f, 13f);
            Rect barRect = GUILayoutUtility.GetRect(10f, barHeight, GUILayout.ExpandWidth(true), GUILayout.Height(barHeight));
            GUI.DrawTexture(barRect, skillBarBgTexture);

            float segmentGap = 2f;
            float segmentWidth = (barRect.width - (segmentGap * (maxTier - 1))) / maxTier;
            for (int i = 0; i < maxTier; i++)
            {
                Rect segRect = new(barRect.x + (i * (segmentWidth + segmentGap)), barRect.y + 1f, segmentWidth, barRect.height - 2f);
                if (i < tier)
                {
                    GUI.DrawTexture(segRect, skillBarFillTexture);
                }
                else
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(0.15f, 0.17f, 0.12f, 0.7f);
                    GUI.DrawTexture(segRect, skillBarBgTexture);
                    GUI.color = prev;
                }
            }

            GUILayout.EndArea();
        }

        private void DrawSkillBranchGrid()
        {
            int columns = 3;
            int rows = 2;
            float gap = Mathf.Clamp(10f * currentUiScale, 8f, 12f);
            float nodeSize = Mathf.Clamp(58f * currentUiScale, 50f, 74f);

            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    Rect nodeRect = GUILayoutUtility.GetRect(nodeSize, nodeSize, GUILayout.Width(nodeSize), GUILayout.Height(nodeSize));
                    GUI.Box(nodeRect, GUIContent.none, hotbarSlotStyle);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }

        private void DrawTimerEntries()
        {
            List<TimerDisplayEntry> entries = GetTimerDisplayEntries();
            if (entries.Count == 0)
            {
                GUILayout.Label("No active player timers yet.", journalSubtitleStyle);
                GUILayout.Label("Plant crop seeds to start countdowns.", journalSubtitleStyle);
                return;
            }

            float bar = Mathf.Clamp(11f * currentUiScale, 9f, 14f);
            foreach (TimerDisplayEntry entry in entries)
            {
                DrawStatBarRow(entry.name, entry.progress, statBarStaminaTexture, $"{entry.timeLabel}  {entry.distanceLabel}", bar);
            }
        }

        private struct TimerDisplayEntry
        {
            public string name;
            public float progress;
            public string timeLabel;
            public string distanceLabel;
        }

        private List<TimerDisplayEntry> GetTimerDisplayEntries()
        {
            List<(float distance, TimerDisplayEntry entry)> entries = new();
            if (playerTransform == null)
            {
                return new List<TimerDisplayEntry>();
            }

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                RenewableNode renewableNode = placedObject != null ? placedObject.GetComponent<RenewableNode>() : null;
                if (placedObject == null || !placedObject.PlacedByPlayer || renewableNode == null || renewableNode.IsHarvestable ||
                    !catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item) ||
                    item.renewableVisualMode != RenewableVisualMode.Crop)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                float totalGrow = item.regrowSeconds > 0f ? item.regrowSeconds : 60f;
                float elapsed = totalGrow - renewableNode.RemainingRegrowSeconds;
                float progress = Mathf.Clamp01(elapsed / totalGrow);

                entries.Add((distance, new TimerDisplayEntry
                {
                    name = item.displayName,
                    progress = progress,
                    timeLabel = FormatDurationShort(renewableNode.RemainingRegrowSeconds),
                    distanceLabel = $"{Mathf.RoundToInt(distance)}m"
                }));
            }

            return entries
                .OrderBy(e => e.distance)
                .Take(6)
                .Select(e => e.entry)
                .ToList();
        }

        private List<string> GetTimerEntries()
        {
            List<(float distance, string text)> entries = new();
            if (playerTransform == null)
            {
                return new List<string>();
            }

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                RenewableNode renewableNode = placedObject != null ? placedObject.GetComponent<RenewableNode>() : null;
                if (placedObject == null || !placedObject.PlacedByPlayer || renewableNode == null || renewableNode.IsHarvestable ||
                    !catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item) ||
                    item.renewableVisualMode != RenewableVisualMode.Crop)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                string state = FormatDurationShort(renewableNode.RemainingRegrowSeconds);
                entries.Add((distance, $"{item.displayName}  |  {state}  |  {Mathf.RoundToInt(distance)}m"));
            }

            return entries
                .OrderBy(entry => entry.distance)
                .Take(6)
                .Select(entry => entry.text)
                .ToList();
        }

        private void DrawQuestEntries()
        {
            DrawQuestRow("Gather 10 Wood", wood >= 10, $"{wood}/10");
            DrawQuestRow("Gather 6 Stone", stone >= 6, $"{stone}/6");
            int totalFood = Mathf.Max(food, storedFoodInventory.Values.Sum());
            DrawQuestRow("Collect 3 Food", totalFood >= 3, $"{totalFood}/3");
            DrawQuestRow("Place 1 Structure", CountPlacedObjects(ItemKind.Structure) > 0, $"{CountPlacedObjects(ItemKind.Structure)}");
            DrawQuestRow("Favorite 3 Pieces", CountFavoritedSlots() >= 3, $"{CountFavoritedSlots()}/3");
        }

        private void DrawSocialEntries()
        {
            GUILayout.Label("No settlers discovered yet.", journalBodyStyle);
            GUILayout.Label("Villagers, factions, and social links will appear here.", journalSubtitleStyle);
            GUILayout.Space(10f);
            GUILayout.Label("Settlement", headingStyle);
            GUILayout.Space(4f);

            float bar = Mathf.Clamp(11f * currentUiScale, 9f, 14f);
            int structures = CountPlacedObjects(ItemKind.Structure);
            int renewables = CountHarvestableRenewables();
            DrawStatBarRow("Structures", Mathf.Clamp01(structures / 10f), skillBarFillTexture, $"{structures}", bar);
            DrawStatBarRow("Harvestable", Mathf.Clamp01(renewables / 8f), statBarStaminaTexture, $"{renewables}", bar);

            GUILayout.Space(6f);
            GUILayout.Label($"Tracking: {GetTrackedTargetSummary()}", journalBodyStyle);
        }

        private void DrawFavoritesEntries()
        {
            int rows = Mathf.CeilToInt(HotbarSlotCount / 2f);
            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                DrawFavoriteEntry(row);

                int rightIndex = row + rows;
                if (rightIndex < HotbarSlotCount)
                {
                    GUILayout.Space(10f);
                    DrawFavoriteEntry(rightIndex);
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }
        }

        private void DrawFavoriteEntry(int slotIndex)
        {
            string itemId = favoriteHotbarItemIds[slotIndex];
            bool hasItem = catalogLookup.TryGetValue(itemId ?? string.Empty, out BuildCatalogItem item);
            string slotText = hasItem ? item.displayName : "---";
            bool isActive = slotIndex == selectedHotbarIndex;
            string prefix = isActive ? ">" : GetHotbarKeyLabel(slotIndex);

            float entryWidth = Mathf.Clamp(210f * currentUiScale, 180f, 240f);
            Rect entryRect = GUILayoutUtility.GetRect(entryWidth, Mathf.RoundToInt(20f * currentUiScale), GUILayout.Width(entryWidth));

            if (isActive && hasItem)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(entryRect, panelTexture);
                GUI.color = prev;
            }

            GUIStyle style = isActive ? headingStyle : hasItem ? journalBodyStyle : journalSubtitleStyle;
            GUI.Label(entryRect, $"{prefix}  {slotText}", style);
        }

        private int CountFavoritedSlots()
        {
            return favoriteHotbarItemIds.Count(itemId => !string.IsNullOrWhiteSpace(itemId));
        }

        private string GetTrackedTargetSummary()
        {
            if (TryGetTrackedTarget(out _, out BuildCatalogItem trackedItem, out float distance))
            {
                return $"{trackedItem.displayName} ({Mathf.RoundToInt(distance)}m)";
            }

            return "None";
        }

        private string FormatDurationShort(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainingSeconds = totalSeconds % 60;
            return $"{minutes}:{remainingSeconds:00}";
        }

        private void DrawInventoryEntries(Dictionary<string, int> inventory)
        {
            if (inventory.Count == 0)
            {
                GUILayout.Label("None", journalSubtitleStyle);
                return;
            }

            foreach (KeyValuePair<string, int> entry in inventory.OrderBy(pair => pair.Key))
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                GUILayout.Label($"{GetDisplayNameForItemId(entry.Key)} x{entry.Value}", journalBodyStyle);
            }
        }

        private bool TryGetTrackedTarget(out PlacedWorldObject trackedObject, out BuildCatalogItem trackedItem, out float trackedDistance)
        {
            trackedObject = null;
            trackedItem = null;
            trackedDistance = float.MaxValue;

            if (playerTransform == null)
            {
                return false;
            }

            int bestPriority = int.MaxValue;
            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                if (placedObject == null || !catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    continue;
                }

                if (string.Equals(item.prefabName, "Terrain_01", StringComparison.Ordinal))
                {
                    continue;
                }

                RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                InventoryPickup pickup = placedObject.GetComponent<InventoryPickup>();
                int priority = renewableNode != null && renewableNode.IsHarvestable ? 0 : pickup != null ? 1 : 2;
                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);

                if (priority < bestPriority || (priority == bestPriority && distance < trackedDistance))
                {
                    bestPriority = priority;
                    trackedDistance = distance;
                    trackedObject = placedObject;
                    trackedItem = item;
                }
            }

            return trackedObject != null && trackedItem != null;
        }

        private int CountPlacedObjects(ItemKind itemKind)
        {
            return placedObjects.Values.Count(placedObject => placedObject != null && placedObject.ItemKind == itemKind);
        }

        private int CountHarvestableRenewables()
        {
            return placedObjects.Values.Count(placedObject =>
            {
                RenewableNode renewableNode = placedObject != null ? placedObject.GetComponent<RenewableNode>() : null;
                return renewableNode != null && renewableNode.IsHarvestable;
            });
        }

        private string GetFacingLabel()
        {
            if (playerTransform == null)
            {
                return "Unknown";
            }

            float yaw = playerTransform.eulerAngles.y;
            return yaw switch
            {
                >= 315f or < 45f => "North",
                >= 45f and < 135f => "East",
                >= 135f and < 225f => "South",
                _ => "West"
            };
        }

        private void ApplyCameraFeel()
        {
            if (followCamera == null)
            {
                return;
            }

            if (calmCameraEnabled)
            {
                followCamera.ApplyRuntimeTuning(0.05f, 24f);
            }
            else
            {
                followCamera.ApplyRuntimeTuning(0.035f, 30f);
            }
        }

        private string GetContextPrompt()
        {
            if (buildPanelOpen)
            {
                string modeName = devBuildMode ? "DEV Build" : "Resource Build";
                BuildCatalogItem selectedItem = GetSelectedItem();
                string selectedName = selectedItem != null ? selectedItem.displayName : "None";
                return $"Build Menu  |  {modeName}  |  {selectedName}\nA/D tab  W/S item  F favorite  Enter/LMB choose  L toggle mode  B/Esc close";
            }

            if (placementActive)
            {
                BuildCatalogItem selectedItem = GetSelectedItem();
                if (selectedItem == null)
                {
                    return "Choose a build item from the panel.";
                }

                string buildCost = devBuildMode ? "Free in DEV Build Mode" : selectedItem.cost.ToDisplayString();
                string validity = placementHasValidTarget ? "Placement ready." : "Move away from the player.";
                return $"{selectedItem.displayName}  |  {buildCost}\n{validity}  LMB/Enter place  R rotate  G snap  Wheel/1-0 slot  Esc/RMB cancel";
            }

            if (deleteMode)
            {
                if (targetedPlacedObject != null && catalogLookup.TryGetValue(targetedPlacedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    return $"Delete {item.displayName}\nRefund: {item.cost.ToDisplayString()}";
                }

                return "Delete Tool active.\nAim at a placed object and left click to remove it.";
            }

            if (targetedPickup != null)
            {
                float holdPercent = GetInteractHoldPercent();
                string progressText = holdPercent > 0f ? $" ({Mathf.RoundToInt(holdPercent * 100f)}%)" : string.Empty;
                return $"Hold Interact to {targetedPickup.GetInteractionLabel()}{progressText}";
            }

            if (targetedRenewable != null)
            {
                if (targetedRenewable.IsHarvestable)
                {
                    float holdPercent = GetInteractHoldPercent();
                    string progressText = holdPercent > 0f ? $" ({Mathf.RoundToInt(holdPercent * 100f)}%)" : string.Empty;
                    return $"Hold Interact to gather +{targetedRenewable.YieldAmount} {targetedRenewable.ResourceType.ToString().ToLowerInvariant()}{progressText}";
                }

                string label = targetedRenewable.GetStatusLabel();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }

            return string.Empty;
        }

        private IEnumerable<string> GetActionPanelLines()
        {
            if (buildPanelOpen)
            {
                yield return "Build menu active";
                yield return "A/D tabs | W/S items";
                yield return "Enter choose | F favorite | L mode";
                yield break;
            }

            if (placementActive)
            {
                yield return "Placement active";
                yield return "Enter/LMB place | R rotate | G snap";
                yield return "Wheel/1-0 slot | Esc cancel";
                yield break;
            }

            if (deleteMode)
            {
                yield return "Delete tool active";
                yield return "Aim at a placed object";
                yield return "LMB remove | X exit";
                yield break;
            }

            if (targetedPickup != null || targetedRenewable != null)
            {
                yield return "Gather or collect";
                yield return "Hold Interact to use target";
                yield return "B build | Wheel/1-0 hotbar";
                yield break;
            }

            yield return "Explore and build";
            yield return "B build | X delete | H heal";
            yield return "Wheel or 1-0 selects hotbar";
        }

        private void DrawCategoryButton(BuildCategory category)
        {
            bool isCurrent = selectedCategory == category;
            GUIStyle style = isCurrent ? selectedTabButtonStyle : tabButtonStyle;
            int itemCount = itemsByCategory.TryGetValue(category, out List<BuildCatalogItem> items) ? items.Count : 0;
            if (GUILayout.Button($"{category} ({itemCount})", style, GUILayout.Height(Mathf.RoundToInt(30f * currentUiScale))))
            {
                SelectCategory(category);
            }
        }

        private void DrawResourceBadge(string text, float width, float height)
        {
            GUILayout.Box(text, resourceBadgeStyle, GUILayout.Height(height), GUILayout.Width(width));
        }

        private void DrawStatBar(Rect rect, float fraction, Texture2D fillTexture, string label, string valueText)
        {
            fraction = Mathf.Clamp01(fraction);
            Color previousColor = GUI.color;

            GUI.DrawTexture(rect, statBarBgTexture);

            float borderInset = 1f;
            Rect innerRect = new(rect.x + borderInset, rect.y + borderInset, rect.width - (borderInset * 2f), rect.height - (borderInset * 2f));
            float fillWidth = innerRect.width * fraction;
            if (fillWidth > 0f)
            {
                GUI.DrawTexture(new Rect(innerRect.x, innerRect.y, fillWidth, innerRect.height), fillTexture);
            }

            Rect labelRect = new(rect.x + 6f, rect.y, rect.width * 0.5f, rect.height);
            GUI.Label(labelRect, label, statLabelStyle);

            Rect valueRect = new(rect.x + (rect.width * 0.5f), rect.y, rect.width * 0.5f - 6f, rect.height);
            GUI.Label(valueRect, valueText, valueLabelStyle);

            GUI.color = previousColor;
        }

        private void DrawStatBarRow(string label, float fraction, Texture2D fillTexture, string valueText, float barHeight)
        {
            Rect barRect = GUILayoutUtility.GetRect(10f, barHeight, GUILayout.ExpandWidth(true), GUILayout.Height(barHeight));
            DrawStatBar(barRect, fraction, fillTexture, label, valueText);
            GUILayout.Space(2f);
        }

        private void DrawBorderedPanel(Rect rect, Action drawer)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.24f, 0.28f, 0.18f, 0.25f);
            GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), borderDarkTexture);
            GUI.color = previousColor;
            GUI.Box(rect, GUIContent.none, hudCardStyle);

            Rect contentRect = new(
                rect.x + hudCardStyle.padding.left,
                rect.y + hudCardStyle.padding.top,
                rect.width - hudCardStyle.padding.left - hudCardStyle.padding.right,
                rect.height - hudCardStyle.padding.top - hudCardStyle.padding.bottom);
            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawToggleRow(string label, bool isOn, Action onToggle, string onLabel = "ON", string offLabel = "OFF")
        {
            float rowHeight = Mathf.RoundToInt(32f * currentUiScale);
            Rect rowRect = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));

            Color previousColor = GUI.color;
            GUI.color = new Color(0.12f, 0.14f, 0.1f, 0.5f);
            GUI.DrawTexture(rowRect, panelTexture);
            GUI.color = previousColor;

            Rect labelRect = new(rowRect.x + 14f, rowRect.y, rowRect.width - 120f, rowRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            float toggleWidth = Mathf.RoundToInt(64f * currentUiScale);
            float toggleHeight = Mathf.RoundToInt(22f * currentUiScale);
            Rect toggleRect = new(rowRect.xMax - toggleWidth - 14f, rowRect.y + ((rowRect.height - toggleHeight) * 0.5f), toggleWidth, toggleHeight);
            GUIStyle toggleStyle = isOn ? toggleOnStyle : toggleOffStyle;
            string toggleLabel = isOn ? onLabel : offLabel;

            if (GUI.Button(toggleRect, toggleLabel, toggleStyle))
            {
                onToggle?.Invoke();
            }

            GUILayout.Space(4f);
        }

        private void DrawQuestRow(string label, bool complete, string progress)
        {
            float rowHeight = Mathf.RoundToInt(26f * currentUiScale);
            Rect rowRect = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));

            if (complete)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.2f, 0.32f, 0.18f, 0.25f);
                GUI.DrawTexture(rowRect, panelTexture);
                GUI.color = previousColor;
            }

            float checkSize = Mathf.RoundToInt(14f * currentUiScale);
            Rect checkRect = new(rowRect.x + 8f, rowRect.y + ((rowHeight - checkSize) * 0.5f), checkSize, checkSize);

            if (complete)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.42f, 0.72f, 0.38f, 0.98f);
                GUI.DrawTexture(checkRect, questCheckTexture);
                GUI.color = previousColor;
            }
            else
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.3f, 0.32f, 0.26f, 0.6f);
                GUI.DrawTexture(checkRect, panelTexture);
                GUI.color = previousColor;
            }

            GUIStyle textStyle = complete ? questDoneStyle : questPendingStyle;
            Rect labelRect = new(checkRect.xMax + 8f, rowRect.y, rowRect.width - checkRect.xMax - 80f, rowHeight);
            GUI.Label(labelRect, label, textStyle);

            Rect progressRect = new(rowRect.xMax - 70f, rowRect.y, 62f, rowHeight);
            GUI.Label(progressRect, progress, valueLabelStyle);

            GUILayout.Space(2f);
        }

        private void EnsureGuiStyles()
        {
            float targetUiScale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.78f, 1.12f);

            if (windowStyle != null)
            {
                if (Mathf.Abs(targetUiScale - currentUiScale) > 0.01f)
                {
                    ApplyGuiScale(targetUiScale);
                }

                return;
            }

            EnsureGuiTextures();

            windowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12),
                fontSize = 12,
                border = new RectOffset(1, 1, 1, 1),
                normal =
                {
                    background = panelTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                }
            };

            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.95f, 0.96f, 0.88f) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.89f, 0.92f, 0.84f) }
            };

            smallMutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.73f, 0.77f, 0.67f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal =
                {
                    background = buttonTexture,
                    textColor = new Color(0.9f, 0.92f, 0.84f)
                },
                hover =
                {
                    background = chipTexture,
                    textColor = Color.white
                },
                active =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                }
            };

            hotbarSlotStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4),
                fontSize = 11,
                normal =
                {
                    background = hotbarTexture,
                    textColor = new Color(0.85f, 0.88f, 0.8f)
                }
            };

            hotbarSelectedSlotStyle = new GUIStyle(hotbarSlotStyle)
            {
                normal =
                {
                    background = hotbarSelectedTexture,
                    textColor = new Color(0.98f, 0.94f, 0.72f)
                }
            };

            hotbarKeyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.96f, 0.88f) }
            };

            tabButtonStyle = new GUIStyle(buttonStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            selectedTabButtonStyle = new GUIStyle(buttonStyle)
            {
                normal =
                {
                    background = chipTexture,
                    textColor = new Color(0.98f, 0.97f, 0.82f)
                }
            };

            catalogItemButtonStyle = new GUIStyle(buttonStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8)
            };

            selectedCatalogItemButtonStyle = new GUIStyle(catalogItemButtonStyle)
            {
                normal =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                },
                hover =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                },
                active =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                }
            };

            resourceBadgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 4, 4),
                normal =
                {
                    background = chipTexture,
                    textColor = new Color(0.93f, 0.94f, 0.86f)
                }
            };

            promptLabelStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            journalShellStyle = new GUIStyle(windowStyle)
            {
                normal =
                {
                    background = journalShellTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                },
                padding = new RectOffset(20, 20, 18, 18)
            };

            journalSectionStyle = new GUIStyle(windowStyle)
            {
                normal =
                {
                    background = journalCardTexture,
                    textColor = new Color(0.91f, 0.92f, 0.86f)
                },
                padding = new RectOffset(14, 14, 12, 12)
            };

            journalTitleStyle = new GUIStyle(headingStyle)
            {
                fontSize = 20,
                normal = { textColor = new Color(0.97f, 0.96f, 0.9f) }
            };

            journalSubtitleStyle = new GUIStyle(smallMutedStyle)
            {
                normal = { textColor = new Color(0.76f, 0.8f, 0.69f) }
            };

            journalTabStyle = new GUIStyle(buttonStyle)
            {
                normal =
                {
                    background = journalTabTexture,
                    textColor = new Color(0.86f, 0.88f, 0.8f)
                }
            };

            journalActiveTabStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    background = journalTabActiveTexture,
                    textColor = new Color(0.98f, 0.92f, 0.62f)
                }
            };

            journalBodyStyle = new GUIStyle(labelStyle)
            {
                normal = { textColor = new Color(0.89f, 0.9f, 0.84f) }
            };

            modeLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.98f, 0.94f, 0.72f) }
            };

            keyHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.64f, 0.54f) }
            };

            statLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.78f, 0.8f, 0.72f) }
            };

            valueLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.93f, 0.93f, 0.88f) }
            };

            settingsRowLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.91f, 0.92f, 0.86f) }
            };

            toggleOnStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = toggleOnTexture,
                    textColor = new Color(0.98f, 0.98f, 0.95f)
                }
            };

            toggleOffStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = toggleOffTexture,
                    textColor = new Color(0.58f, 0.6f, 0.52f)
                }
            };

            questDoneStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.48f, 0.78f, 0.44f) }
            };

            questPendingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.72f, 0.74f, 0.66f) }
            };

            hudCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                border = new RectOffset(1, 1, 1, 1),
                normal =
                {
                    background = hudCardTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                }
            };

            escHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.52f, 0.54f, 0.46f) }
            };

            ApplyGuiScale(targetUiScale);
        }

        private void ApplyGuiScale(float targetUiScale)
        {
            currentUiScale = targetUiScale;

            windowStyle.padding = new RectOffset(
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale));

            headingStyle.fontSize = Mathf.RoundToInt(15f * targetUiScale);
            labelStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            smallMutedStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            buttonStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            tabButtonStyle.fontSize = buttonStyle.fontSize;
            selectedTabButtonStyle.fontSize = buttonStyle.fontSize;
            catalogItemButtonStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            selectedCatalogItemButtonStyle.fontSize = catalogItemButtonStyle.fontSize;
            catalogItemButtonStyle.padding = new RectOffset(
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale));
            selectedCatalogItemButtonStyle.padding = catalogItemButtonStyle.padding;

            hotbarSlotStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            hotbarSlotStyle.padding = new RectOffset(
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale));

            hotbarSelectedSlotStyle.fontSize = hotbarSlotStyle.fontSize;
            hotbarSelectedSlotStyle.padding = hotbarSlotStyle.padding;
            hotbarKeyStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            resourceBadgeStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            resourceBadgeStyle.padding = new RectOffset(
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale));
            promptLabelStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            journalShellStyle.padding = new RectOffset(
                Mathf.RoundToInt(20f * targetUiScale),
                Mathf.RoundToInt(20f * targetUiScale),
                Mathf.RoundToInt(18f * targetUiScale),
                Mathf.RoundToInt(18f * targetUiScale));
            journalSectionStyle.padding = new RectOffset(
                Mathf.RoundToInt(14f * targetUiScale),
                Mathf.RoundToInt(14f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale));
            journalTitleStyle.fontSize = Mathf.RoundToInt(20f * targetUiScale);
            journalSubtitleStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            journalTabStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            journalActiveTabStyle.fontSize = journalTabStyle.fontSize;
            journalBodyStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);

            modeLabelStyle.fontSize = Mathf.RoundToInt(13f * targetUiScale);
            keyHintStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            statLabelStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            valueLabelStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            settingsRowLabelStyle.fontSize = Mathf.RoundToInt(13f * targetUiScale);
            toggleOnStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            toggleOffStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            questDoneStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            questPendingStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            hudCardStyle.padding = new RectOffset(
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale));
            escHintStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
        }

        private string GetBuildMenuItemSubtitle(BuildCatalogItem item, string costLabel)
        {
            if (item == null)
            {
                return string.Empty;
            }

            return $"{GetItemKindLabel(item.kind)}  |  {costLabel}";
        }

        private string GetItemKindLabel(ItemKind kind)
        {
            return kind switch
            {
                ItemKind.Pickup => "Pickup",
                ItemKind.RenewableNode => "Renewable",
                _ => "Structure"
            };
        }

        private void DrawSelectedPreviewCard(BuildCatalogItem item, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Texture previewTex = GetBuildPreviewTexture(item);
            if (previewTex != null)
            {
                float margin = 6f;
                Rect texRect = new(rect.x + margin, rect.y + margin, rect.width - (margin * 2f), rect.height - (margin * 2f) - 28f);
                GUI.DrawTexture(texRect, previewTex, ScaleMode.ScaleToFit);

                Rect labelRect = new(rect.x + margin, rect.yMax - 26f, rect.width - (margin * 2f), 22f);
                GUI.Label(labelRect, item != null ? $"{item.displayName}  |  {item.category}" : string.Empty, smallMutedStyle);
            }
            else
            {
                string previewText = item == null
                    ? "No item selected"
                    : $"{item.displayName}\n{item.category}  |  {GetItemKindLabel(item.kind)}";
                GUI.Label(rect, previewText, promptLabelStyle);
            }
        }

        private Texture GetBuildPreviewTexture(BuildCatalogItem item)
        {
            if (item == null || item.prefab == null)
            {
                CleanupPreviewInstance();
                previewCatalogId = string.Empty;
                return null;
            }

            EnsurePreviewStage();
            if (previewCamera == null || previewRenderTexture == null)
            {
                return null;
            }

            if (!string.Equals(previewCatalogId, item.id, StringComparison.Ordinal))
            {
                RebuildPreviewInstance(item);
            }

            if (previewInstance == null)
            {
                return null;
            }

            QueuePreviewRender();
            return previewRenderTexture;
        }

        private void EnsurePreviewStage()
        {
            if (previewStageRoot == null)
            {
                previewStageRoot = new GameObject("Build Preview Stage")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                previewStageRoot.transform.position = new Vector3(0f, -2000f, 0f);
            }

            if (previewRenderTexture == null)
            {
                previewRenderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "MP Settlers Build Preview"
                };
                previewRenderTexture.Create();
            }

            if (previewCamera == null)
            {
                GameObject cameraObject = new("Build Preview Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                cameraObject.transform.SetParent(previewStageRoot.transform, false);
                previewCamera = cameraObject.AddComponent<Camera>();
                previewCamera.enabled = false;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0.13f, 0.15f, 0.1f, 1f);
                previewCamera.fieldOfView = 26f;
                previewCamera.nearClipPlane = 0.01f;
                previewCamera.farClipPlane = 100f;
                previewCamera.allowHDR = false;
                previewCamera.allowMSAA = true;
                previewCamera.cullingMask = 1 << PreviewLayer;
            }

            if (previewLight == null)
            {
                GameObject lightObject = new("Build Preview Light")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                lightObject.transform.SetParent(previewStageRoot.transform, false);
                previewLight = lightObject.AddComponent<Light>();
                previewLight.type = LightType.Directional;
                previewLight.intensity = 1.15f;
                previewLight.color = new Color(1f, 0.97f, 0.91f);
                previewLight.shadows = LightShadows.None;
                previewLight.cullingMask = 1 << PreviewLayer;
                previewLight.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            }

            ExcludePreviewLayerFromMainCamera();
        }

        private void RebuildPreviewInstance(BuildCatalogItem item)
        {
            CleanupPreviewInstance();
            previewCatalogId = item.id;

            previewInstance = Instantiate(item.prefab, previewStageRoot.transform);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            previewInstance.name = $"{item.prefab.name}_Preview";
            previewInstance.transform.position = previewStageRoot.transform.position;
            previewInstance.transform.rotation = Quaternion.Euler(0f, 24f, 0f);
            SetLayerRecursively(previewInstance.transform, PreviewLayer);
            DisableComponentsForPreview(previewInstance);
            AlignObjectToGround(previewInstance);
            PositionPreviewCamera(previewInstance);
            QueuePreviewRender();
        }

        private void DisableComponentsForPreview(GameObject instanceObject)
        {
            foreach (MonoBehaviour behaviour in instanceObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Animator animator in instanceObject.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }

            foreach (Collider colliderComponent in instanceObject.GetComponentsInChildren<Collider>(true))
            {
                colliderComponent.enabled = false;
            }

            foreach (CharacterController controller in instanceObject.GetComponentsInChildren<CharacterController>(true))
            {
                controller.enabled = false;
            }

            foreach (Rigidbody rigidbodyComponent in instanceObject.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbodyComponent.isKinematic = true;
                rigidbodyComponent.useGravity = false;
            }

            foreach (Light lightComponent in instanceObject.GetComponentsInChildren<Light>(true))
            {
                lightComponent.enabled = false;
            }

            foreach (AudioSource audioSource in instanceObject.GetComponentsInChildren<AudioSource>(true))
            {
                audioSource.enabled = false;
            }

            foreach (Canvas canvas in instanceObject.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = false;
            }
        }

        private void PositionPreviewCamera(GameObject instanceObject)
        {
            if (previewCamera == null || instanceObject == null || !TryGetRenderableBounds(instanceObject, out Bounds bounds))
            {
                return;
            }

            Vector3 focusPoint = bounds.center + Vector3.up * (bounds.size.y * 0.08f);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            float distance = Mathf.Clamp(radius / Mathf.Sin(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad), 2.5f, 18f);
            Vector3 viewDirection = new Vector3(-0.58f, 0.3f, -1f).normalized;
            previewCamera.transform.position = focusPoint - (viewDirection * distance);
            previewCamera.transform.LookAt(focusPoint);
        }

        private void ExcludePreviewLayerFromMainCamera()
        {
            if (mainCamera != null)
            {
                mainCamera.cullingMask &= ~(1 << PreviewLayer);
            }
        }

        private void EnsureGuiTextures()
        {
            panelTexture ??= CreateSolidTexture(new Color(0.1f, 0.12f, 0.08f, 0.82f));
            buttonTexture ??= CreateSolidTexture(new Color(0.17f, 0.2f, 0.13f, 0.92f));
            buttonSelectedTexture ??= CreateSolidTexture(new Color(0.68f, 0.62f, 0.32f, 0.96f));
            hotbarTexture ??= CreateSolidTexture(new Color(0.13f, 0.15f, 0.1f, 0.92f));
            hotbarSelectedTexture ??= CreateSolidTexture(new Color(0.36f, 0.34f, 0.16f, 0.98f));
            chipTexture ??= CreateSolidTexture(new Color(0.23f, 0.29f, 0.18f, 0.95f));
            crosshairTexture ??= CreateSolidTexture(Color.white);
            journalShellTexture ??= CreateSolidTexture(new Color(0.07f, 0.08f, 0.06f, 0.97f));
            journalCardTexture ??= CreateSolidTexture(new Color(0.1f, 0.12f, 0.08f, 0.92f));
            journalTabTexture ??= CreateSolidTexture(new Color(0.15f, 0.18f, 0.11f, 0.98f));
            journalTabActiveTexture ??= CreateSolidTexture(new Color(0.34f, 0.39f, 0.19f, 0.99f));
            journalAccentTexture ??= CreateSolidTexture(new Color(0.73f, 0.77f, 0.47f, 1f));
            statBarBgTexture ??= CreateSolidTexture(new Color(0.06f, 0.07f, 0.05f, 0.92f));
            statBarHpTexture ??= CreateSolidTexture(new Color(0.74f, 0.26f, 0.2f, 0.98f));
            statBarStaminaTexture ??= CreateSolidTexture(new Color(0.3f, 0.62f, 0.36f, 0.98f));
            statBarHungerTexture ??= CreateSolidTexture(new Color(0.72f, 0.54f, 0.22f, 0.98f));
            statBarThirstTexture ??= CreateSolidTexture(new Color(0.26f, 0.5f, 0.72f, 0.98f));
            amberAccentTexture ??= CreateSolidTexture(new Color(0.82f, 0.64f, 0.24f, 1f));
            borderDarkTexture ??= CreateSolidTexture(new Color(0.04f, 0.05f, 0.03f, 0.6f));
            hudCardTexture ??= CreateSolidTexture(new Color(0.11f, 0.13f, 0.09f, 0.88f));
            modeBadgeTexture ??= CreateSolidTexture(new Color(0.18f, 0.22f, 0.13f, 0.96f));
            toggleOnTexture ??= CreateSolidTexture(new Color(0.34f, 0.56f, 0.3f, 0.98f));
            toggleOffTexture ??= CreateSolidTexture(new Color(0.22f, 0.24f, 0.18f, 0.92f));
            questCheckTexture ??= CreateSolidTexture(new Color(0.42f, 0.72f, 0.38f, 0.98f));
            skillBarBgTexture ??= CreateSolidTexture(new Color(0.1f, 0.12f, 0.08f, 0.88f));
            skillBarFillTexture ??= CreateSolidTexture(new Color(0.62f, 0.56f, 0.28f, 0.96f));
        }

        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void CleanupGuiTextures()
        {
            DestroyTexture(panelTexture);
            DestroyTexture(buttonTexture);
            DestroyTexture(buttonSelectedTexture);
            DestroyTexture(hotbarTexture);
            DestroyTexture(hotbarSelectedTexture);
            DestroyTexture(chipTexture);
            DestroyTexture(crosshairTexture);
            DestroyTexture(journalShellTexture);
            DestroyTexture(journalCardTexture);
            DestroyTexture(journalTabTexture);
            DestroyTexture(journalTabActiveTexture);
            DestroyTexture(journalAccentTexture);
            DestroyTexture(statBarBgTexture);
            DestroyTexture(statBarHpTexture);
            DestroyTexture(statBarStaminaTexture);
            DestroyTexture(statBarHungerTexture);
            DestroyTexture(statBarThirstTexture);
            DestroyTexture(amberAccentTexture);
            DestroyTexture(borderDarkTexture);
            DestroyTexture(hudCardTexture);
            DestroyTexture(modeBadgeTexture);
            DestroyTexture(toggleOnTexture);
            DestroyTexture(toggleOffTexture);
            DestroyTexture(questCheckTexture);
            DestroyTexture(skillBarBgTexture);
            DestroyTexture(skillBarFillTexture);
        }

        private void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        private void QueuePreviewRender()
        {
            if (previewRenderQueued || !isActiveAndEnabled || previewCamera == null || previewRenderTexture == null)
            {
                return;
            }

            previewRenderQueued = true;
            StartCoroutine(RenderPreviewNextFrame());
        }

        private IEnumerator RenderPreviewNextFrame()
        {
            yield return new WaitForEndOfFrame();
            previewRenderQueued = false;

            if (!buildPanelOpen || previewCamera == null || previewRenderTexture == null || previewInstance == null)
            {
                yield break;
            }

            UniversalRenderPipeline.SingleCameraRequest request = new()
            {
                destination = previewRenderTexture
            };

            if (RenderPipeline.SupportsRenderRequest(previewCamera, request))
            {
                RenderPipeline.SubmitRenderRequest(previewCamera, request);
            }
        }

        private void CleanupPreviewResources()
        {
            CleanupPreviewInstance();
            previewRenderQueued = false;

            if (previewRenderTexture != null)
            {
                previewRenderTexture.Release();
                Destroy(previewRenderTexture);
                previewRenderTexture = null;
            }

            if (previewStageRoot != null)
            {
                Destroy(previewStageRoot);
                previewStageRoot = null;
            }

            previewCamera = null;
            previewLight = null;
            previewCatalogId = string.Empty;
        }

        private void CleanupPreviewInstance()
        {
            if (previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
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

            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(GetSavePath(), json);
        }

        private PlacedObjectSaveData CapturePlacedObjectState(PlacedWorldObject placedObject)
        {
            RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
            return new PlacedObjectSaveData
            {
                uniqueId = placedObject.UniqueId,
                catalogItemId = placedObject.CatalogItemId,
                position = placedObject.transform.position,
                rotation = placedObject.transform.rotation,
                placedByPlayer = placedObject.PlacedByPlayer,
                renewableNodeState = renewableNode != null ? renewableNode.CaptureState() : null
            };
        }

        private void ApplyDamage(int damage)
        {
            int previousHealth = health;
            health = Mathf.Clamp(health - damage, 0, MaxHealth);
            if (health == previousHealth)
            {
                return;
            }

            SetStatusMessage($"Took {damage} fall damage.");
            SaveWorld();
        }

        private void AddResource(ResourceType resourceType, int amount)
        {
            switch (resourceType)
            {
                case ResourceType.Wood:
                    wood += amount;
                    break;
                case ResourceType.Stone:
                    stone += amount;
                    break;
                case ResourceType.Food:
                    food += amount;
                    break;
            }
        }

        private bool CanAfford(BuildCost cost)
        {
            if (cost == null)
            {
                return true;
            }

            return wood >= cost.wood && stone >= cost.stone && food >= cost.food;
        }

        private void SpendCost(BuildCost cost)
        {
            if (cost == null)
            {
                return;
            }

            wood = Mathf.Max(0, wood - cost.wood);
            stone = Mathf.Max(0, stone - cost.stone);
            food = Mathf.Max(0, food - cost.food);
        }

        private void RefundCost(BuildCost cost)
        {
            if (cost == null)
            {
                return;
            }

            wood += Mathf.Max(0, cost.wood);
            stone += Mathf.Max(0, cost.stone);
            food += Mathf.Max(0, cost.food);
        }

        private void AddInventoryCount(Dictionary<string, int> inventory, string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            {
                return;
            }

            inventory.TryGetValue(itemId, out int currentCount);
            inventory[itemId] = currentCount + amount;
        }

        private void RemoveInventoryCount(Dictionary<string, int> inventory, string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            {
                return;
            }

            if (!inventory.TryGetValue(itemId, out int currentCount))
            {
                return;
            }

            currentCount -= amount;
            if (currentCount <= 0)
            {
                inventory.Remove(itemId);
                return;
            }

            inventory[itemId] = currentCount;
        }

        private string FormatInventorySummary(Dictionary<string, int> inventory, int maxEntries)
        {
            if (inventory.Count == 0)
            {
                return "None";
            }

            List<string> entries = inventory
                .Where(entry => entry.Value > 0)
                .OrderBy(entry => entry.Key)
                .Take(maxEntries)
                .Select(entry => $"{GetDisplayNameForItemId(entry.Key)} x{entry.Value}")
                .ToList();

            int remainingCount = inventory.Count - entries.Count;
            if (remainingCount > 0)
            {
                entries.Add($"+{remainingCount} more");
            }

            return string.Join(", ", entries);
        }

        private string GetDisplayNameForItemId(string itemId)
        {
            if (catalogLookup.TryGetValue(itemId, out BuildCatalogItem item) && !string.IsNullOrWhiteSpace(item.displayName))
            {
                return item.displayName;
            }

            return itemId;
        }

        private string GetHotbarKeyLabel(int slotIndex)
        {
            return slotIndex == 9 ? "0" : (slotIndex + 1).ToString();
        }

        private string GetHotbarSlotLabel(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "Empty";
            }

            return displayName.Length <= 14 ? displayName : $"{displayName[..12]}..";
        }

        private void SetStatusMessage(string message, float duration = 2.5f)
        {
            statusMessage = message ?? string.Empty;
            statusMessageUntil = Time.unscaledTime + duration;
        }

        private string GetSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveFileName);
        }

        private bool TryGetCameraRaycast(float maxDistance, out RaycastHit validHit)
        {
            validHit = default;
            if (mainCamera == null)
            {
                return false;
            }

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
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

                if (ghostInstance != null && hitTransform.IsChildOf(ghostInstance.transform))
                {
                    continue;
                }

                validHit = hit;
                return true;
            }

            return false;
        }

        private bool TryGetAttackPressedThisFrame()
        {
            if (attackAction != null)
            {
                return attackAction.WasPressedThisFrame();
            }

            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        }

        private bool TryGetSubmitPressedThisFrame()
        {
            return Keyboard.current != null &&
                   (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        }

        private bool TryGetInteractTriggered()
        {
            if (pointerMode || placementActive || deleteMode)
            {
                return false;
            }

            if (interactAction != null)
            {
                return interactAction.triggered;
            }

            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        }

        private bool TryGetPreviousPressedThisFrame()
        {
            if (previousAction != null)
            {
                return previousAction.WasPressedThisFrame();
            }

            return Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame;
        }

        private bool TryGetNextPressedThisFrame()
        {
            if (nextAction != null)
            {
                return nextAction.WasPressedThisFrame();
            }

            return Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame;
        }

        private float GetInteractHoldPercent()
        {
            if (interactAction == null || !interactAction.IsPressed())
            {
                return 0f;
            }

            return Mathf.Clamp01(interactAction.GetTimeoutCompletionPercentage());
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

        private void UpdateGhostTint(bool isValid)
        {
            Color tint = isValid ? new Color(0.35f, 1f, 0.45f, 0.92f) : new Color(1f, 0.35f, 0.35f, 0.92f);
            foreach (Renderer rendererComponent in ghostRenderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                foreach (Material material in rendererComponent.materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", tint);
                    }

                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", tint);
                    }
                }
            }
        }

        private void SetLayerRecursively(Transform targetTransform, int layer)
        {
            targetTransform.gameObject.layer = layer;
            foreach (Transform child in targetTransform)
            {
                SetLayerRecursively(child, layer);
            }
        }
    }
}
