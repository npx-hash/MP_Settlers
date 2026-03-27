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
        private readonly Dictionary<string, EquippedSlot> equippedSlots = new(StringComparer.Ordinal);
        private float weaponSwingCooldownUntil = 0f;
        private bool isSwinging = false;
        private bool isRaising = false;
        private float swingProgress = 0f;
        private float raiseProgress = 0f;
        private Transform cachedRightHandBone = null;
        private Transform cachedLeftHandBone = null;
        private static readonly Vector3 SwingStartAngle = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 SwingPeakAngle = new Vector3(-90f, 0f, -30f);
        private static readonly Vector3 SwingEndAngle = new Vector3(40f, 0f, 20f);
        private static readonly Vector3 RaiseAngle = new Vector3(-60f, 0f, 0f);
        private static readonly Vector3 RestAngle = new Vector3(0f, 0f, 0f);
        private readonly string[] favoriteHotbarItemIds = new string[HotbarSlotCount];

        private BuildCatalogDatabase catalogDatabase;
        private PlayerMovementController playerMovement;
        private WeaponMount playerWeaponMount;
        private ArmorMount playerArmorMount;
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
        private bool escMenuOpen;
        private bool pendingEscMenuOpen;
        private bool pendingEscMenuClose;
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
        private float settingsUiScaleMultiplier = 1f;
        private bool showFps = false;

        private enum CameraFeelMode
        {
            Calm,
            Normal,
            Responsive
        }

        private CameraFeelMode cameraFeelMode = CameraFeelMode.Calm;
        private float cameraSmoothingMultiplier = 1f; // multiplier applied to the preset

        private bool pauseGameWhenJournalOpen;
        private float timeScaleBeforePause = 1f;

        // Camera look controls (cached from followCamera when opening the settings tab).
        private float cameraMouseSensitivity = 0.12f;
        private bool cameraInvertY;
        private float cameraDistance = 5f;
        private bool settingsCameraCacheInitialized;
        private Vector2 inventoryGridScroll;

        // ── World container storage (placed object only) ──────────────
        private const int BarrelStorageCapacity = 20;
        private const int BoxStorageCapacity = 30;
        private readonly Dictionary<string, ContainerRuntimeStorage> containerStorageByObjectId = new(StringComparer.Ordinal);
        private PlacedWorldObject activeContainerObject;
        private bool containerStorageOpen;
        private Vector2 containerPlayerItemsScroll;
        private Vector2 containerStoredItemsScroll;
        private int containerTransferAmount = 1;

        // ── Crafting ─────────────────────────────────────────────────
        private List<CraftingRecipe> craftingRecipes;
        private int selectedRecipeIndex;
        private Vector2 craftingScrollPos;

        // ── Seed / Farming ──────────────────────────────────────────
        // Maps virtual seed item IDs to the crop catalog ID they produce when planted.
        private readonly Dictionary<string, string> seedToCropMap = new(StringComparer.Ordinal);
        private bool plantingMode;
        private string plantingSeedId;

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
        private bool IsAnyMenuOpen => buildPanelOpen || inGameMenuOpen || escMenuOpen || containerStorageOpen;

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

        private struct TimerDisplayEntry
        {
            public string name;
            public float progress;
            public string timeLabel;
            public string distanceLabel;
        }

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

                margin = Mathf.Clamp(Mathf.Min(width, height) * 0.018f, 10f, 28f);
                gutter = Mathf.Clamp(margin * 0.6f, 6f, 16f);

                float playerWidth = Mathf.Clamp(width * 0.17f, 190f, 340f);
                float playerHeight = Mathf.Clamp(height * 0.13f, 88f, 148f);
                playerPanelRect = new Rect(margin, margin, playerWidth, playerHeight);

                float helpWidth = Mathf.Clamp(width * 0.18f, 210f, 360f);
                float helpHeight = Mathf.Clamp(height * 0.095f, 72f, 118f);
                helpPanelRect = new Rect(margin, playerPanelRect.yMax + gutter, helpWidth, helpHeight);

                float modeWidth = Mathf.Clamp(width * 0.20f, 220f, 380f);
                float modeHeight = Mathf.Clamp(height * 0.11f, 84f, 132f);

                float hotbarSpacing = Mathf.Clamp(width * 0.005f, 4f, 10f);
                hotbarWidth = Mathf.Min(width - (margin * 2f), 1200f);
                slotWidth = Mathf.Clamp((hotbarWidth - (hotbarSpacing * (HotbarSlotCount - 1))) / HotbarSlotCount, 48f, 96f);
                slotHeight = Mathf.Clamp(height * 0.072f, 58f, 96f);
                resourceBadgeHeight = Mathf.Clamp(height * 0.032f, 24f, 36f);
                resourceBadgeWidth = Mathf.Max(78f, (hotbarWidth - (hotbarSpacing * 4f)) / 5f);
                float hotbarHeight = resourceBadgeHeight + gutter + slotHeight + 14f;
                hotbarRect = new Rect((width - hotbarWidth) * 0.5f, height - hotbarHeight - margin, hotbarWidth, hotbarHeight);

                float contextWidth = Mathf.Min(width - (margin * 2f), hotbarWidth * 0.85f);
                float contextHeight = Mathf.Clamp(height * 0.075f, 52f, 88f);
                contextRect = new Rect((width - contextWidth) * 0.5f, hotbarRect.y - contextHeight - gutter, contextWidth, contextHeight);

                float statusWidth = Mathf.Min(width - (margin * 2f), hotbarWidth * 0.7f);
                float statusHeight = Mathf.Clamp(height * 0.045f, 34f, 52f);
                statusRect = new Rect((width - statusWidth) * 0.5f, contextRect.y - statusHeight - gutter, statusWidth, statusHeight);

                float reservedBottom = (height - contextRect.y) + margin;
                float buildWidth = Mathf.Clamp(width * 0.24f, 310f, 520f);
                buildWidth = Mathf.Min(buildWidth, width - (margin * 2f));
                float availableHeight = height - reservedBottom - margin;
                float buildHeight = Mathf.Clamp(availableHeight, 260f, 780f);
                buildPanelRect = new Rect(width - buildWidth - margin, margin, buildWidth, buildHeight);

                float centeredModeX = (width - modeWidth) * 0.5f;
                float maxModeX = buildPanelRect.x - gutter - modeWidth;
                float minModeX = playerPanelRect.xMax + gutter;
                float resolvedModeX = Mathf.Clamp(centeredModeX, minModeX, Mathf.Max(minModeX, maxModeX));
                modePanelRect = new Rect(resolvedModeX, margin, modeWidth, modeHeight);

                buildItemHeight = Mathf.Clamp(height * 0.055f, 44f, 62f);
            }
        }

        private sealed class ContainerRuntimeStorage
        {
            public int wood;
            public int stone;
            public int food;
            public readonly Dictionary<string, int> storedFoodItems = new(StringComparer.Ordinal);
            public readonly Dictionary<string, int> storedWeapons = new(StringComparer.Ordinal);
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
            InitializeCraftingRecipes();
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
            UpdateWeaponAnimation();
            HandleGlobalShortcuts();

            if (escMenuOpen)
                return;

            if (containerStorageOpen)
            {
                HandleContainerStorageNavigation();
                return;
            }

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
            if (escMenuOpen)
            {
                DrawHud();
                DrawHotbarHud();
                DrawEscMenu();
            }
            else if (containerStorageOpen)
            {
                DrawContainerStoragePanel();
            }
            else if (inGameMenuOpen)
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

            cachedRightHandBone = null;
            cachedLeftHandBone = null;
            playerArmorMount = null;
            playerWeaponMount = null;
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

        private void UpdateCursorMode()
        {
            if (followCamera != null)
            {
                followCamera.SetUiCursorMode(pointerMode || inGameMenuOpen || escMenuOpen);
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

            if (currentEvent.keyCode == KeyCode.Escape)
            {
                if (inGameMenuOpen)
                {
                    pendingInGameMenuClose = true;
                    currentEvent.Use();
                    return;
                }

                if (buildPanelOpen)
                {
                    pendingMenuClose = true;
                    currentEvent.Use();
                    return;
                }

                if (escMenuOpen)
                {
                    pendingEscMenuClose = true;
                    currentEvent.Use();
                    return;
                }

                // No menu open — open the esc menu
                pendingEscMenuOpen = true;
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
                renewableNodeState = renewableNode != null ? renewableNode.CaptureState() : null,
                containerStorage = CaptureContainerStorage(placedObject)
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

        private void HandleInteract()
        {
            if (targetedPickup != null)
            {
                CollectPickup(targetedPickup);
                return;
            }

            if (targetedPlacedObject != null && TryOpenContainerStorage(targetedPlacedObject))
            {
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

            // ── Resolve catalog item: try pickup ID first, then prefab name fallback ──
            BuildCatalogItem pickupItem = null;

            if (!string.IsNullOrWhiteSpace(pickup.ItemId) &&
                catalogLookup.TryGetValue(pickup.ItemId, out BuildCatalogItem resolvedById))
            {
                pickupItem = resolvedById;
            }
            else
            {
                // Fallback: match by prefab/gameObject name (handles scene-placed objects
                // where InventoryPickup.itemId was never set)
                string objectName = pickup.gameObject.name;
                pickupItem = catalogLookup.Values.FirstOrDefault(
                    candidate => string.Equals(candidate.prefabName, objectName, StringComparison.OrdinalIgnoreCase));
            }

            // Canonical ID: always use the catalog item's id when available
            string canonicalId = pickupItem != null ? pickupItem.id : pickup.ItemId;

            if (string.IsNullOrWhiteSpace(canonicalId))
            {
                SetStatusMessage($"Cannot collect {pickup.DisplayName} — unknown item.");
                return;
            }

            EquipmentInfo eqInfo = pickupItem != null ? GetEquipmentInfo(pickupItem) : null;

            string displayName = pickupItem != null ? pickupItem.displayName : pickup.DisplayName;

            switch (pickup.InventoryType)
            {
                case PickupInventoryType.Food:
                    AddInventoryCount(storedFoodInventory, canonicalId, 1);
                    SetStatusMessage($"Stored {displayName}.");
                    break;

                case PickupInventoryType.Weapon:
                    AddInventoryCount(storedWeaponInventory, canonicalId, 1);

                    if (eqInfo != null && eqInfo.isAmmo)
                        SetStatusMessage($"Collected ammo: {displayName}.");
                    else
                        SetStatusMessage($"Collected {displayName}.");

                    break;

                default:
                    // PickupInventoryType.None — auto-detect from catalog inference
                    if (eqInfo != null && (eqInfo.isWeapon || eqInfo.isArmor || eqInfo.isAmmo))
                    {
                        AddInventoryCount(storedWeaponInventory, canonicalId, 1);
                        SetStatusMessage($"Collected {displayName}.");
                    }
                    else
                    {
                        SetStatusMessage($"Picked up {displayName}.");
                    }
                    break;
            }

            // ── Remove from world ─────────────────────────────────────
            PlacedWorldObject placedWorldObject = pickup.GetComponent<PlacedWorldObject>();
            if (placedWorldObject != null)
            {
                placedObjects.Remove(placedWorldObject.UniqueId);
            }

            // Always destroy the pickup gameObject
            Destroy(pickup.gameObject);

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

        private WeaponMount EnsureWeaponMount()
        {
            if (playerWeaponMount != null)
                return playerWeaponMount;

            if (playerTransform == null)
                return null;

            playerWeaponMount = playerTransform.GetComponent<WeaponMount>();
            if (playerWeaponMount == null)
                playerWeaponMount = playerTransform.gameObject.AddComponent<WeaponMount>();

            return playerWeaponMount;
        }

        private ArmorMount EnsureArmorMount()
        {
            if (playerArmorMount != null)
                return playerArmorMount;

            if (playerTransform == null)
                return null;

            playerArmorMount = playerTransform.GetComponent<ArmorMount>();
            if (playerArmorMount == null)
                playerArmorMount = playerTransform.gameObject.AddComponent<ArmorMount>();

            return playerArmorMount;
        }

        private void EnsureHandBoneCache()
        {
            if (playerTransform == null)
                return;

            WeaponMount mount = EnsureWeaponMount();
            if (mount == null)
                return;

            if (cachedRightHandBone == null)
                cachedRightHandBone = mount.FindBone(HumanoidBoneNames.RightHand);

            if (cachedLeftHandBone == null)
                cachedLeftHandBone = mount.FindBone(HumanoidBoneNames.LeftHand);
        }

        private void TrySwingWeapon()
        {
            if (Time.unscaledTime < weaponSwingCooldownUntil)
                return;

            EquippedSlot weaponSlot = equippedSlots.TryGetValue(
                EquipSlotKey.Weapon, out EquippedSlot ws) ? ws : null;

            if (weaponSlot == null || weaponSlot.IsEmpty)
                return;

            float cooldown = weaponSlot.stats?.attackCooldown ?? 0.55f;
            weaponSwingCooldownUntil = Time.unscaledTime + cooldown;

            isSwinging = true;
            swingProgress = 0f;

            SetStatusMessage($"Swung {GetDisplayNameForItemId(weaponSlot.catalogItemId)}!" +
                (GetWeaponDamageBonus() > 0 ? $" (+{GetWeaponDamageBonus()} dmg)" : ""));
        }

        private void TryRaiseShield(bool raise)
        {
            EquippedSlot shieldSlot = equippedSlots.TryGetValue(
                EquipSlotKey.Shield, out EquippedSlot ss) ? ss : null;

            bool hasShield = shieldSlot != null && !shieldSlot.IsEmpty &&
                             shieldSlot.weaponType == WeaponType.Shield;

            bool hasWeapon = equippedSlots.TryGetValue(
                EquipSlotKey.Weapon, out EquippedSlot wSlot) && !wSlot.IsEmpty;

            if (!hasShield && !hasWeapon)
                return;

            isRaising = raise;
            raiseProgress = raise ? 0f : raiseProgress;
        }

        private void UpdateWeaponAnimation()
        {
            EnsureHandBoneCache();

            UpdateSwingAnimation();
            UpdateRaiseAnimation();
        }

        private void UpdateSwingAnimation()
        {
            if (!isSwinging)
                return;

            float duration = 0.28f;
            swingProgress += Time.deltaTime / duration;

            if (cachedRightHandBone != null)
            {
                Vector3 target;
                if (swingProgress < 0.4f)
                {
                    float t = swingProgress / 0.4f;
                    target = Vector3.Lerp(RestAngle, SwingPeakAngle, EaseInOut(t));
                }
                else if (swingProgress < 0.7f)
                {
                    float t = (swingProgress - 0.4f) / 0.3f;
                    target = Vector3.Lerp(SwingPeakAngle, SwingEndAngle, EaseIn(t));
                }
                else
                {
                    float t = (swingProgress - 0.7f) / 0.3f;
                    target = Vector3.Lerp(SwingEndAngle, RestAngle, EaseInOut(t));
                }

                cachedRightHandBone.localEulerAngles = target;
            }

            if (swingProgress >= 1f)
            {
                isSwinging = false;
                swingProgress = 0f;

                if (cachedRightHandBone != null)
                    cachedRightHandBone.localEulerAngles = RestAngle;
            }
        }

        private void UpdateRaiseAnimation()
        {
            if (cachedRightHandBone != null && !isSwinging)
            {
                float duration = 0.18f;

                if (isRaising)
                {
                    raiseProgress = Mathf.MoveTowards(
                        raiseProgress, 1f, Time.deltaTime / duration);

                    cachedRightHandBone.localEulerAngles = Vector3.Lerp(
                        RestAngle, RaiseAngle, EaseInOut(raiseProgress));
                }
                else if (raiseProgress > 0f)
                {
                    raiseProgress = Mathf.MoveTowards(
                        raiseProgress, 0f, Time.deltaTime / duration);

                    cachedRightHandBone.localEulerAngles = Vector3.Lerp(
                        RestAngle, RaiseAngle, EaseInOut(raiseProgress));
                }
            }

            if (cachedLeftHandBone != null)
            {
                float duration = 0.18f;
                Vector3 shieldRaise = new Vector3(-50f, 20f, 0f);

                if (isRaising)
                {
                    cachedLeftHandBone.localEulerAngles = Vector3.Lerp(
                        cachedLeftHandBone.localEulerAngles,
                        shieldRaise,
                        Time.deltaTime / duration * 8f);
                }
                else
                {
                    cachedLeftHandBone.localEulerAngles = Vector3.Lerp(
                        cachedLeftHandBone.localEulerAngles,
                        RestAngle,
                        Time.deltaTime / duration * 8f);
                }
            }
        }

        private static float EaseInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        private EquipmentInfo GetEquipmentInfo(BuildCatalogItem item)
        {
            if (item == null)
                return null;

            if (item.prefab != null)
            {
                EquipmentDefinition def = item.prefab.GetComponent<EquipmentDefinition>();
                if (def != null)
                    return EquipmentInfo.FromDefinition(def);
            }

            return EquipmentInfo.InferFromCatalog(
                item.id, (int)item.category, (int)item.pickupInventoryType);
        }

        private string ResolveEquipSlot(EquipmentInfo def)
        {
            if (def == null)
                return string.Empty;

            if (def.isAmmo)
                return EquipSlotKey.Ammo;

            if (def.isArmor)
                return EquipSlotKey.FromArmorSlot(def.armorSlot);

            if (def.isWeapon)
            {
                switch (def.weaponType)
                {
                    case WeaponType.Shield:
                        return EquipSlotKey.Shield;

                    case WeaponType.Sword:
                    case WeaponType.Dagger:
                    case WeaponType.Axe:
                    case WeaponType.Hammer:
                    case WeaponType.Warhammer:
                    case WeaponType.Spear:
                    case WeaponType.Bow:
                        return EquipSlotKey.Weapon;
                }
            }

            return string.Empty;
        }

        private void UnequipSlot(string slotKey)
        {
            if (!equippedSlots.TryGetValue(slotKey, out EquippedSlot slot) || slot.IsEmpty)
                return;

            string catalogItemId = slot.catalogItemId;

            DismountEquipmentFromPlayer(slotKey);

            AddInventoryCount(storedWeaponInventory, catalogItemId, 1);

            equippedSlots[slotKey] = new EquippedSlot();

            if (catalogLookup.TryGetValue(catalogItemId, out BuildCatalogItem item))
                SetStatusMessage($"Unequipped {item.displayName}.");

            SaveWorld();
        }

        private void UnequipAll()
        {
            foreach (string key in EquipSlotKey.All)
                UnequipSlot(key);
        }

        private void ToggleEquip(string catalogItemId)
        {
            if (string.IsNullOrWhiteSpace(catalogItemId))
                return;

            foreach (KeyValuePair<string, EquippedSlot> pair in equippedSlots)
            {
                if (string.Equals(pair.Value.catalogItemId, catalogItemId, StringComparison.Ordinal))
                {
                    UnequipSlot(pair.Key);
                    return;
                }
            }

            TryEquipItem(catalogItemId);
        }

        private bool TryEquipItem(string catalogItemId)
        {
            if (string.IsNullOrWhiteSpace(catalogItemId))
                return false;

            if (!catalogLookup.TryGetValue(catalogItemId, out BuildCatalogItem item))
                return false;

            EquipmentInfo eqInfo = GetEquipmentInfo(item);

            if (eqInfo == null || (!eqInfo.isWeapon && !eqInfo.isArmor && !eqInfo.isAmmo))
            {
                SetStatusMessage($"{item.displayName} is not equippable.");
                return false;
            }

            string slotKey = string.Empty;

            if (eqInfo.isAmmo)
            {
                slotKey = EquipSlotKey.Ammo;
            }
            else if (eqInfo.isArmor)
            {
                slotKey = EquipSlotKey.FromArmorSlot(eqInfo.armorSlot);
            }
            else if (eqInfo.isWeapon)
            {
                slotKey = eqInfo.weaponType == WeaponType.Shield
                    ? EquipSlotKey.Shield
                    : EquipSlotKey.Weapon;
            }

            if (string.IsNullOrWhiteSpace(slotKey))
            {
                SetStatusMessage($"No equip slot found for {item.displayName}.");
                return false;
            }

            if (!equippedSlots.ContainsKey(slotKey))
                equippedSlots[slotKey] = new EquippedSlot();

            if (!equippedSlots[slotKey].IsEmpty)
                UnequipSlot(slotKey);

            RemoveInventoryCount(storedWeaponInventory, catalogItemId, 1);

            EquippedSlot slot = new EquippedSlot
            {
                catalogItemId = catalogItemId,
                weaponType = eqInfo.weaponType,
                armorSlot = eqInfo.armorSlot,
                ammoType = eqInfo.ammoType,
                stats = eqInfo.stats ?? new EquipmentStats()
            };

            equippedSlots[slotKey] = slot;

            MountEquipmentOnPlayer(slotKey, item, eqInfo);

            SetStatusMessage($"Equipped {item.displayName}.");
            SaveWorld();
            return true;
        }

        private bool IsItemEquipped(string catalogItemId)
        {
            foreach (EquippedSlot slot in equippedSlots.Values)
            {
                if (string.Equals(slot.catalogItemId, catalogItemId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private string GetEquippedSlotKey(string catalogItemId)
        {
            foreach (KeyValuePair<string, EquippedSlot> pair in equippedSlots)
            {
                if (string.Equals(pair.Value.catalogItemId, catalogItemId, StringComparison.Ordinal))
                    return pair.Key;
            }
            return string.Empty;
        }

        private float GetTotalDamageReduction()
        {
            float reduction = 0f;
            foreach (string key in new[] { EquipSlotKey.Head, EquipSlotKey.Chest, EquipSlotKey.Hands, EquipSlotKey.Feet })
            {
                if (equippedSlots.TryGetValue(key, out EquippedSlot slot) && !slot.IsEmpty)
                    reduction += slot.stats.damageReduction;
            }

            if (isRaising && equippedSlots.TryGetValue(EquipSlotKey.Shield, out EquippedSlot shieldSlot) && !shieldSlot.IsEmpty)
                reduction += shieldSlot.stats.damageReduction;

            return Mathf.Clamp01(reduction);
        }

        private int GetWeaponDamageBonus()
        {
            if (equippedSlots.TryGetValue(EquipSlotKey.Weapon, out EquippedSlot slot) && !slot.IsEmpty)
                return Mathf.Max(0, slot.stats.damageBonus);
            return 0;
        }

        private float GetEquipmentSpeedMultiplier()
        {
            float multiplier = 1f;
            foreach (EquippedSlot slot in equippedSlots.Values)
            {
                if (!slot.IsEmpty)
                    multiplier *= slot.stats.moveSpeedMultiplier;
            }
            return Mathf.Clamp(multiplier, 0.4f, 1.5f);
        }

        private void ApplyDamage(int rawDamage)
        {
            float reduction = GetTotalDamageReduction();
            int reducedDamage = Mathf.Max(1, Mathf.RoundToInt(rawDamage * (1f - reduction)));
            int previousHealth = health;

            health = Mathf.Clamp(health - reducedDamage, 0, MaxHealth);
            if (health == previousHealth)
                return;

            string reductionNote = reduction > 0f
                ? $" ({Mathf.RoundToInt(reduction * 100f)}% blocked)"
                : string.Empty;

            SetStatusMessage($"Took {reducedDamage} damage{reductionNote}.");
            SaveWorld();
        }

        private void MountEquipmentOnPlayer(
            string slotKey,
            BuildCatalogItem item,
            EquipmentInfo def)
        {
            if (item?.prefab == null || def == null)
                return;

            bool isArmorSlot = slotKey == EquipSlotKey.Head
                            || slotKey == EquipSlotKey.Chest
                            || slotKey == EquipSlotKey.Hands
                            || slotKey == EquipSlotKey.Feet;

            if (isArmorSlot)
            {
                // ── Armor path ────────────────────────────────────────
                ArmorMount armorMount = EnsureArmorMount();
                if (armorMount == null)
                    return;

                string boneName = string.IsNullOrWhiteSpace(def.overrideBoneName)
                    ? HumanoidBoneNames.ForSlotKey(slotKey)
                    : def.overrideBoneName;

                Vector3 scaleMultiplier = def.mountScaleMultiplier == Vector3.zero
                    ? Vector3.one
                    : def.mountScaleMultiplier;

                GameObject instance = armorMount.MountArmor(
                    slotKey,
                    boneName,
                    item.prefab,
                    def.mountPositionOffset,
                    def.mountRotationOffset,
                    scaleMultiplier);

                if (instance == null)
                    return;

                if (equippedSlots.TryGetValue(slotKey, out EquippedSlot armorSlotData))
                    armorSlotData.mountedObject = instance;
            }
            else
            {
                // ── Weapon / shield / ammo path ──────────────────────
                WeaponMount weaponMount = EnsureWeaponMount();
                if (weaponMount == null)
                    return;

                string boneName = string.IsNullOrWhiteSpace(def.overrideBoneName)
                    ? HumanoidBoneNames.ForSlotKey(slotKey)
                    : def.overrideBoneName;

                // Base mount pose comes from current rig palm + grip alignment.
                // Per-item EquipmentDefinition offsets are fine-tuning additions
                // on top of that dynamic base (instead of replacing it).
                Vector3 posOff = def.mountPositionOffset;
                Vector3 rotOff = def.mountRotationOffset;

                if (slotKey == EquipSlotKey.Weapon || slotKey == EquipSlotKey.Shield)
                {
                    weaponMount.GetPalmOffsetAndRotation(
                        boneName,
                        out Vector3 palmPosOff,
                        out Vector3 palmRotOff);

                    if (palmPosOff == Vector3.zero)
                        palmPosOff = slotKey == EquipSlotKey.Weapon
                            ? EquipmentInfo.FallbackRightHandPosition
                            : EquipmentInfo.FallbackLeftHandPosition;

                    if (palmRotOff == Vector3.zero)
                        palmRotOff = slotKey == EquipSlotKey.Weapon
                            ? EquipmentInfo.FallbackRightHandRotation
                            : EquipmentInfo.FallbackLeftHandRotation;

                    posOff = palmPosOff + def.mountPositionOffset;
                    rotOff = palmRotOff + def.mountRotationOffset;
                }

                GameObject instance = weaponMount.MountOnBone(
                    boneName,
                    item.prefab,
                    posOff,
                    rotOff);

                if (instance == null)
                    return;

                if (equippedSlots.TryGetValue(slotKey, out EquippedSlot weaponSlotData))
                    weaponSlotData.mountedObject = instance;
            }
        }

        private void DismountEquipmentFromPlayer(string slotKey)
        {
            if (!equippedSlots.TryGetValue(slotKey, out EquippedSlot slot))
                return;

            bool isArmorSlot = slotKey == EquipSlotKey.Head
                            || slotKey == EquipSlotKey.Chest
                            || slotKey == EquipSlotKey.Hands
                            || slotKey == EquipSlotKey.Feet;

            if (isArmorSlot)
            {
                ArmorMount armorMount = EnsureArmorMount();
                armorMount?.DismountArmor(slotKey);
            }
            else
            {
                WeaponMount weaponMount = EnsureWeaponMount();
                if (slot.mountedObject != null)
                    weaponMount?.Dismount(slot.mountedObject);
            }

            slot.mountedObject = null;
        }

        private void HandleGlobalShortcuts()
        {
            bool inGameMenuTogglePressed = ConsumePendingShortcut(ref pendingInGameMenuToggle) ||
                                           (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);

            if (inGameMenuTogglePressed)
            {
                if (containerStorageOpen)
                {
                    CloseContainerStorage();
                    return;
                }

                if (inGameMenuOpen)
                {
                    CloseInGameMenu();
                }
                else
                {
                    if (escMenuOpen)
                        CloseEscMenu();
                    OpenInGameMenu();
                }
            }

            if (inGameMenuOpen && (ConsumePendingShortcut(ref pendingInGameMenuClose) ||
                                   (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)))
            {
                CloseInGameMenu();
                return;
            }

            // ESC menu: close if open, open if nothing else is open
            bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (containerStorageOpen && escPressed)
            {
                CloseContainerStorage();
                return;
            }

            if (escMenuOpen && (ConsumePendingShortcut(ref pendingEscMenuClose) || escPressed))
            {
                CloseEscMenu();
                return;
            }

            if (!IsAnyMenuOpen && !placementActive && !deleteMode &&
                (ConsumePendingShortcut(ref pendingEscMenuOpen) || escPressed))
            {
                OpenEscMenu();
                return;
            }

            // Consume stale pending esc shortcuts that didn't apply
            ConsumePendingShortcut(ref pendingEscMenuOpen);
            ConsumePendingShortcut(ref pendingEscMenuClose);

            if (escMenuOpen)
                return;

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
                // Context-sensitive X key:
                // 1. If in inventory/journal on the Inventory tab → drop selected item (handled in HandleInGameMenuNavigation)
                // 2. If in placement/delete mode and looking at a placed object → delete that object directly
                // 3. Otherwise → toggle delete mode
                if (inGameMenuOpen || containerStorageOpen || escMenuOpen)
                {
                    // Let HandleInGameMenuNavigation handle X in journal context
                }
                else if ((placementActive || deleteMode) && targetedPlacedObject != null)
                {
                    TryDeleteTarget();
                }
                else
                {
                    deleteMode = !deleteMode;
                    if (deleteMode)
                    {
                        CloseBuildPanel();
                        placementActive = false;
                        CleanupGhost();
                    }
                }
            }

            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                ConsumeStoredFood();
            }

            if (!placementActive && !buildPanelOpen && !inGameMenuOpen && !escMenuOpen
                && Keyboard.current.pKey.wasPressedThisFrame)
            {
                TryBeginPlantingFirstSeed();
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

            if (!placementActive && !deleteMode && !buildPanelOpen && !inGameMenuOpen)
            {
                if (TryGetAttackPressedThisFrame())
                    TrySwingWeapon();

                if (Mouse.current != null)
                {
                    bool rightHeld = Mouse.current.rightButton.isPressed;

                    if (!placementActive)
                        TryRaiseShield(rightHeld);
                }
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

            // If the hotbar item is a seed, enter planting mode instead of build placement
            if (IsSeedItem(item.id))
            {
                BeginPlantingSeed(item.id);
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
                if (plantingMode)
                    TryPlantSeed();
                else
                    TryPlaceCurrentSelection();
            }

            if (!pointerMode && TryGetSubmitPressedThisFrame())
            {
                if (plantingMode)
                    TryPlantSeed();
                else
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

            // X key also deletes the targeted object directly (handled by HandleGlobalShortcuts,
            // but LMB/attack is the primary path here for consistency)
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
            containerStorageByObjectId.Remove(targetedPlacedObject.UniqueId);
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

            bool canContinue = devBuildMode || CanAfford(item.cost);
            if (canContinue)
            {
                CleanupGhost();
                EnsureGhost(item);
            }
            else
            {
                placementActive = false;
                CleanupGhost();
            }
        }

        private void CancelPlacement()
        {
            placementActive = false;
            plantingMode = false;
            plantingSeedId = null;
            CleanupGhost();
            pointerMode = false;
        }

        // ══════════════════════════════════════════════════════════════
        //  Seed Planting
        // ══════════════════════════════════════════════════════════════

        private void BeginPlantingSeed(string seedId)
        {
            if (!seedToCropMap.TryGetValue(seedId, out string cropId))
                return;
            if (!catalogLookup.TryGetValue(cropId, out BuildCatalogItem cropItem))
                return;

            // Check player has seeds
            if (!storedFoodInventory.TryGetValue(seedId, out int seedCount) || seedCount <= 0)
            {
                SetStatusMessage("No seeds remaining.");
                return;
            }

            // Enter placement mode with the crop as ghost preview
            plantingMode = true;
            plantingSeedId = seedId;
            placementActive = true;
            deleteMode = false;
            buildPanelOpen = false;
            pointerMode = false;
            placementYaw = 0f;

            // Use crop item for the ghost
            CleanupGhost();
            EnsureGhost(cropItem);
            SetStatusMessage($"Select a spot to plant {GetDisplayNameForItemId(seedId)}.");
        }

        private void TryPlantSeed()
        {
            if (!plantingMode || string.IsNullOrWhiteSpace(plantingSeedId))
                return;

            if (!seedToCropMap.TryGetValue(plantingSeedId, out string cropId))
            {
                SetStatusMessage("Unknown seed type.");
                CancelPlacement();
                return;
            }

            if (!catalogLookup.TryGetValue(cropId, out BuildCatalogItem cropItem))
            {
                SetStatusMessage("Crop not found in catalog.");
                CancelPlacement();
                return;
            }

            if (!placementHasValidTarget)
            {
                SetStatusMessage("Move the placement farther from the player.");
                return;
            }

            // Consume 1 seed
            if (!storedFoodInventory.TryGetValue(plantingSeedId, out int seedCount) || seedCount <= 0)
            {
                SetStatusMessage("No seeds remaining.");
                CancelPlacement();
                return;
            }

            RemoveInventoryCount(storedFoodInventory, plantingSeedId, 1);

            // Spawn the crop at growth 0 — it will grow over time via RenewableNode.Update()
            GameObject planted = SpawnCatalogItem(
                cropItem,
                Guid.NewGuid().ToString("N"),
                pendingPlacementPosition,
                pendingPlacementRotation,
                0f, // seedling — growth starts at zero
                registerForSave: true,
                placedByPlayer: true,
                alignToGround: true);

            if (planted == null)
            {
                // Refund seed on failure
                AddInventoryCount(storedFoodInventory, plantingSeedId, 1);
                SetStatusMessage("Failed to plant seed.");
                CancelPlacement();
                return;
            }

            // Enable growth label so the player can see progress
            RenewableNode node = planted.GetComponent<RenewableNode>();
            if (node != null)
            {
                // showGrowthLabel is already set from the catalog item, but ensure it's on for planted crops
                // (The catalog entry for TomatoPlant/Cabbage may or may not have it enabled)
            }

            SaveWorld();

            string seedName = GetDisplayNameForItemId(plantingSeedId);
            SetStatusMessage($"Planted {seedName}! It will take about {Mathf.RoundToInt(cropItem.renewableRegrowSeconds)}s to grow.");

            // Check if player has more seeds to continue planting
            storedFoodInventory.TryGetValue(plantingSeedId, out int remaining);
            if (remaining > 0)
            {
                CleanupGhost();
                EnsureGhost(cropItem);
            }
            else
            {
                SetStatusMessage($"Planted {seedName}! No more seeds of this type.");
                CancelPlacement();
            }
        }

        private void TryBeginPlantingFirstSeed()
        {
            // Find the first seed the player has in inventory
            foreach (KeyValuePair<string, string> pair in seedToCropMap)
            {
                if (storedFoodInventory.TryGetValue(pair.Key, out int count) && count > 0)
                {
                    BeginPlantingSeed(pair.Key);
                    return;
                }
            }

            SetStatusMessage("No seeds in inventory. Craft seeds from food at the crafting panel.");
        }

        private void EnsureGhost(BuildCatalogItem item)
        {
            if (item == null || item.prefab == null)
            {
                CleanupGhost();
                return;
            }

            if (ghostInstance != null && string.Equals(activeGhostCatalogId, item.id, StringComparison.Ordinal))
            {
                return;
            }

            CleanupGhost();

            ghostInstance = Instantiate(item.prefab);
            if (ghostInstance == null)
            {
                activeGhostCatalogId = string.Empty;
                return;
            }

            ghostInstance.name = $"Ghost_{item.prefab.name}";
            activeGhostCatalogId = item.id;
            PrepareGhostForPreview();
            UpdateGhostTint(true);
        }

        private void PrepareGhostForPreview()
        {
            if (ghostInstance == null)
            {
                return;
            }

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
            if (ghostRenderers == null)
            {
                ghostRenderers = Array.Empty<Renderer>();
            }

            foreach (Renderer rendererComponent in ghostRenderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

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
            buildListScroll = Vector2.zero;

            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            selectedIndex = items.Count > 0 ? 0 : -1;
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
            if (!itemsByCategory.TryGetValue(category, out List<BuildCatalogItem> items) || items == null || items.Count == 0)
            {
                return;
            }

            if (index < 0 || index >= items.Count)
            {
                return;
            }

            selectedCategory = category;
            selectedIndex = index;

            EnsureSelectionInRange();
            CenterBuildScrollOnSelection();

            placementActive = true;
            deleteMode = false;
            buildPanelOpen = false;
            pointerMode = false;
            placementYaw = 0f;

            BuildCatalogItem selectedItem = GetSelectedItem();
            if (selectedItem != null)
            {
                EnsureGhost(selectedItem);
            }
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

        private void CycleSelection(int delta)
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            int previousIndex = selectedIndex;
            selectedIndex = (selectedIndex + delta) % items.Count;
            if (selectedIndex < 0)
            {
                selectedIndex += items.Count;
            }

            if (selectedIndex == previousIndex)
            {
                return;
            }

            CenterBuildScrollOnSelection();

            if (placementActive)
            {
                EnsureGhost(GetSelectedItem());
            }
        }

        private void CenterBuildScrollOnSelection()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items == null || items.Count == 0)
            {
                buildListScroll.y = 0f;
                return;
            }

            UiLayout layout = GetUiLayout();
            float rowHeight = layout.buildItemHeight;
            if (rowHeight <= 0f)
            {
                return;
            }

            int index = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            float rowTop = index * rowHeight;
            float rowBottom = rowTop + rowHeight;

            Rect panelRect = layout.buildPanelRect;
            float previewHeight = Mathf.Clamp(panelRect.width * 0.44f, 140f, 260f);
            float detailsHeight = Mathf.Clamp(90f * currentUiScale, 78f, 124f);
            float footerHeight = Mathf.Clamp(50f * currentUiScale, 42f, 64f);
            float viewportHeight = Mathf.Max(
                130f,
                panelRect.height - previewHeight - detailsHeight - footerHeight - Mathf.Clamp(96f * currentUiScale, 80f, 120f));

            float totalContent = items.Count * rowHeight;
            float maxScroll = Mathf.Max(0f, totalContent - viewportHeight);

            if (rowTop >= buildListScroll.y && rowBottom <= buildListScroll.y + viewportHeight)
            {
                return;
            }

            float centered = rowTop - (viewportHeight - rowHeight) * 0.5f;
            buildListScroll.y = Mathf.Clamp(centered, 0f, maxScroll);
        }

        private void EnsureSelectionInRange()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items == null || items.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                return;
            }

            if (!string.IsNullOrEmpty(activeGhostCatalogId))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (string.Equals(items[i].id, activeGhostCatalogId, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                        return;
                    }
                }
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
        }

        private BuildCatalogItem GetSelectedItem()
        {
            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            if (items == null || items.Count == 0)
            {
                return null;
            }

            if (selectedIndex < 0 || selectedIndex >= items.Count)
            {
                return null;
            }

            return items[selectedIndex];
        }

        private BuildCatalogItem GetSelectedHotbarItem()
        {
            if (selectedHotbarIndex < 0 || selectedHotbarIndex >= favoriteHotbarItemIds.Length)
            {
                return null;
            }

            string itemId = favoriteHotbarItemIds[selectedHotbarIndex];
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return catalogLookup.TryGetValue(itemId, out BuildCatalogItem item) ? item : null;
        }

        private List<BuildCatalogItem> GetItemsForSelectedCategory()
        {
            if (itemsByCategory == null)
            {
                return new List<BuildCatalogItem>();
            }

            if (!itemsByCategory.TryGetValue(selectedCategory, out List<BuildCatalogItem> items) || items == null)
            {
                return new List<BuildCatalogItem>();
            }

            return items.Where(item => item != null).ToList();
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

        // ══════════════════════════════════════════════════════════════
        //  World Container Storage (Barrel / Box only)
        // ══════════════════════════════════════════════════════════════

        private bool TryOpenContainerStorage(PlacedWorldObject placedObject)
        {
            if (placedObject == null || !TryGetContainerDefinition(placedObject, out _, out _))
            {
                return false;
            }

            activeContainerObject = placedObject;
            containerStorageOpen = true;
            pointerMode = true;
            containerTransferAmount = 1;

            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
            {
                playerMovement.InputSuppressed = true;
            }

            SetStatusMessage($"Opened {GetContainerDisplayName(placedObject)}.");
            return true;
        }

        private void CloseContainerStorage()
        {
            containerStorageOpen = false;
            activeContainerObject = null;
            pointerMode = false;

            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
            {
                playerMovement.InputSuppressed = false;
            }
        }

        private bool TryGetActiveContainer(out PlacedWorldObject placedObject, out ContainerRuntimeStorage storage, out int capacity)
        {
            placedObject = activeContainerObject;
            storage = null;
            capacity = 0;

            if (!containerStorageOpen || placedObject == null || string.IsNullOrWhiteSpace(placedObject.UniqueId))
            {
                return false;
            }

            if (!TryGetContainerDefinition(placedObject, out _, out capacity))
            {
                return false;
            }

            storage = GetOrCreateContainerStorage(placedObject.UniqueId);
            return storage != null;
        }

        private bool TryGetContainerDefinition(PlacedWorldObject placedObject, out string label, out int capacity)
        {
            label = string.Empty;
            capacity = 0;

            if (placedObject == null)
            {
                return false;
            }

            string catalogId = placedObject.CatalogItemId ?? string.Empty;
            string prefabName = string.Empty;
            if (catalogLookup.TryGetValue(catalogId, out BuildCatalogItem catalogItem))
            {
                prefabName = catalogItem.prefabName ?? string.Empty;
            }

            if (string.Equals(catalogId, "Town:Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prefabName, "Barrel_01_LITE", StringComparison.OrdinalIgnoreCase))
            {
                label = "Barrel";
                capacity = BarrelStorageCapacity;
                return true;
            }

            if (string.Equals(catalogId, "Farm:Box_01", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prefabName, "Box_01", StringComparison.OrdinalIgnoreCase))
            {
                label = "Box";
                capacity = BoxStorageCapacity;
                return true;
            }

            return false;
        }

        private bool IsContainerCatalogItem(BuildCatalogItem item)
        {
            if (item == null)
            {
                return false;
            }

            return string.Equals(item.id, "Town:Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.prefabName, "Barrel_01_LITE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.id, "Farm:Box_01", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.prefabName, "Box_01", StringComparison.OrdinalIgnoreCase);
        }

        private string GetContainerDisplayName(PlacedWorldObject placedObject)
        {
            return TryGetContainerDefinition(placedObject, out string label, out _) ? label : "Container";
        }

        private ContainerRuntimeStorage GetOrCreateContainerStorage(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                return null;
            }

            if (!containerStorageByObjectId.TryGetValue(uniqueId, out ContainerRuntimeStorage storage) || storage == null)
            {
                storage = new ContainerRuntimeStorage();
                containerStorageByObjectId[uniqueId] = storage;
            }

            return storage;
        }

        private int GetContainerUsedCapacity(ContainerRuntimeStorage storage)
        {
            if (storage == null)
            {
                return 0;
            }

            return Mathf.Max(0, storage.wood)
                   + Mathf.Max(0, storage.stone)
                   + Mathf.Max(0, storage.food)
                   + storage.storedFoodItems.Values.Where(v => v > 0).Sum()
                   + storage.storedWeapons.Values.Where(v => v > 0).Sum();
        }

        // ══════════════════════════════════════════════════════════════
        //  Crafting System
        // ══════════════════════════════════════════════════════════════

        private void InitializeCraftingRecipes()
        {
            craftingRecipes = new List<CraftingRecipe>
            {
                new() { id = "craft_wooden_fence", displayName = "Wooden Fence", description = "A simple wooden fence section.",
                    resultItemId = "town:WoodFence_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 3 } },
                new() { id = "craft_wooden_gate", displayName = "Wooden Gate", description = "A gate for your settlement.",
                    resultItemId = "town:WoodGate", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 5, stone = 1 } },
                new() { id = "craft_stone_wall", displayName = "Stone Wall", description = "A sturdy stone wall segment.",
                    resultItemId = "town:StoneWall_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 1, stone = 5 } },
                new() { id = "craft_campfire", displayName = "Campfire", description = "Provides light and warmth.",
                    resultItemId = "town:Campfire", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 4, stone = 2 } },
                new() { id = "craft_torch", displayName = "Torch", description = "A standing torch for your settlement.",
                    resultItemId = "town:Torch_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 2 } },
                new() { id = "craft_barrel", displayName = "Storage Barrel", description = "Extra storage for your settlement.",
                    resultItemId = "town:Barrel_01", resultCount = 1, resultIsStructure = true,
                    cost = new CraftingIngredient { wood = 4, stone = 1 } },
                new() { id = "craft_sword", displayName = "Iron Sword", description = "A basic melee weapon.",
                    resultItemId = "weapons:Sword01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 2, stone = 6 } },
                new() { id = "craft_axe", displayName = "Battle Axe", description = "Heavy hitting but slow.",
                    resultItemId = "weapons:Ax01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 3, stone = 5 } },
                new() { id = "craft_shield", displayName = "Wooden Shield", description = "Blocks incoming damage.",
                    resultItemId = "weapons:Shield01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 5, stone = 2 } },
                new() { id = "craft_dagger", displayName = "Dagger", description = "Fast and light.",
                    resultItemId = "weapons:Dagger01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 1, stone = 3 } },
                new() { id = "craft_spear", displayName = "Spear", description = "Good reach, balanced stats.",
                    resultItemId = "weapons:Spear01", resultCount = 1, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 4, stone = 3 } },
                new() { id = "craft_arrows", displayName = "Arrows (x5)", description = "Ammunition for bows.",
                    resultItemId = "weapons:Arrow01", resultCount = 5, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 3, stone = 1 } },
                new() { id = "craft_cooked_food", displayName = "Cooked Rations", description = "Prepared food from raw ingredients.",
                    resultItemId = "food:Bread_01", resultCount = 2, resultIsStructure = false,
                    cost = new CraftingIngredient { wood = 1, food = 3 } },
                // ── Seed recipes ──────────────────────────────────────
                new() { id = "craft_tomato_seeds", displayName = "Tomato Seeds (x3)", description = "Plant in ground to grow tomatoes over time.",
                    resultItemId = "seed:tomato", resultCount = 3, resultIsStructure = false,
                    cost = new CraftingIngredient { food = 2 } },
                new() { id = "craft_cabbage_seeds", displayName = "Cabbage Seeds (x3)", description = "Plant in ground to grow cabbages over time.",
                    resultItemId = "seed:cabbage", resultCount = 3, resultIsStructure = false,
                    cost = new CraftingIngredient { food = 2 } },
            };

            // Validate recipes against actual catalog — remove any whose resultItemId doesn't exist
            craftingRecipes.RemoveAll(r => !catalogLookup.ContainsKey(r.resultItemId));
        }

        private bool CanAffordRecipe(CraftingRecipe recipe)
        {
            if (recipe?.cost == null) return true;
            CraftingIngredient c = recipe.cost;
            if (c.wood > wood) return false;
            if (c.stone > stone) return false;
            if (c.food > food) return false;
            if (!string.IsNullOrWhiteSpace(c.requiredItemId) && c.requiredItemCount > 0)
            {
                int itemCount = GetTotalItemCount(c.requiredItemId);
                if (itemCount < c.requiredItemCount) return false;
            }
            return true;
        }

        private int GetTotalItemCount(string itemId)
        {
            int count = 0;
            if (storedFoodInventory.TryGetValue(itemId, out int fc)) count += fc;
            if (storedWeaponInventory.TryGetValue(itemId, out int wc)) count += wc;
            return count;
        }

        private void SpendCraftingCost(CraftingIngredient cost)
        {
            if (cost == null) return;

            wood = Mathf.Max(0, wood - cost.wood);
            stone = Mathf.Max(0, stone - cost.stone);
            food = Mathf.Max(0, food - cost.food);

            if (!string.IsNullOrWhiteSpace(cost.requiredItemId) && cost.requiredItemCount > 0)
            {
                int remaining = cost.requiredItemCount;
                remaining = SpendItemFromInventory(storedFoodInventory, cost.requiredItemId, remaining);
                SpendItemFromInventory(storedWeaponInventory, cost.requiredItemId, remaining);
            }
        }

        private int SpendItemFromInventory(Dictionary<string, int> inventory, string itemId, int remaining)
        {
            if (remaining <= 0) return 0;
            if (!inventory.TryGetValue(itemId, out int available) || available <= 0) return remaining;
            int spend = Mathf.Min(remaining, available);
            RemoveInventoryCount(inventory, itemId, spend);
            return remaining - spend;
        }

        private void CraftRecipe(CraftingRecipe recipe)
        {
            if (recipe == null) return;
            if (!CanAffordRecipe(recipe))
            {
                SetStatusMessage("Not enough materials to craft this.");
                return;
            }

            if (!catalogLookup.TryGetValue(recipe.resultItemId, out BuildCatalogItem resultItem))
            {
                SetStatusMessage("Recipe result not found in catalog.");
                return;
            }

            SpendCraftingCost(recipe.cost);

            if (recipe.resultIsStructure)
            {
                // Structure recipes go through the build/placement flow
                SetStatusMessage($"Crafted {recipe.displayName} — use Build menu to place it.");
                // Refund the cost into a "free" placement by giving resources back
                // Actually, for structures we just let them build with the build system.
                // The cost was already spent — mark it as available in build mode.
                // Simplest approach: add as a free-place credit by just opening build panel.
                // Better: directly add to inventory as a placeable item credit.
                // Most coherent: the build system already charges BuildCost. Since we just
                // spent crafting cost, refund the build cost equivalent so the player can
                // place it for free. But this is complex. Simplest: give the resources back
                // and let build system charge them. That means crafting structures is pointless.
                //
                // Best approach: add the item to weapon inventory as a "structure kit" that
                // the player can then place.
                AddInventoryCount(storedWeaponInventory, recipe.resultItemId, recipe.resultCount);
            }
            else
            {
                // Item/pickup recipes go to the appropriate inventory
                if (resultItem.pickupInventoryType == PickupInventoryType.Food)
                {
                    AddInventoryCount(storedFoodInventory, recipe.resultItemId, recipe.resultCount);
                }
                else
                {
                    AddInventoryCount(storedWeaponInventory, recipe.resultItemId, recipe.resultCount);
                }
            }

            SetStatusMessage($"Crafted {recipe.resultCount}x {recipe.displayName}!");
            SaveWorld();
        }

        private string GetRecipeCostDisplay(CraftingRecipe recipe)
        {
            if (recipe?.cost == null) return "Free";
            CraftingIngredient c = recipe.cost;
            List<string> parts = new();
            if (c.wood > 0) parts.Add($"{c.wood} wood");
            if (c.stone > 0) parts.Add($"{c.stone} stone");
            if (c.food > 0) parts.Add($"{c.food} food");
            if (!string.IsNullOrWhiteSpace(c.requiredItemId) && c.requiredItemCount > 0)
            {
                string name = GetDisplayNameForItemId(c.requiredItemId);
                parts.Add($"{c.requiredItemCount}x {name}");
            }
            return parts.Count == 0 ? "Free" : string.Join("  ·  ", parts);
        }

        private string FormatInventorySummary(Dictionary<string, int> inventory, int maxEntries)
        {
            if (inventory == null || inventory.Count == 0 || maxEntries <= 0)
            {
                return "Empty";
            }

            List<string> parts = new List<string>();

            foreach (KeyValuePair<string, int> pair in inventory
                .Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key))
            {
                string label = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                parts.Add($"{label} x{pair.Value}");
            }

            if (parts.Count == 0)
            {
                return "Empty";
            }

            int shownCount = Mathf.Min(maxEntries, parts.Count);
            string summary = string.Join(", ", parts.Take(shownCount));

            if (parts.Count > shownCount)
            {
                summary += $" +{parts.Count - shownCount} more";
            }

            return summary;
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
            if (slotIndex < 0)
            {
                return "-";
            }

            if (slotIndex < 9)
            {
                return (slotIndex + 1).ToString();
            }

            if (slotIndex == 9)
            {
                return "0";
            }

            return "?";
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

            // X key drops the selected inventory item to the ground
            if (Keyboard.current.xKey.wasPressedThisFrame && selectedInGameMenuTab == InGameMenuTab.Inventory)
            {
                TryDropSelectedInventoryItem();
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

        private void TryDropSelectedInventoryItem()
        {
            List<JournalInventoryEntry> entries = GetJournalInventoryEntries();
            JournalInventoryEntry? selected = GetSelectedInventoryEntry(entries);
            if (selected == null || selected.Value.quantity <= 0)
            {
                SetStatusMessage("No item selected to drop.");
                return;
            }

            string itemId = selected.Value.itemId;
            string displayName = selected.Value.displayName;

            // Handle raw resources (not catalog items — just decrement the resource counter)
            if (string.Equals(itemId, "resource_wood", StringComparison.Ordinal))
            {
                if (wood <= 0) return;
                wood--;
                SetStatusMessage("Dropped 1 wood.");
                SaveWorld();
                return;
            }

            if (string.Equals(itemId, "resource_stone", StringComparison.Ordinal))
            {
                if (stone <= 0) return;
                stone--;
                SetStatusMessage("Dropped 1 stone.");
                SaveWorld();
                return;
            }

            if (string.Equals(itemId, "carry_food", StringComparison.Ordinal))
            {
                if (food <= 0) return;
                food--;
                SetStatusMessage("Dropped 1 food.");
                SaveWorld();
                return;
            }

            // Catalog item: must exist in catalog to spawn as a world pickup
            if (!catalogLookup.TryGetValue(itemId, out BuildCatalogItem catalogItem))
            {
                SetStatusMessage($"Cannot drop {displayName} — unknown catalog item.");
                return;
            }

            // Remove 1 from the correct inventory
            if (storedFoodInventory.ContainsKey(itemId))
            {
                RemoveInventoryCount(storedFoodInventory, itemId, 1);
            }
            else if (storedWeaponInventory.ContainsKey(itemId))
            {
                RemoveInventoryCount(storedWeaponInventory, itemId, 1);
            }
            else
            {
                SetStatusMessage($"Cannot drop {displayName} — not in inventory.");
                return;
            }

            // Spawn the pickup on the ground in front of the player
            if (playerTransform != null && catalogItem.prefab != null)
            {
                Vector3 dropPosition = playerTransform.position + playerTransform.forward * 2f;
                dropPosition = GetGroundedPosition(dropPosition);

                SpawnCatalogItem(
                    catalogItem,
                    null,
                    dropPosition,
                    Quaternion.identity,
                    1f,
                    registerForSave: true,
                    placedByPlayer: true,
                    alignToGround: true);
            }

            SetStatusMessage($"Dropped {displayName}.");
            SaveWorld();
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

            // Apply immediately so the cursor unlocks and camera/look stops
            // on the same frame the key is pressed (before LateUpdate).
            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
                playerMovement.InputSuppressed = true;

            if (pauseGameWhenJournalOpen)
            {
                timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? timeScaleBeforePause : Time.timeScale;
                Time.timeScale = 0f;
            }
        }

        private void CloseInGameMenu()
        {
            inGameMenuOpen = false;
            pointerMode = false;

            // Restore gameplay mode immediately.
            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
                playerMovement.InputSuppressed = false;

            if (pauseGameWhenJournalOpen && Mathf.Approximately(Time.timeScale, 0f))
            {
                Time.timeScale = timeScaleBeforePause;
            }
        }

        private void OpenEscMenu()
        {
            if (inGameMenuOpen || buildPanelOpen)
                return;

            escMenuOpen = true;
            pointerMode = true;

            followCamera?.SetUiCursorMode(true);
            if (playerMovement != null)
                playerMovement.InputSuppressed = true;
        }

        private void CloseEscMenu()
        {
            escMenuOpen = false;
            pointerMode = false;

            followCamera?.SetUiCursorMode(false);
            if (playerMovement != null)
                playerMovement.InputSuppressed = false;
        }

        private void EscMenuOpenSettings()
        {
            CloseEscMenu();
            selectedInGameMenuTab = InGameMenuTab.Settings;
            OpenInGameMenu();
        }

        private void DrawEscMenu()
        {
            float screenW = Screen.width;
            float screenH = Screen.height;

            // Dim overlay
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, screenW, screenH), panelTexture);

            // Centered panel
            float panelW = Mathf.Clamp(340f * currentUiScale, 280f, 480f);
            float buttonH = Mathf.Clamp(42f * currentUiScale, 36f, 58f);
            float buttonGap = Mathf.RoundToInt(10f * currentUiScale);
            float titleH = Mathf.Clamp(48f * currentUiScale, 40f, 66f);
            float innerPad = Mathf.RoundToInt(24f * currentUiScale);
            int buttonCount = 4;
            float panelH = titleH + innerPad + (buttonH * buttonCount) + (buttonGap * (buttonCount - 1)) + innerPad + innerPad;

            float panelX = (screenW - panelW) * 0.5f;
            float panelY = (screenH - panelH) * 0.5f;
            Rect panelRect = new Rect(panelX, panelY, panelW, panelH);

            // Panel background
            GUI.color = new Color(0.040f, 0.051f, 0.031f, 0.96f);
            GUI.DrawTexture(panelRect, journalShellTexture);

            // Border
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.50f);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.yMax - 1f, panelRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 1f, panelRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.xMax - 1f, panelRect.y, 1f, panelRect.height), amberAccentTexture);

            // Top accent
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.92f);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), amberAccentTexture);

            GUI.color = prev;

            // Title
            Rect titleRect = new Rect(panelRect.x, panelRect.y + innerPad, panelRect.width, titleH);
            GUI.Label(titleRect, "PAUSED", journalTitleStyle);

            // Separator under title
            prev = GUI.color;
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.35f);
            GUI.DrawTexture(new Rect(panelRect.x + innerPad, titleRect.yMax, panelRect.width - innerPad * 2f, 1f), amberAccentTexture);
            GUI.color = prev;

            // Buttons area
            float buttonsY = titleRect.yMax + innerPad;
            float buttonX = panelRect.x + innerPad;
            float buttonW = panelRect.width - innerPad * 2f;

            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Resume"))
            {
                CloseEscMenu();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Settings"))
            {
                EscMenuOpenSettings();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Controls / Help"))
            {
                CloseEscMenu();
                selectedInGameMenuTab = InGameMenuTab.Overview;
                OpenInGameMenu();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        private bool DrawEscMenuButton(Rect rect, string label)
        {
            Color prev = GUI.color;
            bool hover = rect.Contains(Event.current.mousePosition);

            // Button background
            GUI.color = hover
                ? new Color(0.788f, 0.659f, 0.298f, 0.22f)
                : new Color(0.10f, 0.12f, 0.08f, 0.80f);
            GUI.DrawTexture(rect, panelTexture);

            // Button border
            GUI.color = hover
                ? new Color(0.788f, 0.659f, 0.298f, 0.60f)
                : new Color(0.788f, 0.659f, 0.298f, 0.25f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), amberAccentTexture);

            GUI.color = prev;

            // Label
            GUI.Label(rect, label, journalTitleStyle);

            // Click detection
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private void DrawHud()
        {
            UiLayout layout = GetUiLayout();
            BuildCatalogItem selectedHotbarItem = GetSelectedHotbarItem();
            float barHeight = Mathf.Clamp(14f * currentUiScale, 11f, 20f);

            DrawBorderedPanel(layout.playerPanelRect, () =>
            {
                GUILayout.Label("Settler", headingStyle);
                GUILayout.Space(4f);

                DrawStatBarRow(
                    "HP",
                    health / (float)MaxHealth,
                    statBarHpTexture,
                    $"{health}/{MaxHealth}",
                    barHeight);

                DrawStatBarRow(
                    "Food",
                    Mathf.Clamp01(storedFoodInventory.Values.Sum() / 10f),
                    statBarHungerTexture,
                    FormatInventorySummary(storedFoodInventory, 2),
                    barHeight);

                DrawStatBarRow(
                    "Arms",
                    Mathf.Clamp01(storedWeaponInventory.Values.Sum() / 6f),
                    statBarStaminaTexture,
                    FormatInventorySummary(storedWeaponInventory, 2),
                    barHeight);
            });

            if (showActionHints)
            {
                DrawBorderedPanel(layout.helpPanelRect, () =>
                {
                    GUILayout.Label("Actions", headingStyle);
                    GUILayout.Space(2f);

                    foreach (string line in GetActionPanelLines())
                    {
                        GUILayout.Label(line, line.Contains('|') ? smallMutedStyle : labelStyle);
                    }
                });
            }

            DrawBorderedPanel(layout.modePanelRect, () =>
            {
                string modeText = deleteMode ? "DELETE" : devBuildMode ? "DEV BUILD" : "RESOURCE";
                string statusText = selectedHotbarItem != null
                    ? selectedHotbarItem.displayName
                    : $"Slot {GetHotbarKeyLabel(selectedHotbarIndex)} Empty";

                string hintText = deleteMode
                    ? "Aim + LMB to remove"
                    : devBuildMode
                        ? "Free placement enabled"
                        : "B build | X delete";

                Color modeColor = deleteMode
                    ? new Color(0.84f, 0.32f, 0.28f)
                    : devBuildMode
                        ? new Color(0.62f, 0.82f, 0.48f)
                        : new Color(0.82f, 0.64f, 0.24f);

                Rect badgeRect = GUILayoutUtility.GetRect(
                    10f,
                    Mathf.RoundToInt(20f * currentUiScale),
                    GUILayout.ExpandWidth(true));

                Color previousColor = GUI.color;
                GUI.color = new Color(modeColor.r, modeColor.g, modeColor.b, 0.22f);
                GUI.DrawTexture(badgeRect, modeBadgeTexture);
                GUI.color = previousColor;

                modeLabelStyle.normal.textColor = modeColor;
                GUI.Label(badgeRect, modeText, modeLabelStyle);

                GUILayout.Space(4f);
                GUILayout.Label(statusText, labelStyle);
                GUILayout.Label(hintText, smallMutedStyle);
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
            float slotSize = Mathf.Clamp(68f * currentUiScale, 54f, 94f);
            float gap = Mathf.Clamp(8f * currentUiScale, 5f, 14f);
            float barHeight = Mathf.Clamp(6f * currentUiScale, 4f, 9f);

            float totalWidth = (slotSize * HotbarSlotCount) + (gap * (HotbarSlotCount - 1));
            Rect areaRect = new Rect(
                (Screen.width - totalWidth) * 0.5f,
                Screen.height - slotSize - Mathf.Clamp(30f * currentUiScale, 22f, 42f),
                totalWidth,
                slotSize + barHeight + 6f);

            GUILayout.BeginArea(areaRect);
            GUILayout.BeginHorizontal();

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                BuildCatalogItem item = null;
                string itemId = favoriteHotbarItemIds[i];
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    catalogLookup.TryGetValue(itemId, out item);
                }

                bool isSelected = i == selectedHotbarIndex;

                GUILayout.BeginVertical(GUILayout.Width(slotSize));

                Rect topBarRect = GUILayoutUtility.GetRect(slotSize, barHeight, GUILayout.Width(slotSize), GUILayout.Height(barHeight));
                GUI.DrawTexture(topBarRect, statBarBgTexture);

                if (item != null)
                {
                    Rect fillRect = new Rect(topBarRect.x + 1f, topBarRect.y + 1f, topBarRect.width - 2f, topBarRect.height - 2f);
                    GUI.DrawTexture(fillRect, statBarHpTexture);
                }

                Rect slotRect = GUILayoutUtility.GetRect(slotSize, slotSize, GUILayout.Width(slotSize), GUILayout.Height(slotSize));

                if (isSelected)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.22f);
                    GUI.DrawTexture(new Rect(slotRect.x - 2f, slotRect.y - 2f, slotRect.width + 4f, slotRect.height + 4f), modeBadgeTexture);
                    GUI.color = prev;
                }

                GUI.Box(slotRect, GUIContent.none, isSelected ? hotbarSelectedSlotStyle : hotbarSlotStyle);

                Rect keyRect = new Rect(slotRect.x + 6f, slotRect.y + 4f, slotRect.width - 12f, 14f);
                GUI.Label(keyRect, GetHotbarKeyLabel(i), hotbarKeyStyle);

                if (item != null)
                {
                    Rect nameRect = new Rect(slotRect.x + 8f, slotRect.y + 22f, slotRect.width - 16f, slotRect.height - 30f);
                    GUI.Label(nameRect, item.displayName, labelStyle);

                    Rect kindRect = new Rect(slotRect.x + 8f, slotRect.yMax - 16f, slotRect.width - 16f, 12f);
                    GUI.Label(kindRect, GetItemKindLabel(item.kind), smallMutedStyle);
                }
                else
                {
                    Rect emptyRect = new Rect(slotRect.x + 8f, slotRect.y + 24f, slotRect.width - 16f, slotRect.height - 28f);
                    GUI.Label(emptyRect, "Empty", smallMutedStyle);
                }

                GUILayout.EndVertical();

                if (i < HotbarSlotCount - 1)
                {
                    GUILayout.Space(gap);
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

            float previewHeight = Mathf.Clamp(panelRect.width * 0.44f, 140f, 260f);
            float detailsHeight = Mathf.Clamp(90f * currentUiScale, 78f, 124f);
            float footerHeight = Mathf.Clamp(50f * currentUiScale, 42f, 64f);
            float listHeight = Mathf.Max(
                130f,
                panelRect.height - previewHeight - detailsHeight - footerHeight - Mathf.Clamp(96f * currentUiScale, 80f, 120f));

            GUILayout.BeginArea(panelRect, windowStyle);

            GUILayout.Label("Build Catalog", headingStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawCategoryButton(BuildCategory.Town);
            DrawCategoryButton(BuildCategory.Farm);
            DrawCategoryButton(BuildCategory.Food);
            DrawCategoryButton(BuildCategory.Weapons);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
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

                string subtitle = GetBuildMenuItemSubtitle(item, costLabel);
                string buttonText = $"{item.displayName}\n{subtitle}";
                GUIStyle itemStyle = isSelected ? selectedCatalogItemButtonStyle : catalogItemButtonStyle;

                if (GUILayout.Button(buttonText, itemStyle, GUILayout.Height(layout.buildItemHeight)))
                {
                    SelectItem(item.category, i);
                }
            }

            if (items.Count == 0)
            {
                GUILayout.Label("No catalog items found for this category.", smallMutedStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10f);
            GUILayout.Label("Selected Preview", headingStyle);
            DrawSelectedPreviewCard(selectedItem, previewHeight);

            GUILayout.Space(8f);
            Rect detailsRect = GUILayoutUtility.GetRect(10f, detailsHeight, GUILayout.ExpandWidth(true), GUILayout.Height(detailsHeight));
            DrawBorderPanel(detailsRect, hudCardTexture, 1f);

            Rect detailsInner = new Rect(detailsRect.x + 10f, detailsRect.y + 8f, detailsRect.width - 20f, detailsRect.height - 16f);

            if (selectedItem != null)
            {
                GUI.Label(new Rect(detailsInner.x, detailsInner.y, detailsInner.width, 20f), selectedItem.displayName, headingStyle);
                GUI.Label(
                    new Rect(detailsInner.x, detailsInner.y + 22f, detailsInner.width, 18f),
                    $"{selectedItem.category}  |  {GetItemKindLabel(selectedItem.kind)}",
                    smallMutedStyle);

                string costText = devBuildMode
                    ? "Cost: Free in DEV Build Mode"
                    : $"Cost: {selectedItem.cost.ToDisplayString()}";

                GUI.Label(new Rect(detailsInner.x, detailsInner.y + 42f, detailsInner.width, 18f), costText, labelStyle);

                if (!devBuildMode && !CanAfford(selectedItem.cost))
                {
                    GUI.Label(
                        new Rect(detailsInner.x, detailsInner.y + 60f, detailsInner.width, 18f),
                        "Missing resources. Gather more or toggle DEV Build.",
                        smallMutedStyle);
                }
            }
            else
            {
                GUI.Label(new Rect(detailsInner.x, detailsInner.y, detailsInner.width, 20f), "No item selected", headingStyle);
                GUI.Label(
                    new Rect(detailsInner.x, detailsInner.y + 24f, detailsInner.width, 36f),
                    "Pick an item from the current category to start placement.",
                    smallMutedStyle);
            }

            GUILayout.Space(8f);

            Rect footerRect = GUILayoutUtility.GetRect(10f, footerHeight, GUILayout.ExpandWidth(true), GUILayout.Height(footerHeight));
            DrawBorderPanel(footerRect, hudCardTexture, 1f);

            Rect footerInner = new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, footerRect.height - 16f);
            GUI.Label(
                new Rect(footerInner.x, footerInner.y, footerInner.width, 16f),
                $"Mode: {(devBuildMode ? "DEV Build" : "Resource Build")}  |  L toggles",
                labelStyle);
            GUI.Label(
                new Rect(footerInner.x, footerInner.y + 18f, footerInner.width, 16f),
                "Wheel/W/S item  A/D tab  F favorite  Enter/LMB choose  B/Esc close",
                smallMutedStyle);

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

            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padH = Mathf.RoundToInt(14f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);
            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, innerRect.height - 8f),
                prompt,
                promptLabelStyle);
        }

        private void DrawStatusMessage()
        {
            if (string.IsNullOrWhiteSpace(statusMessage) || Time.unscaledTime > statusMessageUntil)
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.statusRect, statusMessage, labelStyle);

            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padH = Mathf.RoundToInt(14f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);
            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, innerRect.height - 8f),
                statusMessage,
                labelStyle);
        }

        private void DrawInGameMenu()
        {
            float screenW = Screen.width;
            float screenH = Screen.height;
            float marginFraction = screenW >= 2560f ? 0.025f : screenW >= 1600f ? 0.03f : 0.035f;
            float margin = Mathf.Clamp(
                Mathf.Min(screenW, screenH) * marginFraction, 20f, 80f);

            Rect shellRect = new Rect(
                margin,
                margin,
                Mathf.Max(420f, screenW - (margin * 2f)),
                Mathf.Max(320f, screenH - (margin * 2f)));

            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(
                new Rect(0f, 0f, screenW, screenH),
                panelTexture);

            GUI.color = new Color(0.040f, 0.051f, 0.031f, 0.98f);
            GUI.DrawTexture(shellRect, journalShellTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.45f);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.y, shellRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.yMax - 1f, shellRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.y, 1f, shellRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.xMax - 1f, shellRect.y, 1f, shellRect.height), amberAccentTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.95f);
            GUI.DrawTexture(
                new Rect(shellRect.x, shellRect.y, shellRect.width, 2f),
                amberAccentTexture);

            float navHeight = Mathf.Clamp(82f * currentUiScale, 74f, 140f);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.92f);
            GUI.DrawTexture(
                new Rect(shellRect.x + 22f, shellRect.y + navHeight + 10f,
                    shellRect.width - 44f, 1f),
                amberAccentTexture);

            GUI.color = prev;

            float padding = Mathf.RoundToInt(22f * currentUiScale);
            float footerHeight = Mathf.Clamp(30f * currentUiScale, 26f, 42f);

            Rect navRect = new Rect(
                shellRect.x + padding,
                shellRect.y + padding,
                shellRect.width - (padding * 2f),
                navHeight);

            Rect footerRect = new Rect(
                shellRect.x + padding,
                shellRect.yMax - padding - footerHeight,
                shellRect.width - (padding * 2f),
                footerHeight);

            Rect contentRect = new Rect(
                shellRect.x + padding,
                navRect.yMax + Mathf.RoundToInt(14f * currentUiScale),
                shellRect.width - (padding * 2f),
                footerRect.y - (navRect.yMax + Mathf.RoundToInt(22f * currentUiScale)));

            Rect escRect = new Rect(
                shellRect.xMax - padding - 150f,
                shellRect.y + padding,
                150f,
                24f);
            GUI.Label(escRect, "Esc to close", escHintStyle);

            GUILayout.BeginArea(navRect);
            GUILayout.Label("Settler Journal", journalTitleStyle);
            GUILayout.Label("World state, tracking, and character notes", journalSubtitleStyle);
            GUILayout.Space(Mathf.RoundToInt(10f * currentUiScale));

            GUILayout.BeginHorizontal();
            DrawInGameMenuTabButton(InGameMenuTab.Overview, "Overview");
            DrawInGameMenuTabButton(InGameMenuTab.Skills, "Skills");
            DrawInGameMenuTabButton(InGameMenuTab.Inventory, "Inventory");
            DrawInGameMenuTabButton(InGameMenuTab.Settings, "Settings");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            prev = GUI.color;
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.30f);
            GUI.DrawTexture(
                new Rect(footerRect.x, footerRect.y - 4f, footerRect.width, 1f),
                amberAccentTexture);
            GUI.color = prev;

            GUI.Label(
                new Rect(footerRect.x + 2f, footerRect.y + 4f, footerRect.width, 18f),
                "Journal navigation  •  Esc close  •  A/D or 1-4 switch tabs",
                smallMutedStyle);

            switch (selectedInGameMenuTab)
            {
                case InGameMenuTab.Overview: DrawOverviewTab(contentRect); break;
                case InGameMenuTab.Skills: DrawSkillsTab(contentRect); break;
                case InGameMenuTab.Inventory: DrawInventoryTab(contentRect); break;
                case InGameMenuTab.Settings: DrawInGameSettingsTab(contentRect); break;
            }
        }

        private void DrawInGameMenuTabButton(InGameMenuTab tab, string label)
        {
            bool isSelected = selectedInGameMenuTab == tab;
            float buttonHeight = Mathf.RoundToInt(38f * currentUiScale);
            float minWidth = Mathf.Clamp(110f * currentUiScale, 96f, 240f);
            float tabGap = Mathf.RoundToInt(10f * currentUiScale);

            Rect buttonRect = GUILayoutUtility.GetRect(
                minWidth,
                buttonHeight,
                GUILayout.MinWidth(minWidth),
                GUILayout.MaxWidth(280f),
                GUILayout.Height(buttonHeight));

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y + 1f, buttonRect.width, buttonRect.height - 2f), modeBadgeTexture);
                GUI.color = previousColor;
            }

            GUIStyle style = isSelected ? journalActiveTabStyle : journalTabStyle;
            if (GUI.Button(buttonRect, label, style))
            {
                selectedInGameMenuTab = tab;
            }

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.95f);
                GUI.DrawTexture(new Rect(buttonRect.x + 8f, buttonRect.yMax - 2f, buttonRect.width - 16f, 2f), amberAccentTexture);
                GUI.color = previousColor;
            }

            GUILayout.Space(tabGap);
        }

        private void DrawJournalPanel(Rect rect, string title, Action drawer, string subtitle = null)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);

            float padH = Mathf.RoundToInt(16f * currentUiScale);
            float padV = Mathf.RoundToInt(14f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);

            float titleHeight = Mathf.RoundToInt(22f * currentUiScale);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, titleHeight);
            GUI.Label(titleRect, title, headingStyle);

            Rect accentRect = new Rect(innerRect.x, titleRect.yMax + 5f, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            float subtitleHeight = 0f;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                subtitleHeight = Mathf.Clamp(18f * currentUiScale, 16f, 24f);
                Rect subtitleRect = new Rect(innerRect.x, accentRect.yMax + 6f, innerRect.width, subtitleHeight);
                GUI.Label(subtitleRect, subtitle, journalSubtitleStyle);
            }

            float contentTop = accentRect.yMax + 10f + subtitleHeight + (subtitleHeight > 0f ? 2f : 0f);
            Rect contentRect = new Rect(
                innerRect.x,
                contentTop,
                innerRect.width,
                Mathf.Max(20f, innerRect.yMax - contentTop));

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawOverviewTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);
            float topHeight = Mathf.Clamp(contentRect.height * 0.50f, 220f, 540f);
            float leftWidth = Mathf.Max(200f, contentRect.width * 0.26f);
            float middleWidth = Mathf.Max(240f, contentRect.width * 0.36f);
            float rightWidth = Mathf.Max(180f, contentRect.width - leftWidth - middleWidth - (gutter * 2f));

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
                GUILayout.Label($"Carried: {FormatInventorySummary(storedFoodInventory, 3)}", journalBodyStyle);
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

        private void DrawSelectedPreviewCard(BuildCatalogItem item, float height)
        {
            Rect outerRect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            DrawBorderPanel(outerRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(outerRect.x + 10f, outerRect.y + 10f, outerRect.width - 20f, outerRect.height - 20f);

            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 20f);
            GUI.Label(titleRect, "Selected Preview", headingStyle);

            Rect accentRect = new Rect(innerRect.x, titleRect.yMax + 4f, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            float infoHeight = 42f;
            float previewTop = accentRect.yMax + 8f;
            float previewHeight = Mathf.Max(84f, innerRect.height - (previewTop - innerRect.y) - infoHeight);
            Rect previewRect = new Rect(innerRect.x, previewTop, innerRect.width, previewHeight);

            DrawBorderPanel(previewRect, journalCardTexture, 1f);

            Texture previewTexture = GetBuildPreviewTexture(item);
            if (previewTexture != null && item != null)
            {
                Rect imageRect = new Rect(previewRect.x + 8f, previewRect.y + 8f, previewRect.width - 16f, previewRect.height - 16f);
                GUI.DrawTexture(imageRect, previewTexture, ScaleMode.ScaleToFit, false);

                Rect infoRect = new Rect(innerRect.x, previewRect.yMax + 8f, innerRect.width, infoHeight);
                GUI.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 18f), item.displayName, headingStyle);
                GUI.Label(
                    new Rect(infoRect.x, infoRect.y + 20f, infoRect.width, 18f),
                    $"{item.category}  |  {GetItemKindLabel(item.kind)}",
                    smallMutedStyle);
            }
            else
            {
                string titleText = item == null ? "No item selected" : item.displayName;
                string bodyText = item == null
                    ? "Choose a build piece to inspect it here."
                    : "Preview is loading or unavailable for this item.";

                GUI.Label(
                    new Rect(previewRect.x + 12f, previewRect.center.y - 18f, previewRect.width - 24f, 20f),
                    titleText,
                    headingStyle);

                GUI.Label(
                    new Rect(previewRect.x + 12f, previewRect.center.y + 6f, previewRect.width - 24f, 36f),
                    bodyText,
                    promptLabelStyle);
            }
        }

        private void DrawSkillsTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.54f, 340f, 780f);

            Rect skillsRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect previewRect = new Rect(skillsRect.xMax + gutter, contentRect.y, contentRect.width - leftWidth - gutter, contentRect.height);

            DrawJournalPanel(skillsRect, "Skill Tracks", () =>
            {
                GUILayout.Label("Progress across current settlement activities.", journalSubtitleStyle);
                GUILayout.Space(8f);

                DrawSkillTrackCard(
                    "Builder",
                    Mathf.Clamp(CountPlacedObjects(ItemKind.Structure), 0, 5),
                    "Structures placed and maintained.");

                GUILayout.Space(6f);

                DrawSkillTrackCard(
                    "Gatherer",
                    Mathf.Clamp((wood / 5) + (stone / 3), 0, 5),
                    "Material collection efficiency.");

                GUILayout.Space(6f);

                int providerTier = Mathf.Clamp(Mathf.Max(food, storedFoodInventory.Values.Sum()), 0, 5);
                DrawSkillTrackCard(
                    "Provider",
                    providerTier,
                    "Food collection and storage.");

                GUILayout.Space(6f);

                DrawSkillTrackCard(
                    "Curator",
                    Mathf.Clamp(CountFavoritedSlots(), 0, 5),
                    "Pinned and organized build pieces.");
            }, "Character progression overview");

            DrawJournalPanel(previewRect, "Selected Preview", () =>
            {
                BuildCatalogItem selectedItem = GetSelectedHotbarItem();
                float previewHeight = Mathf.Clamp(previewRect.height - 96f, 180f, 360f);

                GUILayout.Label("Current hotbar focus", journalSubtitleStyle);
                GUILayout.Space(8f);

                DrawSelectedPreviewCard(selectedItem, previewHeight);

                GUILayout.Space(8f);
                Rect infoRect = GUILayoutUtility.GetRect(10f, 58f, GUILayout.ExpandWidth(true), GUILayout.Height(58f));
                DrawBorderPanel(infoRect, hudCardTexture, 1f);

                Rect innerRect = new Rect(infoRect.x + 10f, infoRect.y + 8f, infoRect.width - 20f, infoRect.height - 16f);

                if (selectedItem != null)
                {
                    GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 18f), selectedItem.displayName, headingStyle);
                    GUI.Label(
                        new Rect(innerRect.x, innerRect.y + 20f, innerRect.width, 16f),
                        $"{selectedItem.category}  |  {GetItemKindLabel(selectedItem.kind)}",
                        journalSubtitleStyle);
                }
                else
                {
                    GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 18f), "Nothing selected", headingStyle);
                    GUI.Label(
                        new Rect(innerRect.x, innerRect.y + 20f, innerRect.width, 16f),
                        "Pick a hotbar item or build piece to inspect it here.",
                        journalSubtitleStyle);
                }
            }, "Current build item");
        }

        private void DrawInventoryTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);

            // ── Proportional column sizing ────────────────────────────
            float totalWidth = contentRect.width - (gutter * 3f);
            float col1Fraction = 0.20f;
            float col2Fraction = 0.30f;
            float col3Fraction = 0.24f;
            float col4Fraction = 0.26f;
            float col1W = Mathf.Max(200f, totalWidth * col1Fraction);
            float col2W = Mathf.Max(260f, totalWidth * col2Fraction);
            float col3W = Mathf.Max(220f, totalWidth * col3Fraction);
            float col4W = Mathf.Max(230f, totalWidth * col4Fraction);

            Rect col1Rect = new Rect(contentRect.x, contentRect.y, col1W, contentRect.height);
            Rect col2Rect = new Rect(col1Rect.xMax + gutter, contentRect.y, col2W, contentRect.height);
            Rect col3Rect = new Rect(col2Rect.xMax + gutter, contentRect.y, col3W, contentRect.height);
            Rect col4Rect = new Rect(col3Rect.xMax + gutter, contentRect.y, col4W, contentRect.height);

            float col1TopH = Mathf.Clamp(col1Rect.height * 0.38f, 190f, 360f);
            Rect resourcesRect = new Rect(col1Rect.x, col1Rect.y, col1Rect.width, col1TopH);
            Rect loadoutRect = new Rect(col1Rect.x, resourcesRect.yMax + gutter,
                col1Rect.width, col1Rect.height - col1TopH - gutter);

            List<JournalInventoryEntry> entries = GetJournalInventoryEntries();
            JournalInventoryEntry? selectedEntry = GetSelectedInventoryEntry(entries);

            // ── Column 1: Resources & Loadout ─────────────────────────
            DrawJournalPanel(resourcesRect, "Resources & Harvest", () =>
            {
                DrawInventoryResourceStrip();
                GUILayout.Space(10f);
                GUILayout.Label("Food", headingStyle);
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                DrawMiniItemSlot("Pouch",
                    storedFoodInventory.Values.Sum() > 0
                        ? $"x{storedFoodInventory.Values.Sum()}" : "Empty");
                DrawMiniItemSlot("Rations", food > 0 ? $"x{food}" : "Empty");
                GUILayout.EndHorizontal();
            }, "Carry totals and stored food");

            DrawJournalPanel(loadoutRect, "Equipped Loadout", () =>
            {
                DrawEquippedLoadout();
            }, "Active gear and armor");

            // ── Column 2: Inventory Grid ──────────────────────────────
            DrawInventoryCenterPanel(col2Rect, entries);

            // ── Column 3: Selected Item + Equip ───────────────────────
            DrawJournalPanel(col3Rect, "Selected Item", () =>
            {
                GUILayout.Label("Details and equip options.", journalSubtitleStyle);
                GUILayout.Space(8f);
                DrawInventorySelectionDetails(selectedEntry);
                GUILayout.Space(10f);
                DrawEquipButton(selectedEntry);
                GUILayout.Space(6f);
                DrawDropButton(selectedEntry);
            }, "Inspection");

            // ── Column 4: Crafting ────────────────────────────────────
            DrawJournalPanel(col4Rect, "Crafting", () =>
            {
                DrawCraftingPanel();
            }, "Craft items from materials");
        }

        private void DrawInventoryCenterPanel(Rect rect, List<JournalInventoryEntry> entries)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);

            float pad = Mathf.RoundToInt(16f * currentUiScale);
            float padV = Mathf.RoundToInt(14f * currentUiScale);
            Rect inner = new Rect(rect.x + pad, rect.y + padV, rect.width - pad * 2f, rect.height - padV * 2f);

            float titleH = Mathf.RoundToInt(22f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, titleH), "Inventory Layout", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + titleH + 5f, inner.width, 2f), amberAccentTexture);
            GUI.Label(new Rect(inner.x, inner.y + titleH + 13f, inner.width, Mathf.RoundToInt(18f * currentUiScale)), "Carried inventory", journalSubtitleStyle);

            float gridTop = inner.y + titleH + Mathf.RoundToInt(38f * currentUiScale);
            float gridHeight = Mathf.Max(100f, inner.yMax - gridTop);
            Rect gridRect = new Rect(inner.x, gridTop, inner.width, gridHeight);

            // ── Grid content ──────────────────────────────────────────
            DrawInventoryCardGrid(entries, gridRect);
        }

        private void DrawInventoryCard(JournalInventoryEntry entry, float width, float height)
        {
            Rect cardRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            bool isSelected = string.Equals(selectedInventoryItemId, entry.itemId, StringComparison.Ordinal);
            bool isEquipment = catalogLookup.TryGetValue(entry.itemId, out BuildCatalogItem catItem)
                && GetEquipmentInfo(catItem) != null;

            Color prev = GUI.color;

            GUI.color = new Color(0.090f, 0.102f, 0.058f, 0.97f);
            GUI.DrawTexture(cardRect, journalCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, isSelected ? 0.70f : 0.25f);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), amberAccentTexture);

            if (isSelected)
            {
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.22f);
                GUI.DrawTexture(new Rect(cardRect.x + 1f, cardRect.y + 1f, cardRect.width - 2f, cardRect.height - 2f), modeBadgeTexture);
            }
            else if (entry.isFavorited)
            {
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.12f);
                GUI.DrawTexture(new Rect(cardRect.x + 1f, cardRect.y + 1f, cardRect.width - 2f, 20f), modeBadgeTexture);
            }

            GUI.color = prev;

            float innerPad = Mathf.RoundToInt(12f * currentUiScale);
            Rect innerRect = new Rect(cardRect.x + innerPad, cardRect.y + Mathf.RoundToInt(10f * currentUiScale),
                cardRect.width - innerPad * 2f, cardRect.height - Mathf.RoundToInt(20f * currentUiScale));

            float lineH = Mathf.RoundToInt(20f * currentUiScale);
            float smallH = Mathf.RoundToInt(16f * currentUiScale);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 28f, lineH), entry.displayName, headingStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + lineH + 2f, innerRect.width, smallH), entry.categoryLabel, smallMutedStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + lineH + smallH + 6f, innerRect.width, lineH), $"x{entry.quantity}", labelStyle);

            string subtitle = entry.isFavorited
                ? "Pinned build item"
                : isEquipment
                    ? "Equippable gear"
                    : entry.categoryLabel == "Food"
                        ? "Stored crop or ration"
                        : entry.categoryLabel == "Resource"
                            ? "Construction material"
                            : "Stored inventory item";

            float subtitleY = innerRect.y + lineH * 2f + smallH + 10f;
            if (subtitleY + smallH <= innerRect.yMax)
            {
                GUI.Label(new Rect(innerRect.x, subtitleY, innerRect.width, smallH + 10f), subtitle, journalSubtitleStyle);
            }

            if (entry.isFavorited)
            {
                GUI.Label(new Rect(innerRect.xMax - 24f, innerRect.y, 24f, smallH), "★", smallMutedStyle);
            }

            if (isEquipment && !entry.isFavorited)
            {
                GUI.Label(new Rect(innerRect.xMax - 24f, innerRect.y, 24f, smallH),
                    IsItemEquipped(entry.itemId) ? "✦" : "⚔", smallMutedStyle);
            }

            if (GUI.Button(cardRect, GUIContent.none, GUIStyle.none))
            {
                selectedInventoryItemId = entry.itemId;
            }
        }

        private void DrawInventoryCardGrid(List<JournalInventoryEntry> entries, Rect gridRect)
        {
            if (entries == null || entries.Count == 0)
            {
                // ── Empty state ───────────────────────────────────────
                DrawBorderPanel(gridRect, hudCardTexture, 1f);

                Rect emptyInner = new Rect(
                    gridRect.x + 12f, gridRect.y + 10f,
                    gridRect.width - 24f, gridRect.height - 20f);

                GUI.Label(new Rect(emptyInner.x, emptyInner.y, emptyInner.width, 20f),
                    "No stored inventory yet", headingStyle);
                GUI.Label(
                    new Rect(emptyInner.x, emptyInner.y + 24f, emptyInner.width, 40f),
                    "Gather crops or pick up crafted gear to populate this layout.",
                    journalSubtitleStyle);
                return;
            }

            // ── Frame background ──────────────────────────────────────
            Color prev = GUI.color;
            GUI.color = new Color(0.050f, 0.060f, 0.035f, 0.92f);
            GUI.DrawTexture(gridRect, hudCardTexture);
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.15f);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.y, gridRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.yMax - 1f, gridRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.y, 1f, gridRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.xMax - 1f, gridRect.y, 1f, gridRect.height), amberAccentTexture);
            GUI.color = prev;

            // ── Card sizing ───────────────────────────────────────────
            float gap = Mathf.Clamp(12f * currentUiScale, 8f, 16f);
            float pad = Mathf.RoundToInt(12f * currentUiScale);
            float usableWidth = Mathf.Max(160f, gridRect.width - (pad * 2f));

            int columns = usableWidth >= 900f ? 5
                        : usableWidth >= 650f ? 4
                        : usableWidth >= 420f ? 3
                        : 2;
            float rawCardWidth = (usableWidth - (gap * (columns - 1))) / columns;
            float cardWidth  = Mathf.Clamp(rawCardWidth, 100f, 230f);
            float cardHeight = Mathf.Clamp(112f * currentUiScale, 96f, 148f);

            // ── Scroll area — single BeginArea directly on screen rect ──
            Rect scrollOuter = new Rect(
                gridRect.x + pad,
                gridRect.y + pad,
                gridRect.width - (pad * 2f),
                gridRect.height - (pad * 2f));

            GUILayout.BeginArea(scrollOuter);
            inventoryGridScroll = GUILayout.BeginScrollView(
                inventoryGridScroll,
                false, false,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUIStyle.none);

            int index = 0;
            while (index < entries.Count)
            {
                GUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    if (index < entries.Count)
                    {
                        DrawInventoryCard(entries[index], cardWidth, cardHeight);
                        index++;
                    }
                    else
                    {
                        GUILayout.Space(cardWidth);
                    }

                    if (col < columns - 1)
                        GUILayout.Space(gap);
                }

                GUILayout.EndHorizontal();

                if (index < entries.Count)
                    GUILayout.Space(gap);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawQuestEntries()
        {
            int placedStructures = CountPlacedObjects(ItemKind.Structure);
            int favoritedCount = CountFavoritedSlots();
            int totalFood = Mathf.Max(food, storedFoodInventory.Values.Sum());

            DrawQuestRow(
                "Gather 10 Wood",
                wood >= 10,
                $"{Mathf.Min(wood, 10)}/10",
                Mathf.Clamp01(wood / 10f));

            DrawQuestRow(
                "Gather 6 Stone",
                stone >= 6,
                $"{Mathf.Min(stone, 6)}/6",
                Mathf.Clamp01(stone / 6f));

            DrawQuestRow(
                "Collect 3 Food",
                totalFood >= 3,
                $"{Mathf.Min(totalFood, 3)}/3",
                Mathf.Clamp01(totalFood / 3f));

            DrawQuestRow(
                "Place 1 Structure",
                placedStructures >= 1,
                $"{Mathf.Min(placedStructures, 1)}/1",
                Mathf.Clamp01(placedStructures));

            DrawQuestRow(
                "Favorite 3 Pieces",
                favoritedCount >= 3,
                $"{Mathf.Min(favoritedCount, 3)}/3",
                Mathf.Clamp01(favoritedCount / 3f));
        }

        private void DrawQuestRow(string title, bool complete, string progressText, float progress01)
        {
            float rowHeight = Mathf.Clamp(62f * currentUiScale, 54f, 78f);
            Rect rowRect = GUILayoutUtility.GetRect(
                10f, rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            Color prev = GUI.color;

            GUI.color = complete
                ? new Color(0.145f, 0.176f, 0.078f, 0.35f)
                : new Color(0.086f, 0.102f, 0.055f, 0.92f);
            GUI.DrawTexture(rowRect, journalCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.18f);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y, 1f, rowRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.xMax - 1f, rowRect.y, 1f, rowRect.height), amberAccentTexture);

            GUI.color = complete
                ? new Color(0.788f, 0.659f, 0.298f, 0.95f)
                : new Color(0.788f, 0.659f, 0.298f, 0.20f);
            GUI.DrawTexture(
                new Rect(rowRect.x, rowRect.y, 3f, rowRect.height),
                amberAccentTexture);

            GUI.color = prev;

            Rect inner = new Rect(
                rowRect.x + 14f,
                rowRect.y + 8f,
                rowRect.width - 24f,
                rowRect.height - 16f);

            Rect checkRect = new Rect(inner.x, inner.y + 1f, 16f, 16f);

            prev = GUI.color;
            if (complete)
            {
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.18f);
                GUI.DrawTexture(checkRect, modeBadgeTexture);
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.95f);
                GUI.DrawTexture(
                    new Rect(checkRect.x, checkRect.yMax - 1f, checkRect.width, 1f),
                    amberAccentTexture);
            }
            else
            {
                GUI.color = new Color(0.118f, 0.133f, 0.078f, 0.85f);
                GUI.DrawTexture(checkRect, skillBarBgTexture);
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.20f);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.y, checkRect.width, 1f), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.yMax - 1f, checkRect.width, 1f), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.y, 1f, checkRect.height), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.xMax - 1f, checkRect.y, 1f, checkRect.height), amberAccentTexture);
            }
            GUI.color = prev;

            GUIStyle checkStyle = complete ? questDoneStyle : questPendingStyle;
            GUI.Label(checkRect, complete ? "✓" : "○", checkStyle);

            Rect titleRect = new Rect(
                checkRect.xMax + 8f,
                inner.y,
                inner.width - 70f,
                18f);
            GUI.Label(titleRect, title, complete ? questDoneStyle : journalBodyStyle);

            Rect countRect = new Rect(inner.xMax - 52f, inner.y, 52f, 18f);
            GUI.Label(countRect, progressText, smallMutedStyle);

            float barHeight = Mathf.Clamp(6f * currentUiScale, 4f, 8f);
            Rect barBgRect = new Rect(
                checkRect.xMax + 8f,
                inner.y + 26f,
                inner.width - 26f,
                barHeight);

            prev = GUI.color;
            GUI.color = new Color(0.039f, 0.047f, 0.027f, 0.92f);
            GUI.DrawTexture(barBgRect, statBarBgTexture);

            float fillWidth = (barBgRect.width - 2f) * Mathf.Clamp01(progress01);
            if (fillWidth > 0f)
            {
                GUI.color = complete
                    ? new Color(0.788f, 0.659f, 0.298f, 0.95f)
                    : new Color(0.545f, 0.427f, 0.173f, 0.90f);
                GUI.DrawTexture(
                    new Rect(barBgRect.x + 1f, barBgRect.y + 1f, fillWidth, barBgRect.height - 2f),
                    complete ? skillBarFillTexture : statBarHungerTexture);
            }
            GUI.color = prev;

            if (complete)
            {
                GUI.Label(
                    new Rect(checkRect.xMax + 8f, inner.y + 38f, inner.width, 14f),
                    "Complete",
                    smallMutedStyle);
            }
        }

        private void DrawInGameSettingsTab(Rect contentRect)
        {
            // UI scale slider affects only the Settings tab layout.
            float uiScale = Mathf.Clamp(currentUiScale * settingsUiScaleMultiplier, 0.7f, 1.7f);

            float gutter = Mathf.RoundToInt(14f * uiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.56f, 380f, 720f);

            Rect settingsRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect previewRect = new Rect(
                settingsRect.xMax + gutter,
                contentRect.y,
                contentRect.width - leftWidth - gutter,
                contentRect.height);

            // Cache camera settings once when opening the settings tab.
            if (!settingsCameraCacheInitialized && followCamera != null)
            {
                cameraMouseSensitivity = followCamera.MouseSensitivity;
                cameraInvertY = followCamera.InvertY;
                cameraDistance = followCamera.Distance;

                // Derive smoothing multiplier from the currently applied runtime tuning.
                float baseFollowSmoothTime = cameraFeelMode == CameraFeelMode.Calm
                    ? 0.05f
                    : cameraFeelMode == CameraFeelMode.Normal
                        ? 0.035f
                        : 0.025f;

                float baseRotationSmoothSpeed = cameraFeelMode == CameraFeelMode.Calm
                    ? 24f
                    : cameraFeelMode == CameraFeelMode.Normal
                        ? 30f
                        : 36f;

                float fromFollow = baseFollowSmoothTime > 0f ? followCamera.FollowSmoothTime / baseFollowSmoothTime : 1f;
                float fromRotation = baseRotationSmoothSpeed > 0f
                    ? baseRotationSmoothSpeed / Mathf.Max(0.001f, followCamera.RotationSmoothSpeed)
                    : 1f;
                cameraSmoothingMultiplier = Mathf.Clamp((fromFollow + fromRotation) * 0.5f, 0.6f, 1.6f);

                settingsCameraCacheInitialized = true;
            }

            DrawJournalPanel(settingsRect, "In-Game Settings", () =>
            {
                GUILayout.Label("Session settings for HUD, camera, and journal behavior.", journalSubtitleStyle);
                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Display ─────────────────────────────────────────────
                DrawSettingsSection("Display", 6, uiScale, () =>
                {
                    DrawSliderRow(
                        "UI Scale",
                        settingsUiScaleMultiplier,
                        0.85f,
                        1.25f,
                        v => settingsUiScaleMultiplier = v,
                        v => $"{(currentUiScale * v):F2}x",
                        uiScale);

                    bool isFullscreen = Screen.fullScreen;
                    DrawToggleRow(
                        "Fullscreen",
                        isFullscreen,
                        () => Screen.fullScreen = !Screen.fullScreen,
                        uiScale,
                        "FULL",
                        "WINDOW");

                    DrawInfoRow(
                        "Resolution",
                        $"{Screen.width}x{Screen.height}",
                        uiScale);

                    DrawToggleRow(
                        "Show FPS",
                        showFps,
                        () => showFps = !showFps,
                        uiScale);

                    DrawToggleRow(
                        "Show Interaction Hints",
                        showActionHints,
                        () => showActionHints = !showActionHints,
                        uiScale);

                    DrawToggleRow(
                        "Crosshair",
                        showCrosshair,
                        () => showCrosshair = !showCrosshair,
                        uiScale);
                });

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Camera ─────────────────────────────────────────────
                DrawSettingsSection("Camera", 5, uiScale, () =>
                {
                    DrawSliderRow(
                        "Mouse Sensitivity",
                        cameraMouseSensitivity,
                        0.05f,
                        0.40f,
                        v =>
                        {
                            cameraMouseSensitivity = v;
                            followCamera?.ApplyLookSettings(cameraMouseSensitivity, cameraInvertY);
                        },
                        v => v.ToString("F2"),
                        uiScale);

                    DrawSliderRow(
                        "Camera Smoothing",
                        cameraSmoothingMultiplier,
                        0.60f,
                        1.60f,
                        v =>
                        {
                            cameraSmoothingMultiplier = v;
                            ApplyCameraFeel();
                        },
                        v => $"{v:F2}x",
                        uiScale);

                    DrawToggleRow(
                        "Invert Y",
                        cameraInvertY,
                        () =>
                        {
                            cameraInvertY = !cameraInvertY;
                            followCamera?.ApplyLookSettings(cameraMouseSensitivity, cameraInvertY);
                        },
                        uiScale,
                        "YES",
                        "NO");

                    DrawSliderRow(
                        "Camera Distance",
                        cameraDistance,
                        2.0f,
                        10.0f,
                        v =>
                        {
                            cameraDistance = v;
                            followCamera?.ApplyCameraDistance(cameraDistance);
                        },
                        v => $"{v:F1}m",
                        uiScale);

                    DrawCameraFeelRow(
                        uiScale,
                        cameraFeelMode,
                        newMode =>
                        {
                            cameraFeelMode = newMode;
                            ApplyCameraFeel();
                        });
                });

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Gameplay ───────────────────────────────────────────
                DrawSettingsSection("Gameplay", 4, uiScale, () =>
                {
                    DrawToggleRow(
                        "Pause Game (Journal)",
                        pauseGameWhenJournalOpen,
                        () =>
                        {
                            pauseGameWhenJournalOpen = !pauseGameWhenJournalOpen;

                            // Apply immediately if the journal is already open.
                            if (inGameMenuOpen)
                            {
                                if (pauseGameWhenJournalOpen)
                                {
                                    timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? timeScaleBeforePause : Time.timeScale;
                                    Time.timeScale = 0f;
                                }
                                else
                                {
                                    Time.timeScale = timeScaleBeforePause;
                                }
                            }
                        },
                        uiScale);

                    DrawToggleRow(
                        "DEV Build Mode",
                        devBuildMode,
                        () => devBuildMode = !devBuildMode,
                        uiScale,
                        "DEV",
                        "RES");

                    // Not implemented as automatic behavior in the current codebase;
                    // shown to keep the Settings panel complete.
                    DrawInfoRow("Auto-equip on pickup", "Manual equip only", uiScale);
                    DrawInfoRow("Interact mode", "Uses input action config", uiScale);
                });
            }, "Session-level options");

            // ── Preview (Right column) ─────────────────────────────────
            DrawJournalPanel(previewRect, "Preview", () =>
            {
                GUILayout.Label("Live session preview", journalSubtitleStyle);
                GUILayout.Space(Mathf.RoundToInt(8f * uiScale));

                float hudCardHeight = Mathf.Clamp(240f * uiScale, 200f, 360f);
                float sessionCardHeight = Mathf.Clamp(150f * uiScale, 120f, 260f);

                Rect hudCardRect = GUILayoutUtility.GetRect(
                    Mathf.RoundToInt(10f * uiScale),
                    hudCardHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(hudCardHeight));

                DrawBorderPanel(hudCardRect, journalCardTexture, 1f);

                Rect hudInner = new Rect(
                    hudCardRect.x + 10f * uiScale,
                    hudCardRect.y + 10f * uiScale,
                    hudCardRect.width - (20f * uiScale),
                    hudCardRect.height - (20f * uiScale));

                GUI.Label(new Rect(hudInner.x, hudInner.y, hudInner.width, 18f * uiScale), "HUD Preview", headingStyle);
                GUI.DrawTexture(new Rect(hudInner.x, hudInner.y + 22f * uiScale, hudInner.width, 2f * uiScale), amberAccentTexture);

                float textTop = hudInner.y + 34f * uiScale;
                GUI.Label(new Rect(hudInner.x, textTop, hudInner.width, 16f * uiScale),
                    $"Resolution: {Screen.width}x{Screen.height}  |  Fullscreen: {(Screen.fullScreen ? "ON" : "OFF")}",
                    journalSubtitleStyle);

                string hudFlags = $"{(showCrosshair ? "Crosshair" : "No Crosshair")}  •  {(showActionHints ? "Hints" : "No Hints")}  •  {(showFps ? "FPS On" : "FPS Off")}";
                GUI.Label(new Rect(hudInner.x, textTop + 20f * uiScale, hudInner.width, 16f * uiScale),
                    hudFlags,
                    smallMutedStyle);

                string cameraProfile =
                    $"Camera: {cameraFeelMode}  |  Smoothing {cameraSmoothingMultiplier:F2}  |  Sens {cameraMouseSensitivity:F2}  |  Dist {cameraDistance:F1}m  |  InvertY {(cameraInvertY ? "ON" : "OFF")}";
                GUI.Label(new Rect(hudInner.x, textTop + 38f * uiScale, hudInner.width, 16f * uiScale),
                    cameraProfile,
                    smallMutedStyle);

                Rect uiBarRect = new Rect(
                    hudInner.x,
                    textTop + 58f * uiScale,
                    hudInner.width,
                    Mathf.RoundToInt(14f * uiScale));
                DrawStatBar(
                    uiBarRect,
                    Mathf.Clamp01((currentUiScale * settingsUiScaleMultiplier) / 1.5f),
                    skillBarFillTexture,
                    "UI",
                    $"{(currentUiScale * settingsUiScaleMultiplier):F2}x");

                Rect miniHudRect = new Rect(
                    hudInner.x,
                    hudInner.yMax - (uiScale * 64f),
                    hudInner.width,
                    uiScale * 64f);
                DrawMiniSettingsHudPreview(miniHudRect, uiScale);

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                Rect sessionCardRect = GUILayoutUtility.GetRect(
                    Mathf.RoundToInt(10f * uiScale),
                    sessionCardHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(sessionCardHeight));

                DrawBorderPanel(sessionCardRect, journalCardTexture, 1f);

                Rect sessionInner = new Rect(
                    sessionCardRect.x + 10f * uiScale,
                    sessionCardRect.y + 10f * uiScale,
                    sessionCardRect.width - (20f * uiScale),
                    sessionCardRect.height - (20f * uiScale));

                GUI.Label(new Rect(sessionInner.x, sessionInner.y, sessionInner.width, 18f * uiScale), "Session Preview", headingStyle);
                GUI.DrawTexture(new Rect(sessionInner.x, sessionInner.y + 22f * uiScale, sessionInner.width, 2f * uiScale), amberAccentTexture);

                float sTop = sessionInner.y + 34f * uiScale;
                GUI.Label(new Rect(sessionInner.x, sTop, sessionInner.width, 16f * uiScale),
                    $"Journal Open: Always  |  Game Paused: {(pauseGameWhenJournalOpen ? "Yes" : "No")}",
                    journalSubtitleStyle);
                GUI.Label(new Rect(sessionInner.x, sTop + 20f * uiScale, sessionInner.width, 16f * uiScale),
                    "Cursor: Unlocked UI  •  Look: Fully Paused",
                    smallMutedStyle);
                GUI.Label(new Rect(sessionInner.x, sTop + 38f * uiScale, sessionInner.width, 16f * uiScale),
                    "Close with Esc / Tab (cursor locks + look resumes).",
                    smallMutedStyle);
            }, "Visual feedback");
        }

        private void DrawSettingsSection(string title, int rowCount, float uiScale, Action drawer)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            float headerHeight = Mathf.RoundToInt(50f * uiScale);
            float sectionHeight = (rowCount * rowHeight) + headerHeight;
            Rect sectionRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                sectionHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(sectionHeight));

            DrawBorderPanel(sectionRect, hudCardTexture, 1f);

            float padX = Mathf.RoundToInt(14f * uiScale);
            float padY = Mathf.RoundToInt(12f * uiScale);
            float titleH = Mathf.RoundToInt(20f * uiScale);
            float accentYOffset = titleH + Mathf.RoundToInt(6f * uiScale);
            float contentYOffset = accentYOffset + Mathf.RoundToInt(10f * uiScale);

            Rect innerRect = new Rect(
                sectionRect.x + padX,
                sectionRect.y + padY,
                sectionRect.width - (padX * 2f),
                sectionRect.height - (padY * 2f));

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, titleH), title, headingStyle);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + accentYOffset, innerRect.width, 2f), amberAccentTexture);

            Rect contentRect = new Rect(innerRect.x, innerRect.y + contentYOffset, innerRect.width, innerRect.height - contentYOffset);

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawMiniSettingsHudPreview(Rect rect, float uiScale)
        {
            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 8f * uiScale;

            Rect hudRect = new Rect(
                rect.x + padX,
                rect.y + padY,
                rect.width - (padX * 2f),
                rect.height - (padY * 2f));

            Rect hpRect = new Rect(hudRect.x, hudRect.y + 4f * uiScale, hudRect.width, 6f * uiScale);
            DrawStatBar(hpRect, 0.72f, statBarHpTexture, string.Empty, string.Empty);

            Rect subBarRect = new Rect(hudRect.x, hudRect.y + 12f * uiScale, hudRect.width * 0.56f, 4f * uiScale);
            GUI.DrawTexture(subBarRect, statBarBgTexture);
            GUI.DrawTexture(
                new Rect(
                    subBarRect.x + 1f * uiScale,
                    subBarRect.y + 1f * uiScale,
                    Mathf.Max(0f, subBarRect.width - 2f * uiScale) * 0.65f,
                    subBarRect.height - 2f * uiScale),
                statBarHungerTexture);

            Rect modeRect = new Rect(hudRect.x, hudRect.y + 20f * uiScale, 60f * uiScale, 12f * uiScale);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.22f);
            GUI.DrawTexture(modeRect, modeBadgeTexture);
            GUI.color = previousColor;

            GUI.Label(modeRect, devBuildMode ? "DEV" : "LIVE", smallMutedStyle);

            if (showCrosshair)
            {
                float cx = hudRect.xMax - 18f * uiScale;
                float cy = hudRect.y + (hudRect.height * 0.45f);

                GUI.DrawTexture(new Rect(cx - 6f * uiScale, cy - 1f * uiScale, 12f * uiScale, 2f * uiScale), amberAccentTexture);
                GUI.DrawTexture(new Rect(cx - 1f * uiScale, cy - 6f * uiScale, 2f * uiScale, 12f * uiScale), amberAccentTexture);
            }

            if (showActionHints)
            {
                float hintLabelW = 70f * uiScale;
                GUI.Label(
                    new Rect(hudRect.xMax - hintLabelW, hudRect.yMax - 18f * uiScale, hintLabelW, 14f * uiScale),
                    "HINTS",
                    smallMutedStyle);
                Rect hintLine = new Rect(hudRect.xMax - (40f * uiScale), hudRect.yMax - (8f * uiScale), 30f * uiScale, 2f * uiScale);
                GUI.DrawTexture(hintLine, amberAccentTexture);
            }

            if (showFps)
            {
                GUI.Label(new Rect(hudRect.x, hudRect.yMax - 18f * uiScale, 60f * uiScale, 14f * uiScale), "FPS", smallMutedStyle);
            }
        }

        private void DrawToggleRow(
            string label,
            bool isOn,
            Action onToggle,
            float uiScale,
            string onLabel = "ON",
            string offLabel = "OFF")
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float toggleWidth = Mathf.RoundToInt(74f * uiScale);
            float toggleHeight = Mathf.RoundToInt(26f * uiScale);
            float labelReserved = toggleWidth + 28f * uiScale;
            Rect labelRect = new Rect(innerRect.x, innerRect.y, innerRect.width - labelReserved, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect toggleRect = new Rect(
                innerRect.xMax - toggleWidth,
                innerRect.y + ((innerRect.height - toggleHeight) * 0.5f),
                toggleWidth,
                toggleHeight);

            Rect stateDotRect = new Rect(
                toggleRect.x - 14f * uiScale,
                innerRect.y + ((innerRect.height - 8f * uiScale) * 0.5f),
                8f * uiScale,
                8f * uiScale);

            Color previousColor = GUI.color;
            GUI.color = isOn
                ? new Color(0.82f, 0.64f, 0.24f, 0.95f)
                : new Color(0.30f, 0.32f, 0.28f, 0.9f);
            GUI.DrawTexture(stateDotRect, panelTexture);
            GUI.color = previousColor;

            GUIStyle toggleStyle = isOn ? toggleOnStyle : toggleOffStyle;
            string toggleLabel = isOn ? onLabel : offLabel;

            if (GUI.Button(toggleRect, toggleLabel, toggleStyle))
            {
                onToggle?.Invoke();
            }
        }

        private void DrawInfoRow(string label, string value, float uiScale)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = innerRect.width * 0.50f;
            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect valueRect = new Rect(innerRect.x + labelWidth, innerRect.y, innerRect.width - labelWidth, innerRect.height);
            GUI.Label(valueRect, value, journalBodyStyle);
        }

        private void DrawSliderRow(
            string label,
            float value,
            float min,
            float max,
            Action<float> onValueChanged,
            Func<float, string> valueText,
            float uiScale)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = Mathf.Clamp(innerRect.width * 0.46f, 140f * uiScale, 220f * uiScale);
            float valueWidth = Mathf.Clamp(88f * uiScale, 70f * uiScale, 120f * uiScale);
            float sliderLeft = innerRect.x + labelWidth + 10f * uiScale;
            float sliderWidth = innerRect.xMax - sliderLeft - valueWidth - (6f * uiScale);
            sliderWidth = Mathf.Max(40f * uiScale, sliderWidth);

            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect sliderRect = new Rect(
                sliderLeft,
                innerRect.y + ((innerRect.height - 10f * uiScale) * 0.5f),
                sliderWidth,
                10f * uiScale);

            float newValue = GUI.HorizontalSlider(sliderRect, value, min, max);
            Rect valueRect = new Rect(
                sliderRect.xMax + 6f * uiScale,
                innerRect.y,
                valueWidth,
                innerRect.height);

            GUI.Label(valueRect, valueText != null ? valueText(newValue) : newValue.ToString("F2"), journalBodyStyle);

            if (!Mathf.Approximately(newValue, value))
            {
                onValueChanged?.Invoke(newValue);
            }
        }

        private void DrawCameraFeelRow(
            float uiScale,
            CameraFeelMode selected,
            Action<CameraFeelMode> onChanged)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = Mathf.Clamp(innerRect.width * 0.33f, 120f * uiScale, 180f * uiScale);
            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, "Camera Feel", settingsRowLabelStyle);

            float buttonsX = innerRect.x + labelWidth + 6f * uiScale;
            Rect buttonsRect = new Rect(buttonsX, innerRect.y, innerRect.width - labelWidth - 6f * uiScale, innerRect.height);

            float gap = 6f * uiScale;
            float buttonWidth = (buttonsRect.width - (gap * 2f)) / 3f;

            CameraFeelMode[] modes = { CameraFeelMode.Calm, CameraFeelMode.Normal, CameraFeelMode.Responsive };
            string[] labels = { "CALM", "NORM", "RESP" };

            for (int i = 0; i < 3; i++)
            {
                bool isSelected = selected == modes[i];
                GUIStyle style = isSelected ? journalActiveTabStyle : journalTabStyle;

                Rect r = new Rect(
                    buttonsRect.x + (i * (buttonWidth + gap)),
                    innerRect.y + ((innerRect.height - 26f * uiScale) * 0.5f),
                    buttonWidth,
                    26f * uiScale);

                if (GUI.Button(r, labels[i], style))
                {
                    onChanged?.Invoke(modes[i]);
                }
            }
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
            GUILayout.Label("Materials", headingStyle);
            GUILayout.Space(Mathf.RoundToInt(8f * currentUiScale));

            float badgeWidth = Mathf.Clamp(90f * currentUiScale, 76f, 130f);
            float badgeHeight = Mathf.Clamp(30f * currentUiScale, 26f, 38f);

            GUILayout.BeginHorizontal();
            DrawResourceBadge($"Wood: {wood}", badgeWidth, badgeHeight);
            GUILayout.Space(6f);
            DrawResourceBadge($"Stone: {stone}", badgeWidth, badgeHeight);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            int storedFood = storedFoodInventory.Values.Sum();
            int storedWeapons = storedWeaponInventory.Values.Sum();

            GUILayout.BeginHorizontal();
            DrawResourceBadge($"Food: {storedFood}", badgeWidth, badgeHeight);
            GUILayout.Space(6f);
            DrawResourceBadge($"Arms: {storedWeapons}", badgeWidth, badgeHeight);
            GUILayout.EndHorizontal();
        }

        private void DrawMiniItemSlot(string label, string value)
        {
            float width = Mathf.Clamp(94f * currentUiScale, 78f, 140f);
            float height = Mathf.Clamp(72f * currentUiScale, 62f, 100f);
            bool isEmpty = string.Equals(value, "Empty", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "0", StringComparison.Ordinal);

            Rect slotRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            DrawBorderPanel(slotRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(slotRect.x + 8f, slotRect.y + 6f, slotRect.width - 16f, slotRect.height - 12f);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 16f), label, smallMutedStyle);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + 18f, innerRect.width, 1f), amberAccentTexture);

            string displayValue = isEmpty ? "---" : value;
            GUIStyle valueStyle = isEmpty ? smallMutedStyle : headingStyle;

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, 22f),
                displayValue,
                valueStyle);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 48f, innerRect.width, 14f),
                isEmpty ? "Empty" : "Stored",
                journalSubtitleStyle);
        }

        private List<JournalInventoryEntry> GetJournalInventoryEntries()
        {
            List<JournalInventoryEntry> entries = new();

            HashSet<string> favoriteIds = new();
            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                string favoriteId = favoriteHotbarItemIds[i];
                if (!string.IsNullOrWhiteSpace(favoriteId))
                {
                    favoriteIds.Add(favoriteId);
                }
            }

            if (wood > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "resource_wood",
                    "Wood",
                    wood,
                    "Resource",
                    false));
            }

            if (stone > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "resource_stone",
                    "Stone",
                    stone,
                    "Resource",
                    false));
            }

            if (food > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "carry_food",
                    "Food On Hand",
                    food,
                    "Food",
                    false));
            }

            foreach (KeyValuePair<string, int> pair in storedFoodInventory.OrderBy(k => k.Key))
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                string displayName = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                entries.Add(new JournalInventoryEntry(
                    pair.Key,
                    displayName,
                    pair.Value,
                    "Food",
                    favoriteIds.Contains(pair.Key)));
            }

            foreach (KeyValuePair<string, int> pair in storedWeaponInventory.OrderBy(k => k.Key))
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                string displayName = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                entries.Add(new JournalInventoryEntry(
                    pair.Key,
                    displayName,
                    pair.Value,
                    "Gear",
                    favoriteIds.Contains(pair.Key)));
            }

            return entries
                .OrderByDescending(e => e.isFavorited)
                .ThenBy(e => e.categoryLabel)
                .ThenBy(e => e.displayName)
                .ToList();
        }

        private JournalInventoryEntry? GetSelectedInventoryEntry(List<JournalInventoryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            // Priority 1: explicit inventory card selection
            if (!string.IsNullOrWhiteSpace(selectedInventoryItemId))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].itemId, selectedInventoryItemId, StringComparison.Ordinal))
                    {
                        return entries[i];
                    }
                }
            }

            // Priority 2: hotbar favorite selection
            string hotbarItemId = null;
            if (selectedHotbarIndex >= 0 && selectedHotbarIndex < favoriteHotbarItemIds.Length)
            {
                hotbarItemId = favoriteHotbarItemIds[selectedHotbarIndex];
            }

            if (!string.IsNullOrWhiteSpace(hotbarItemId))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].itemId, hotbarItemId, StringComparison.Ordinal))
                    {
                        return entries[i];
                    }
                }
            }

            // Priority 3: first favorited, then first entry
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].isFavorited)
                {
                    return entries[i];
                }
            }

            return entries[0];
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
                Rect emptyRect = GUILayoutUtility.GetRect(
                    10f, 120f,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(120f));

                DrawBorderPanel(emptyRect, journalCardTexture, 1f);

                Rect inner = new Rect(
                    emptyRect.x + 12f, emptyRect.y + 10f,
                    emptyRect.width - 24f, emptyRect.height - 20f);

                GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f),
                    "No item selected", headingStyle);
                GUI.DrawTexture(
                    new Rect(inner.x, inner.y + 24f, inner.width, 2f),
                    amberAccentTexture);
                GUI.Label(
                    new Rect(inner.x, inner.y + 34f, inner.width, 40f),
                    "Pick an inventory card to inspect its details.",
                    journalSubtitleStyle);
                return;
            }

            JournalInventoryEntry entry = selectedEntry.Value;

            bool isEquipment = catalogLookup.TryGetValue(
                entry.itemId, out BuildCatalogItem eqItem) &&
                GetEquipmentInfo(eqItem) != null;

            bool currentlyEquipped = IsItemEquipped(entry.itemId);

            float cardHeight = Mathf.Clamp(
                isEquipment ? 230f * currentUiScale : 164f * currentUiScale,
                isEquipment ? 200f : 148f,
                isEquipment ? 360f : 260f);

            Rect cardRect = GUILayoutUtility.GetRect(
                10f, cardHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(cardHeight));

            DrawBorderPanel(cardRect, journalCardTexture, 1f);

            Rect inner2 = new Rect(
                cardRect.x + 12f, cardRect.y + 10f,
                cardRect.width - 24f, cardRect.height - 20f);

            GUI.Label(
                new Rect(inner2.x, inner2.y, inner2.width - 28f, 20f),
                entry.displayName, headingStyle);

            if (currentlyEquipped)
            {
                GUI.Label(
                    new Rect(inner2.xMax - 60f, inner2.y, 60f, 16f),
                    "✦ Equip'd", questDoneStyle);
            }

            GUI.DrawTexture(
                new Rect(inner2.x, inner2.y + 24f, inner2.width, 2f),
                amberAccentTexture);

            GUI.Label(
                new Rect(inner2.x, inner2.y + 34f, inner2.width, 18f),
                entry.categoryLabel, smallMutedStyle);

            GUI.Label(
                new Rect(inner2.x, inner2.y + 56f, inner2.width, 20f),
                $"Quantity: x{entry.quantity}", labelStyle);

            if (isEquipment)
            {
                EquipmentInfo def = GetEquipmentInfo(eqItem);

                GUI.DrawTexture(
                    new Rect(inner2.x, inner2.y + 82f, inner2.width, 1f),
                    amberAccentTexture);

                string typeLine = "Item";

                if (def.isAmmo)
                    typeLine = $"Ammo  ·  {def.ammoType}";
                else if (def.isWeapon)
                    typeLine = $"Weapon  ·  {def.weaponType}";
                else if (def.isArmor)
                    typeLine = $"Armor  ·  {def.armorSlot}";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 90f, inner2.width, 16f),
                    typeLine, journalSubtitleStyle);

                string statsLine = string.Empty;

                if (def.isAmmo)
                {
                    string ammoLabel = def.ammoType == AmmoType.Arrows ? "Used by bows" : "Ammo item";
                    statsLine = $"Ammo Stack x{entry.quantity}   {ammoLabel}";
                }
                else if (def.stats != null)
                {
                    statsLine = def.isWeapon
                        ? $"Damage +{def.stats.damageBonus}   " +
                          $"Cooldown {def.stats.attackCooldown:F2}s   " +
                          $"Speed ×{def.stats.moveSpeedMultiplier:F2}"
                        : $"Block {Mathf.RoundToInt(def.stats.damageReduction * 100f)}%   " +
                          $"Speed ×{def.stats.moveSpeedMultiplier:F2}";
                }

                if (!string.IsNullOrWhiteSpace(statsLine))
                {
                    GUI.Label(
                        new Rect(inner2.x, inner2.y + 110f, inner2.width, 32f),
                        statsLine, journalBodyStyle);
                }

                string slotHint = currentlyEquipped
                    ? $"In slot: {GetEquippedSlotKey(entry.itemId)}"
                    : "Not equipped";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 148f, inner2.width, 16f),
                    slotHint, smallMutedStyle);
            }
            else
            {
                string infoText = entry.categoryLabel == "Food"
                    ? "Consumable or harvested supply."
                    : "Stored inventory item.";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 82f, inner2.width, 28f),
                    infoText, journalSubtitleStyle);
            }
        }

        private void DrawEquippedLoadout()
        {
            // ── Weapon row ────────────────────────────────────────────
            GUILayout.Label("Weapons", headingStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Weapon, GetWeaponSlotDisplayLabel());
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Shield, GetOffhandSlotDisplayLabel());
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Ammo, "Ammo");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);

            // ── Armor rows ────────────────────────────────────────────
            GUILayout.Label("Armor", headingStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Head, "Head");
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Chest, "Chest");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Hands, "Hands");
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Feet, "Feet");
            GUILayout.EndHorizontal();

            // ── Stat summary ──────────────────────────────────────────
            GUILayout.Space(Mathf.RoundToInt(14f * currentUiScale));
            float reduction = GetTotalDamageReduction();
            int dmgBonus = GetWeaponDamageBonus();
            float speed = GetEquipmentSpeedMultiplier();

            float statHeight = Mathf.Clamp(76f * currentUiScale, 66f, 120f);
            Rect statCardRect = GUILayoutUtility.GetRect(
                10f, statHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(statHeight));

            DrawBorderPanel(statCardRect, journalCardTexture, 1f);

            Rect inner = new Rect(
                statCardRect.x + 12f,
                statCardRect.y + 10f,
                statCardRect.width - 24f,
                statCardRect.height - 20f);

            GUI.Label(
                new Rect(inner.x, inner.y, inner.width, 18f),
                "Loadout Stats",
                headingStyle);

            GUI.Label(
                new Rect(inner.x, inner.y + 24f, inner.width, 22f),
                $"Damage Bonus: +{dmgBonus}     Reduction: {Mathf.RoundToInt(reduction * 100f)}%     Speed: {speed:F2}x",
                journalSubtitleStyle);
        }

        private void DrawEquipSlotWidget(string slotKey, string label)
        {
            float width = Mathf.Clamp(90f * currentUiScale, 76f, 160f);
            float height = Mathf.Clamp(78f * currentUiScale, 66f, 125f);

            bool hasItem = equippedSlots.TryGetValue(slotKey, out EquippedSlot slot)
                           && !slot.IsEmpty;

            Rect slotRect = GUILayoutUtility.GetRect(
                width, height,
                GUILayout.Width(width),
                GUILayout.Height(height));

            Color prev = GUI.color;

            GUI.color = hasItem
                ? new Color(0.145f, 0.176f, 0.078f, 0.95f)
                : new Color(0.067f, 0.078f, 0.043f, 0.90f);
            GUI.DrawTexture(slotRect, journalCardTexture);

            GUI.color = hasItem
                ? new Color(0.788f, 0.659f, 0.298f, 0.55f)
                : new Color(0.788f, 0.659f, 0.298f, 0.18f);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.y, slotRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.yMax - 1f, slotRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.y, 1f, slotRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.xMax - 1f, slotRect.y, 1f, slotRect.height), amberAccentTexture);

            GUI.color = prev;

            Rect inner = new Rect(
                slotRect.x + 6f,
                slotRect.y + 6f,
                slotRect.width - 12f,
                slotRect.height - 12f);

            GUI.Label(
                new Rect(inner.x, inner.y, inner.width, 14f),
                label, smallMutedStyle);

            if (hasItem && catalogLookup.TryGetValue(slot.catalogItemId, out BuildCatalogItem item))
            {
                GUI.Label(
                    new Rect(inner.x, inner.y + 16f, inner.width, 18f),
                    item.displayName, journalBodyStyle);

                string statLine = string.Empty;

                if (slot.ammoType != AmmoType.None)
                {
                    statLine = slot.ammoType == AmmoType.Arrows
                        ? "Arrows"
                        : "Ammo";
                }
                else if (slot.stats.damageBonus > 0)
                {
                    statLine = $"+{slot.stats.damageBonus} dmg";
                }
                else if (slot.stats.damageReduction > 0f)
                {
                    statLine = $"{Mathf.RoundToInt(slot.stats.damageReduction * 100f)}% block";
                }

                if (!string.IsNullOrWhiteSpace(statLine))
                {
                    GUI.Label(
                        new Rect(inner.x, inner.y + 36f, inner.width, 14f),
                        statLine, smallMutedStyle);
                }

                if (GUI.Button(
                    new Rect(inner.x, inner.yMax - 14f, inner.width, 14f),
                    "unequip", smallMutedStyle))
                {
                    UnequipSlot(slotKey);
                }
            }
            else
            {
                GUI.Label(
                    new Rect(inner.x, inner.y + 18f, inner.width, 16f),
                    "Empty", smallMutedStyle);
            }
        }

        private string GetWeaponSlotDisplayLabel()
        {
            if (!equippedSlots.TryGetValue(EquipSlotKey.Weapon, out EquippedSlot slot) || slot.IsEmpty)
                return "R.Hand";

            return slot.weaponType switch
            {
                WeaponType.Dagger => "Dagger",
                WeaponType.Sword => "Sword",
                WeaponType.Axe => "Axe",
                WeaponType.Hammer => "Hammer",
                WeaponType.Warhammer => "Warhmr",
                WeaponType.Spear => "Spear",
                WeaponType.Bow => "Bow",
                _ => "R.Hand"
            };
        }

        private string GetOffhandSlotDisplayLabel()
        {
            if (equippedSlots.TryGetValue(EquipSlotKey.Weapon, out EquippedSlot slot) && !slot.IsEmpty)
            {
                if (slot.weaponType == WeaponType.Bow)
                    return "Grip";
            }

            return "L.Hand";
        }

        private void DrawEquipButton(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue)
                return;

            JournalInventoryEntry entry = selectedEntry.Value;

            if (!catalogLookup.TryGetValue(entry.itemId, out BuildCatalogItem item))
                return;

            EquipmentInfo def = GetEquipmentInfo(item);

            if (def == null || (!def.isWeapon && !def.isArmor && !def.isAmmo))
                return;

            bool equipped = IsItemEquipped(entry.itemId);

            float btnHeight = Mathf.Clamp(38f * currentUiScale, 32f, 56f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnHeight));

            Color prev = GUI.color;

            GUI.color = equipped
                ? new Color(0.400f, 0.200f, 0.080f, 0.90f)
                : new Color(0.145f, 0.176f, 0.078f, 0.95f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.55f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.xMax - 1f, btnRect.y, 1f, btnRect.height), amberAccentTexture);

            GUI.color = prev;

            string itemVerbLabel = item.displayName;

            if (def.isAmmo && def.ammoType == AmmoType.Arrows)
                itemVerbLabel = "Arrows";
            else if (def.isWeapon && def.weaponType != WeaponType.None)
                itemVerbLabel = def.weaponType.ToString();
            else if (def.isArmor)
                itemVerbLabel = def.armorSlot.ToString();

            string btnLabel = equipped
                ? $"Unequip {itemVerbLabel}"
                : $"Equip {itemVerbLabel}";

            if (GUI.Button(btnRect, btnLabel, journalActiveTabStyle))
            {
                ToggleEquip(entry.itemId);
            }
        }

        private void DrawDropButton(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue || selectedEntry.Value.quantity <= 0)
                return;

            JournalInventoryEntry entry = selectedEntry.Value;

            float btnHeight = Mathf.Clamp(32f * currentUiScale, 28f, 48f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnHeight));

            Color prev = GUI.color;
            GUI.color = new Color(0.300f, 0.140f, 0.060f, 0.85f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.40f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.color = prev;

            if (GUI.Button(btnRect, $"Drop {entry.displayName} (X)", journalActiveTabStyle))
            {
                TryDropSelectedInventoryItem();
            }
        }

        private void HandleContainerStorageNavigation()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame)
            {
                CloseContainerStorage();
            }
        }

        private void DrawContainerStoragePanel()
        {
            if (!TryGetActiveContainer(out PlacedWorldObject placedObject, out ContainerRuntimeStorage containerStorage, out int capacity))
            {
                CloseContainerStorage();
                return;
            }

            float used = GetContainerUsedCapacity(containerStorage);
            string containerName = GetContainerDisplayName(placedObject);

            float shellMargin = Mathf.Clamp(18f * currentUiScale, 14f, 28f);
            Rect shellRect = new(
                shellMargin,
                shellMargin,
                Screen.width - (shellMargin * 2f),
                Screen.height - (shellMargin * 2f));

            DrawBorderPanel(shellRect, journalShellTexture, 2f);

            float pad = Mathf.RoundToInt(18f * currentUiScale);
            Rect inner = new(shellRect.x + pad, shellRect.y + pad, shellRect.width - (pad * 2f), shellRect.height - (pad * 2f));
            float titleH = Mathf.Clamp(30f * currentUiScale, 24f, 36f);

            GUI.Label(new Rect(inner.x, inner.y, inner.width, titleH), $"{containerName} Storage", journalTitleStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + titleH + 4f, inner.width, 2f), amberAccentTexture);
            GUI.Label(new Rect(inner.x, inner.y + titleH + 10f, inner.width, 18f * currentUiScale),
                $"Container ID: {placedObject.UniqueId}  |  Capacity: {used}/{capacity}",
                journalSubtitleStyle);

            float controlsTop = inner.y + titleH + 32f;
            float controlsH = Mathf.Clamp(34f * currentUiScale, 28f, 42f);
            Rect controlsRect = new(inner.x, controlsTop, inner.width, controlsH);
            DrawContainerTransferControls(controlsRect);

            float columnsTop = controlsRect.yMax + Mathf.RoundToInt(8f * currentUiScale);
            float columnsH = inner.yMax - columnsTop - Mathf.RoundToInt(40f * currentUiScale);
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float colWidth = (inner.width - gutter) * 0.5f;
            Rect playerRect = new(inner.x, columnsTop, colWidth, columnsH);
            Rect containerRect = new(playerRect.xMax + gutter, columnsTop, colWidth, columnsH);

            DrawContainerPlayerSide(playerRect, containerStorage, capacity);
            DrawContainerStoredSide(containerRect, containerStorage, capacity);

            Rect footerRect = new(inner.x, inner.yMax - Mathf.RoundToInt(30f * currentUiScale), inner.width, Mathf.RoundToInt(24f * currentUiScale));
            GUI.Label(footerRect, "Esc / Tab close", smallMutedStyle);
        }

        private void DrawContainerTransferControls(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transfer Amount", journalBodyStyle, GUILayout.Width(150f * currentUiScale));

            int[] values = { 1, 5, 10, 20 };
            for (int i = 0; i < values.Length; i++)
            {
                bool selected = containerTransferAmount == values[i];
                if (GUILayout.Button(values[i].ToString(), selected ? journalActiveTabStyle : journalTabStyle, GUILayout.Width(52f * currentUiScale)))
                {
                    containerTransferAmount = values[i];
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", journalTabStyle, GUILayout.Width(90f * currentUiScale)))
            {
                CloseContainerStorage();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawContainerPlayerSide(Rect rect, ContainerRuntimeStorage storage, int capacity)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);
            Rect inner = new(rect.x + 10f * currentUiScale, rect.y + 10f * currentUiScale, rect.width - 20f * currentUiScale, rect.height - 20f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f * currentUiScale), "Player Inventory", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + 22f * currentUiScale, inner.width, 1f), amberAccentTexture);

            Rect body = new(inner.x, inner.y + 28f * currentUiScale, inner.width, inner.height - 28f * currentUiScale);
            GUILayout.BeginArea(body);
            DrawContainerResourceRow("Wood", wood, storage.wood, () => DepositResourceToContainer(ResourceType.Wood, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Wood, storage));
            DrawContainerResourceRow("Stone", stone, storage.stone, () => DepositResourceToContainer(ResourceType.Stone, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Stone, storage));
            DrawContainerResourceRow("Food", food, storage.food, () => DepositResourceToContainer(ResourceType.Food, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Food, storage));

            GUILayout.Space(6f);
            GUI.DrawTexture(GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)), amberAccentTexture);
            GUILayout.Label("Item Stacks", smallMutedStyle);
            containerPlayerItemsScroll = GUILayout.BeginScrollView(containerPlayerItemsScroll, GUILayout.Height(Mathf.Max(80f, body.height - 148f * currentUiScale)));
            DrawContainerItemTransferRows(storedFoodInventory, storage.storedFoodItems, capacity, depositDirection: true);
            DrawContainerItemTransferRows(storedWeaponInventory, storage.storedWeapons, capacity, depositDirection: true);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawContainerStoredSide(Rect rect, ContainerRuntimeStorage storage, int capacity)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);
            Rect inner = new(rect.x + 10f * currentUiScale, rect.y + 10f * currentUiScale, rect.width - 20f * currentUiScale, rect.height - 20f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f * currentUiScale), "Container Contents", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + 22f * currentUiScale, inner.width, 1f), amberAccentTexture);

            int used = GetContainerUsedCapacity(storage);
            GUI.Label(new Rect(inner.x, inner.y + 26f * currentUiScale, inner.width, 18f * currentUiScale), $"Used: {used}/{capacity}", journalSubtitleStyle);

            Rect body = new(inner.x, inner.y + 46f * currentUiScale, inner.width, inner.height - 46f * currentUiScale);
            GUILayout.BeginArea(body);
            GUILayout.Label($"Wood: {storage.wood}", journalBodyStyle);
            GUILayout.Label($"Stone: {storage.stone}", journalBodyStyle);
            GUILayout.Label($"Food: {storage.food}", journalBodyStyle);

            GUILayout.Space(6f);
            GUI.DrawTexture(GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)), amberAccentTexture);
            GUILayout.Label("Stored Item Stacks", smallMutedStyle);
            containerStoredItemsScroll = GUILayout.BeginScrollView(containerStoredItemsScroll, GUILayout.Height(Mathf.Max(90f, body.height - 112f * currentUiScale)));
            DrawContainerItemTransferRows(storage.storedFoodItems, storedFoodInventory, capacity, depositDirection: false);
            DrawContainerItemTransferRows(storage.storedWeapons, storedWeaponInventory, capacity, depositDirection: false);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawContainerResourceRow(string label, int carried, int stored, Action onDeposit, Action onWithdraw)
        {
            float btnH = Mathf.Clamp(26f * currentUiScale, 22f, 34f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {carried} / {stored}", journalBodyStyle, GUILayout.Width(150f * currentUiScale));
            if (GUILayout.Button("Deposit", journalTabStyle, GUILayout.Height(btnH), GUILayout.Width(80f * currentUiScale)) && carried > 0)
            {
                onDeposit?.Invoke();
            }

            if (GUILayout.Button("Take", journalTabStyle, GUILayout.Height(btnH), GUILayout.Width(70f * currentUiScale)) && stored > 0)
            {
                onWithdraw?.Invoke();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawContainerItemTransferRows(Dictionary<string, int> sourceInventory, Dictionary<string, int> targetInventory, int capacity, bool depositDirection)
        {
            foreach (KeyValuePair<string, int> pair in sourceInventory.OrderBy(k => k.Key).ToList())
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{GetDisplayNameForItemId(pair.Key)} x{pair.Value}", smallMutedStyle, GUILayout.Width(190f * currentUiScale));
                string buttonLabel = depositDirection ? "Deposit" : "Take";
                if (GUILayout.Button(buttonLabel, journalTabStyle, GUILayout.Width(80f * currentUiScale)))
                {
                    if (depositDirection)
                    {
                        DepositItemToContainer(pair.Key, sourceInventory, targetInventory, capacity);
                    }
                    else
                    {
                        WithdrawItemFromContainer(pair.Key, sourceInventory, targetInventory);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DepositResourceToContainer(ResourceType resourceType, ContainerRuntimeStorage containerStorage, int capacity)
        {
            if (containerStorage == null)
            {
                return;
            }

            int free = Mathf.Max(0, capacity - GetContainerUsedCapacity(containerStorage));
            if (free <= 0)
            {
                SetStatusMessage("Container is full.");
                return;
            }

            int requested = Mathf.Max(1, containerTransferAmount);
            int moved = 0;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    moved = Mathf.Min(requested, Mathf.Min(free, wood));
                    wood -= moved;
                    containerStorage.wood += moved;
                    break;
                case ResourceType.Stone:
                    moved = Mathf.Min(requested, Mathf.Min(free, stone));
                    stone -= moved;
                    containerStorage.stone += moved;
                    break;
                case ResourceType.Food:
                    moved = Mathf.Min(requested, Mathf.Min(free, food));
                    food -= moved;
                    containerStorage.food += moved;
                    break;
            }

            if (moved > 0)
            {
                SetStatusMessage($"Deposited {moved} {resourceType.ToString().ToLowerInvariant()}.");
                SaveWorld();
            }
        }

        private void WithdrawResourceFromContainer(ResourceType resourceType, ContainerRuntimeStorage containerStorage)
        {
            if (containerStorage == null)
            {
                return;
            }

            int requested = Mathf.Max(1, containerTransferAmount);
            int moved = 0;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    moved = Mathf.Min(requested, containerStorage.wood);
                    containerStorage.wood -= moved;
                    wood += moved;
                    break;
                case ResourceType.Stone:
                    moved = Mathf.Min(requested, containerStorage.stone);
                    containerStorage.stone -= moved;
                    stone += moved;
                    break;
                case ResourceType.Food:
                    moved = Mathf.Min(requested, containerStorage.food);
                    containerStorage.food -= moved;
                    food += moved;
                    break;
            }

            if (moved > 0)
            {
                SetStatusMessage($"Withdrew {moved} {resourceType.ToString().ToLowerInvariant()}.");
                SaveWorld();
            }
        }

        private void DepositItemToContainer(string itemId, Dictionary<string, int> sourceInventory, Dictionary<string, int> containerInventory, int capacity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || sourceInventory == null || containerInventory == null)
            {
                return;
            }

            if (!sourceInventory.TryGetValue(itemId, out int available) || available <= 0)
            {
                return;
            }

            if (!TryGetActiveContainer(out _, out ContainerRuntimeStorage storage, out _))
            {
                return;
            }

            int free = Mathf.Max(0, capacity - GetContainerUsedCapacity(storage));
            if (free <= 0)
            {
                SetStatusMessage("Container is full.");
                return;
            }

            int move = Mathf.Min(available, Mathf.Min(containerTransferAmount, free));
            if (move <= 0)
            {
                return;
            }

            RemoveInventoryCount(sourceInventory, itemId, move);
            AddInventoryCount(containerInventory, itemId, move);
            SetStatusMessage($"Deposited {move}x {GetDisplayNameForItemId(itemId)}.");
            SaveWorld();
        }

        private void WithdrawItemFromContainer(string itemId, Dictionary<string, int> containerInventory, Dictionary<string, int> targetInventory)
        {
            if (string.IsNullOrWhiteSpace(itemId) || containerInventory == null || targetInventory == null)
            {
                return;
            }

            if (!containerInventory.TryGetValue(itemId, out int available) || available <= 0)
            {
                return;
            }

            int move = Mathf.Min(available, Mathf.Max(1, containerTransferAmount));
            RemoveInventoryCount(containerInventory, itemId, move);
            AddInventoryCount(targetInventory, itemId, move);
            SetStatusMessage($"Withdrew {move}x {GetDisplayNameForItemId(itemId)}.");
            SaveWorld();
        }

        private ContainerStorageSaveData CaptureContainerStorage(PlacedWorldObject placedObject)
        {
            if (placedObject == null || !TryGetContainerDefinition(placedObject, out _, out _))
            {
                return null;
            }

            ContainerRuntimeStorage runtime = GetOrCreateContainerStorage(placedObject.UniqueId);
            return new ContainerStorageSaveData
            {
                wood = Mathf.Max(0, runtime.wood),
                stone = Mathf.Max(0, runtime.stone),
                food = Mathf.Max(0, runtime.food),
                storedFoodItems = runtime.storedFoodItems
                    .Where(entry => entry.Value > 0)
                    .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                    .ToList(),
                storedWeapons = runtime.storedWeapons
                    .Where(entry => entry.Value > 0)
                    .Select(entry => new InventoryEntryData { itemId = entry.Key, count = entry.Value })
                    .ToList()
            };
        }

        private void RestoreContainerStorage(ContainerRuntimeStorage runtimeStorage, ContainerStorageSaveData saveData)
        {
            if (runtimeStorage == null)
            {
                return;
            }

            runtimeStorage.wood = Mathf.Max(0, saveData?.wood ?? 0);
            runtimeStorage.stone = Mathf.Max(0, saveData?.stone ?? 0);
            runtimeStorage.food = Mathf.Max(0, saveData?.food ?? 0);
            RestoreInventory(runtimeStorage.storedFoodItems, saveData?.storedFoodItems);
            RestoreInventory(runtimeStorage.storedWeapons, saveData?.storedWeapons);
        }

        private void DrawCraftingPanel()
        {
            if (craftingRecipes == null || craftingRecipes.Count == 0)
            {
                GUILayout.Label("No recipes available.", journalSubtitleStyle);
                return;
            }

            selectedRecipeIndex = Mathf.Clamp(selectedRecipeIndex, 0, craftingRecipes.Count - 1);
            CraftingRecipe selected = craftingRecipes[selectedRecipeIndex];
            bool canCraft = CanAffordRecipe(selected);

            // ── Recipe list (scrollable) ──────────────────────────────
            GUILayout.Label("Recipes", headingStyle);
            GUILayout.Space(4f);

            float recipeRowH = Mathf.Clamp(30f * currentUiScale, 26f, 42f);
            float listHeight = Mathf.Min(craftingRecipes.Count * (recipeRowH + 2f), 200f * currentUiScale);

            craftingScrollPos = GUILayout.BeginScrollView(
                craftingScrollPos,
                false, false,
                GUILayout.Height(listHeight));

            for (int i = 0; i < craftingRecipes.Count; i++)
            {
                CraftingRecipe recipe = craftingRecipes[i];
                bool affordable = CanAffordRecipe(recipe);
                bool isSelected = i == selectedRecipeIndex;

                Rect rowRect = GUILayoutUtility.GetRect(
                    10f, recipeRowH,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(recipeRowH));

                Color prev = GUI.color;

                if (isSelected)
                {
                    GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                    GUI.DrawTexture(rowRect, modeBadgeTexture);
                }

                GUI.color = affordable
                    ? new Color(0.85f, 0.75f, 0.55f, 1f)
                    : new Color(0.50f, 0.45f, 0.40f, 0.70f);

                string marker = isSelected ? "▸ " : "  ";
                string affordTag = affordable ? "" : "  [need materials]";
                GUI.Label(
                    new Rect(rowRect.x + 6f, rowRect.y, rowRect.width - 12f, rowRect.height),
                    $"{marker}{recipe.displayName}{affordTag}",
                    journalBodyStyle);

                GUI.color = prev;

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    rowRect.Contains(Event.current.mousePosition))
                {
                    selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();

            // ── Selected recipe details ───────────────────────────────
            GUILayout.Space(8f);
            GUI.DrawTexture(
                GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)),
                amberAccentTexture);
            GUILayout.Space(6f);

            GUILayout.Label(selected.displayName, headingStyle);
            GUILayout.Space(2f);
            GUILayout.Label(selected.description, journalSubtitleStyle);
            GUILayout.Space(6f);

            string costText = GetRecipeCostDisplay(selected);
            GUILayout.Label($"Cost: {costText}", canCraft ? journalBodyStyle : smallMutedStyle);

            string resultName = GetDisplayNameForItemId(selected.resultItemId);
            string resultType = selected.resultIsStructure ? "Structure (build menu)" : "Item (inventory)";
            GUILayout.Label($"Result: {selected.resultCount}x {resultName}  ({resultType})", journalBodyStyle);

            // Resource summary
            GUILayout.Space(4f);
            GUILayout.Label(
                $"Available: {wood} wood  ·  {stone} stone  ·  {food} food",
                smallMutedStyle);

            // ── Craft button ──────────────────────────────────────────
            GUILayout.Space(8f);
            float btnH = Mathf.Clamp(38f * currentUiScale, 32f, 52f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnH,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnH));

            Color prevBtn = GUI.color;
            GUI.color = canCraft
                ? new Color(0.145f, 0.176f, 0.078f, 0.95f)
                : new Color(0.10f, 0.10f, 0.10f, 0.50f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);

            GUI.color = canCraft
                ? new Color(0.788f, 0.659f, 0.298f, 0.55f)
                : new Color(0.40f, 0.35f, 0.30f, 0.30f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.xMax - 1f, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.color = prevBtn;

            string craftLabel = canCraft ? $"Craft {selected.displayName}" : "Insufficient Materials";

            if (GUI.Button(btnRect, craftLabel, journalActiveTabStyle) && canCraft)
            {
                CraftRecipe(selected);
            }
        }

        private string GetInventoryEntryDescription(JournalInventoryEntry entry)
        {
            return entry.categoryLabel switch
            {
                "Food" => "Food items can be consumed in the field for recovery. Use Storage to deposit extras.",
                "Gear" => "Weapons and armor stored in your inventory.",
                "Resource" => "Raw material carried on your person.",
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
            const int maxTier = 5;

            tier = Mathf.Clamp(tier, 0, maxTier);

            float cardHeight = Mathf.Clamp(98f * currentUiScale, 84f, 130f);
            Rect cardRect = GUILayoutUtility.GetRect(10f, cardHeight, GUILayout.ExpandWidth(true), GUILayout.Height(cardHeight));

            DrawBorderPanel(cardRect, journalCardTexture, 1f);

            Rect innerRect = new Rect(cardRect.x + 10f, cardRect.y + 8f, cardRect.width - 20f, cardRect.height - 16f);

            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            Rect titleRect = new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, 20f);
            GUI.Label(titleRect, $"{title}  |  Tier {tier}/{maxTier}", headingStyle);

            Rect descRect = new Rect(innerRect.x, innerRect.y + 30f, innerRect.width, 18f);
            GUI.Label(descRect, description, journalSubtitleStyle);

            float barHeight = Mathf.Clamp(10f * currentUiScale, 8f, 13f);
            Rect barRect = new Rect(innerRect.x, innerRect.y + 54f, innerRect.width, barHeight);
            GUI.DrawTexture(barRect, skillBarBgTexture);

            float segmentGap = 2f;
            float segmentWidth = (barRect.width - (segmentGap * (maxTier - 1))) / maxTier;

            for (int i = 0; i < maxTier; i++)
            {
                Rect segmentRect = new Rect(
                    barRect.x + (i * (segmentWidth + segmentGap)),
                    barRect.y + 1f,
                    segmentWidth,
                    barRect.height - 2f);

                if (i < tier)
                {
                    GUI.DrawTexture(segmentRect, skillBarFillTexture);
                }
                else
                {
                    Color previousColor = GUI.color;
                    GUI.color = new Color(0.18f, 0.18f, 0.16f, 0.75f);
                    GUI.DrawTexture(segmentRect, skillBarBgTexture);
                    GUI.color = previousColor;
                }
            }

            Rect footerRect = new Rect(innerRect.x, innerRect.y + 68f, innerRect.width, 16f);
            GUI.Label(footerRect, $"Progress: {tier}/{maxTier} tiers unlocked", smallMutedStyle);
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
                GUILayout.Space(2f);
                GUILayout.Label("Plant crop seeds to start countdowns.", journalSubtitleStyle);
                return;
            }

            GUILayout.Label("Active Growth", headingStyle);
            GUILayout.Space(6f);

            float barHeight = Mathf.Clamp(11f * currentUiScale, 9f, 14f);

            for (int i = 0; i < entries.Count; i++)
            {
                TimerDisplayEntry entry = entries[i];
                DrawStatBarRow(
                    entry.name,
                    entry.progress,
                    statBarStaminaTexture,
                    $"{entry.timeLabel}  |  {entry.distanceLabel}",
                    barHeight);

                if (i < entries.Count - 1)
                {
                    GUILayout.Space(4f);
                }
            }
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
                if (placedObject == null || !placedObject.PlacedByPlayer || renewableNode == null || renewableNode.IsHarvestable)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    continue;
                }

                if (item.renewableVisualMode != RenewableVisualMode.Crop)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                float remaining = Mathf.Max(0f, renewableNode.RemainingRegrowSeconds);

                float assumedTotalGrow = Mathf.Max(remaining, 60f);
                float elapsed = Mathf.Max(0f, assumedTotalGrow - remaining);
                float progress = Mathf.Clamp01(elapsed / assumedTotalGrow);

                entries.Add((distance, new TimerDisplayEntry
                {
                    name = item.displayName,
                    progress = progress,
                    timeLabel = FormatDurationShort(remaining),
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
                RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                if (renewableNode == null)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                string regrowState = FormatDurationShort(renewableNode.RemainingRegrowSeconds);
                entries.Add((distance, $"{item.displayName}  |  {regrowState}  |  {Mathf.RoundToInt(distance)}m"));
            }

            return entries
                .OrderBy(entry => entry.distance)
                .Take(6)
                .Select(entry => entry.text)
                .ToList();
        }

        private void DrawSocialEntries()
        {
            GUILayout.Label("No settlers discovered yet.", journalBodyStyle);
            GUILayout.Label("Villagers, factions, and social links will appear here.", journalSubtitleStyle);

            GUILayout.Space(10f);
            GUILayout.Label("Settlement", headingStyle);
            GUILayout.Space(6f);

            float barHeight = Mathf.Clamp(11f * currentUiScale, 9f, 14f);
            int structures = CountPlacedObjects(ItemKind.Structure);
            int harvestables = CountHarvestableRenewables();

            DrawStatBarRow(
                "Structures",
                Mathf.Clamp01(structures / 10f),
                skillBarFillTexture,
                $"{structures}",
                barHeight);

            GUILayout.Space(4f);

            DrawStatBarRow(
                "Harvestable",
                Mathf.Clamp01(harvestables / 8f),
                statBarStaminaTexture,
                $"{harvestables}",
                barHeight);

            GUILayout.Space(8f);
            GUI.DrawTexture(
                GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)),
                amberAccentTexture);
            GUILayout.Space(6f);

            GUILayout.Label("Tracking", headingStyle);
            GUILayout.Space(2f);
            GUILayout.Label(GetTrackedTargetSummary(), journalBodyStyle);
            GUILayout.Label("Nearby targets and future settlers will surface here.", journalSubtitleStyle);
        }

        private void DrawFavoritesEntries()
        {
            bool hasAnyFavorite = false;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!string.IsNullOrWhiteSpace(favoriteHotbarItemIds[i]))
                {
                    hasAnyFavorite = true;
                    break;
                }
            }

            if (!hasAnyFavorite)
            {
                GUILayout.Label("No favorites pinned yet.", journalBodyStyle);
                GUILayout.Space(2f);
                GUILayout.Label("Use the build menu or hotbar favorite flow to pin pieces here.", journalSubtitleStyle);
                return;
            }

            GUILayout.Label("Pinned Hotbar", headingStyle);
            GUILayout.Space(6f);

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

                if (row < rows - 1)
                {
                    GUILayout.Space(6f);
                }
            }
        }

        private void DrawFavoriteEntry(int slotIndex)
        {
            float rowHeight = Mathf.Clamp(54f * currentUiScale, 46f, 68f);
            Rect rowRect = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, journalCardTexture, 1f);

            Rect innerRect = new Rect(rowRect.x + 8f, rowRect.y + 7f, rowRect.width - 16f, rowRect.height - 14f);

            string itemId = favoriteHotbarItemIds[slotIndex];
            bool isSelectedSlot = slotIndex == selectedHotbarIndex;

            Rect slotBadgeRect = new Rect(innerRect.xMax - 34f, innerRect.y + 2f, 34f, 16f);
            if (isSelectedSlot)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(new Rect(rowRect.x + 1f, rowRect.y + 1f, rowRect.width - 2f, rowRect.height - 2f), modeBadgeTexture);
                GUI.color = previousColor;
            }

            GUI.Label(slotBadgeRect, GetHotbarKeyLabel(slotIndex), smallMutedStyle);

            if (string.IsNullOrWhiteSpace(itemId) || !catalogLookup.TryGetValue(itemId, out BuildCatalogItem item))
            {
                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 40f, 18f), "Empty Slot", headingStyle);
                GUI.Label(
                    new Rect(innerRect.x, innerRect.y + 20f, innerRect.width - 40f, 16f),
                    "No build piece pinned",
                    journalSubtitleStyle);
                return;
            }

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 40f, 18f), item.displayName, headingStyle);
            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 20f, innerRect.width - 40f, 16f),
                $"{item.category}  |  {GetItemKindLabel(item.kind)}",
                journalSubtitleStyle);

            if (isSelectedSlot)
            {
                GUI.Label(
                    new Rect(innerRect.x, innerRect.y + 34f, innerRect.width - 40f, 14f),
                    "Selected on hotbar",
                    smallMutedStyle);
            }
        }

        private int CountFavoritedSlots()
        {
            int count = 0;

            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(favoriteHotbarItemIds[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private string GetTrackedTargetSummary()
        {
            if (!TryGetTrackedTarget(out _, out BuildCatalogItem trackedItem, out float distance) || trackedItem == null)
            {
                return "No nearby target";
            }

            string typeLabel = GetItemKindLabel(trackedItem.kind);
            return $"{trackedItem.displayName}  |  {typeLabel}  |  {Mathf.RoundToInt(distance)}m";
        }

        private string FormatDurationShort(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));

            if (totalSeconds < 60)
            {
                return $"{totalSeconds}s";
            }

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
            {
                return secs > 0
                    ? $"{hours}h {minutes}m {secs}s"
                    : $"{hours}h {minutes}m";
            }

            return secs > 0
                ? $"{minutes}m {secs}s"
                : $"{minutes}m";
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
            trackedDistance = 0f;

            if (playerTransform == null || placedObjects == null || placedObjects.Count == 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                if (placedObject == null)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item) || item == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    trackedObject = placedObject;
                    trackedItem = item;
                    trackedDistance = distance;
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
            int count = 0;

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                if (placedObject == null)
                {
                    continue;
                }

                RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                if (renewableNode != null && renewableNode.IsHarvestable)
                {
                    count++;
                }
            }

            return count;
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

            float baseFollowSmoothTime;
            float baseRotationSmoothSpeed;

            switch (cameraFeelMode)
            {
                case CameraFeelMode.Calm:
                    baseFollowSmoothTime = 0.05f;
                    baseRotationSmoothSpeed = 24f;
                    break;

                case CameraFeelMode.Normal:
                    baseFollowSmoothTime = 0.035f;
                    baseRotationSmoothSpeed = 30f;
                    break;

                case CameraFeelMode.Responsive:
                    baseFollowSmoothTime = 0.025f;
                    baseRotationSmoothSpeed = 36f;
                    break;

                default:
                    baseFollowSmoothTime = 0.035f;
                    baseRotationSmoothSpeed = 30f;
                    break;
            }

            float smoothing = Mathf.Clamp(cameraSmoothingMultiplier, 0.6f, 1.6f);
            followCamera.ApplyRuntimeTuning(
                baseFollowSmoothTime * smoothing,
                baseRotationSmoothSpeed / smoothing);
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

            if (placementActive && plantingMode)
            {
                string seedName = GetDisplayNameForItemId(plantingSeedId);
                storedFoodInventory.TryGetValue(plantingSeedId, out int seedsLeft);
                string validity = placementHasValidTarget ? "Ready to plant." : "Move away from the player.";
                return $"Planting {seedName}  |  {seedsLeft} remaining\n{validity}  LMB/Enter plant  R rotate  Esc/RMB cancel";
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
                return $"{selectedItem.displayName}  |  {buildCost}\n{validity}  LMB/Enter place  R rotate  G snap  X delete aimed  Esc/RMB cancel";
            }

            if (deleteMode)
            {
                if (targetedPlacedObject != null && catalogLookup.TryGetValue(targetedPlacedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    return $"Delete {item.displayName}  |  Refund: {item.cost.ToDisplayString()}\nLMB or X to remove  |  Esc/RMB exit delete mode";
                }

                return "Delete Tool active.\nAim at a placed object and press LMB or X to remove it.";
            }

            if (targetedPickup != null)
            {
                float holdPercent = GetInteractHoldPercent();
                string progressText = holdPercent > 0f ? $" ({Mathf.RoundToInt(holdPercent * 100f)}%)" : string.Empty;
                return $"Hold Interact to {targetedPickup.GetInteractionLabel()}{progressText}";
            }

            if (targetedPlacedObject != null && TryGetContainerDefinition(targetedPlacedObject, out string containerLabel, out int capacity))
            {
                ContainerRuntimeStorage storage = GetOrCreateContainerStorage(targetedPlacedObject.UniqueId);
                int used = GetContainerUsedCapacity(storage);
                return $"Press Interact to open {containerLabel} storage ({used}/{capacity})";
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

            if (placementActive && plantingMode)
            {
                yield return "Planting mode";
                yield return "Enter/LMB plant | R rotate | Esc cancel";
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
            yield return "B build | X delete | H heal | P plant";
            yield return "Wheel or 1-0 selects hotbar";
        }

        private void DrawCategoryButton(BuildCategory category)
        {
            bool isSelected = selectedCategory == category;
            string label = category.ToString();

            float buttonHeight = Mathf.Clamp(36f * currentUiScale, 30f, 48f);
            float minWidth = Mathf.Clamp(96f * currentUiScale, 86f, 140f);

            Rect buttonRect = GUILayoutUtility.GetRect(
                minWidth,
                buttonHeight,
                GUILayout.MinWidth(minWidth),
                GUILayout.Height(buttonHeight));

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y + 1f, buttonRect.width, buttonRect.height - 2f), modeBadgeTexture);
                GUI.color = previousColor;
            }

            GUIStyle style = isSelected ? journalActiveTabStyle : journalTabStyle;
            if (GUI.Button(buttonRect, label, style))
            {
                selectedCategory = category;
                selectedIndex = 0;
            }

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.95f);
                GUI.DrawTexture(new Rect(buttonRect.x + 10f, buttonRect.yMax - 2f, buttonRect.width - 20f, 2f), amberAccentTexture);
                GUI.color = previousColor;
            }

            GUILayout.Space(6f);
        }

        private void DrawResourceBadge(string text, float width, float height)
        {
            Rect badgeRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            DrawBorderPanel(badgeRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(badgeRect.x + 8f, badgeRect.y + 4f, badgeRect.width - 16f, badgeRect.height - 8f);

            if (text.Contains(":"))
            {
                string[] parts = text.Split(':');
                string left = parts.Length > 0 ? parts[0].Trim() : text;
                string right = parts.Length > 1 ? string.Join(":", parts.Skip(1)).Trim() : string.Empty;

                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width * 0.52f, innerRect.height), left, smallMutedStyle);
                GUI.Label(new Rect(innerRect.x + (innerRect.width * 0.5f), innerRect.y, innerRect.width * 0.5f, innerRect.height), right, labelStyle);
            }
            else
            {
                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, innerRect.height), text, resourceBadgeStyle);
            }
        }

        private void DrawStatBar(Rect rect, float fraction, Texture2D fillTexture, string label, string valueText)
        {
            fraction = Mathf.Clamp01(fraction);

            GUI.DrawTexture(rect, statBarBgTexture);

            Rect innerRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            float fillWidth = innerRect.width * fraction;

            if (fillWidth > 0f)
            {
                Texture2D textureToUse = fillTexture != null ? fillTexture : statBarHpTexture;
                GUI.DrawTexture(new Rect(innerRect.x, innerRect.y, fillWidth, innerRect.height), textureToUse);
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                GUI.Label(
                    new Rect(rect.x + 6f, rect.y - 1f, rect.width * 0.5f, rect.height + 2f),
                    label,
                    smallMutedStyle);
            }

            if (!string.IsNullOrWhiteSpace(valueText))
            {
                GUI.Label(
                    new Rect(rect.x + (rect.width * 0.45f), rect.y - 1f, rect.width * 0.5f - 6f, rect.height + 2f),
                    valueText,
                    labelStyle);
            }
        }

        private void DrawStatBarRow(string label, float fraction, Texture2D fillTexture, string valueText, float barHeight)
        {
            float rowHeight = Mathf.Max(barHeight + 6f, Mathf.RoundToInt(20f * currentUiScale));
            Rect rowRect = GUILayoutUtility.GetRect(
                10f, rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            float labelWidth = Mathf.Clamp(rowRect.width * 0.22f, 52f, 100f);
            float valueWidth = Mathf.Clamp(rowRect.width * 0.28f, 60f, 140f);
            float gap = Mathf.RoundToInt(10f * currentUiScale);

            GUI.Label(
                new Rect(rowRect.x, rowRect.y - 1f, labelWidth, rowRect.height + 2f),
                label,
                smallMutedStyle);

            GUI.Label(
                new Rect(rowRect.xMax - valueWidth, rowRect.y - 1f, valueWidth, rowRect.height + 2f),
                valueText,
                valueLabelStyle);

            Rect barRect = new Rect(
                rowRect.x + labelWidth + gap,
                rowRect.y + Mathf.Max(0f, (rowRect.height - barHeight) * 0.5f),
                rowRect.width - labelWidth - valueWidth - (gap * 2f),
                barHeight);

            Color prev = GUI.color;

            GUI.color = new Color(0.039f, 0.047f, 0.027f, 0.92f);
            GUI.DrawTexture(barRect, statBarBgTexture);

            float fillWidth = (barRect.width - 2f) * Mathf.Clamp01(fraction);
            if (fillWidth > 0f)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(
                    new Rect(barRect.x + 1f, barRect.y + 1f, fillWidth, barRect.height - 2f),
                    fillTexture != null ? fillTexture : statBarHpTexture);
            }

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.10f);
            GUI.DrawTexture(
                new Rect(barRect.x, barRect.y, barRect.width, 1f),
                amberAccentTexture);

            GUI.color = prev;
        }

        private void DrawBorderedPanel(Rect rect, Action drawer)
        {
            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.32f);
            GUI.DrawTexture(
                new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height),
                panelTexture);

            GUI.color = new Color(0.067f, 0.078f, 0.043f, 0.94f);
            GUI.DrawTexture(rect, hudCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.25f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), amberAccentTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.50f);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width, 1f),
                amberAccentTexture);

            GUI.color = prev;

            float padH = Mathf.RoundToInt(12f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect contentRect = new Rect(
                rect.x + padH,
                rect.y + padV,
                rect.width - padH * 2f,
                rect.height - padV * 2f);

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
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

        private void DrawBorderPanel(Rect rect, Texture2D fillTexture, float borderThickness)
        {
            Color prev = GUI.color;

            GUI.color = new Color(0.067f, 0.078f, 0.043f, 0.95f);
            GUI.DrawTexture(rect, fillTexture != null ? fillTexture : panelTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.22f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderThickness), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderThickness, rect.width, borderThickness), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, borderThickness, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - borderThickness, rect.y, borderThickness, rect.height), amberAccentTexture);

            GUI.color = prev;
        }

        private void EnsureGuiStyles()
        {
            float targetUiScale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.75f, 1.6f);

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
                normal = { textColor = new Color(0.910f, 0.875f, 0.753f) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            smallMutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
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
                fontSize = 24,
                normal = { textColor = new Color(0.910f, 0.875f, 0.753f) }
            };

            journalSubtitleStyle = new GUIStyle(smallMutedStyle)
            {
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            journalTabStyle = new GUIStyle(buttonStyle)
            {
                normal =
                {
                    background = journalTabTexture,
                    textColor = new Color(0.353f, 0.333f, 0.251f)
                },
                hover =
                {
                    background = chipTexture,
                    textColor = new Color(0.788f, 0.659f, 0.298f)
                }
            };

            journalActiveTabStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    background = journalTabActiveTexture,
                    textColor = new Color(0.910f, 0.796f, 0.471f)
                },
                hover =
                {
                    background = journalTabActiveTexture,
                    textColor = new Color(0.910f, 0.796f, 0.471f)
                }
            };

            journalBodyStyle = new GUIStyle(labelStyle)
            {
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            modeLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.910f, 0.796f, 0.471f) }
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
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            valueLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            settingsRowLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
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
                normal = { textColor = new Color(0.788f, 0.659f, 0.298f) }
            };

            questPendingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
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
                normal = { textColor = new Color(0.353f, 0.333f, 0.251f) }
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
            journalTitleStyle.fontSize = Mathf.RoundToInt(22f * targetUiScale);
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
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale));
            escHintStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
        }

        private string GetBuildMenuItemSubtitle(BuildCatalogItem item, string costLabel)
        {
            if (item == null)
            {
                return string.Empty;
            }

            string kindLabel = GetItemKindLabel(item.kind);
            string subtitle = $"{kindLabel}  |  {costLabel}";

            if (item.renewableVisualMode == RenewableVisualMode.Crop)
            {
                subtitle += "  |  Crop";
            }

            return subtitle;
        }

        private void DrawPlacementPrompt()
        {
            if (!placementActive)
            {
                return;
            }

            BuildCatalogItem selectedItem = GetSelectedItem();
            string title = selectedItem != null ? selectedItem.displayName : "Placement";
            string subtitle = placementHasValidTarget
                ? "Ready to place"
                : "Invalid position";

            string controls = placementSnapEnabled
                ? "Enter / LMB place  •  R rotate  •  G snap"
                : "Enter / LMB place  •  R rotate  •  Esc cancel";

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.contextRect, $"{title}\n{subtitle}\n{controls}", promptLabelStyle);

            DrawBorderPanel(rect, hudCardTexture, 1f);

            Rect innerRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 18f), title, headingStyle);

            Color previousColor = GUI.color;
            GUI.color = placementHasValidTarget
                ? new Color(0.62f, 0.82f, 0.48f, 0.9f)
                : new Color(0.84f, 0.32f, 0.28f, 0.9f);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + 22f, innerRect.width, 2f), amberAccentTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(innerRect.x, innerRect.y + 30f, innerRect.width, 16f), subtitle, labelStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + 48f, innerRect.width, 18f), controls, smallMutedStyle);
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
            panelTexture ??= CreateSolidTexture(new Color(0.040f, 0.051f, 0.031f, 0.88f));
            buttonTexture ??= CreateSolidTexture(new Color(0.086f, 0.102f, 0.055f, 0.92f));
            buttonSelectedTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.96f));
            hotbarTexture ??= CreateSolidTexture(new Color(0.078f, 0.094f, 0.055f, 0.92f));
            hotbarSelectedTexture ??= CreateSolidTexture(new Color(0.224f, 0.212f, 0.098f, 0.98f));
            chipTexture ??= CreateSolidTexture(new Color(0.145f, 0.176f, 0.078f, 0.95f));
            crosshairTexture ??= CreateSolidTexture(Color.white);
            journalShellTexture ??= CreateSolidTexture(new Color(0.040f, 0.051f, 0.031f, 0.97f));
            journalCardTexture ??= CreateSolidTexture(new Color(0.086f, 0.102f, 0.055f, 0.92f));
            journalTabTexture ??= CreateSolidTexture(new Color(0.067f, 0.078f, 0.043f, 0.98f));
            journalTabActiveTexture ??= CreateSolidTexture(new Color(0.145f, 0.176f, 0.078f, 0.99f));
            journalAccentTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 1.00f));
            statBarBgTexture ??= CreateSolidTexture(new Color(0.039f, 0.047f, 0.027f, 0.92f));
            statBarHpTexture ??= CreateSolidTexture(new Color(0.769f, 0.267f, 0.220f, 0.98f));
            statBarStaminaTexture ??= CreateSolidTexture(new Color(0.353f, 0.541f, 0.235f, 0.98f));
            statBarHungerTexture ??= CreateSolidTexture(new Color(0.545f, 0.376f, 0.141f, 0.98f));
            statBarThirstTexture ??= CreateSolidTexture(new Color(0.290f, 0.494f, 0.659f, 0.98f));
            amberAccentTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 1.00f));
            borderDarkTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.20f));
            hudCardTexture ??= CreateSolidTexture(new Color(0.067f, 0.078f, 0.043f, 0.90f));
            modeBadgeTexture ??= CreateSolidTexture(new Color(0.110f, 0.133f, 0.071f, 0.96f));
            toggleOnTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.25f));
            toggleOffTexture ??= CreateSolidTexture(new Color(0.118f, 0.133f, 0.078f, 0.92f));
            questCheckTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.98f));
            skillBarBgTexture ??= CreateSolidTexture(new Color(0.063f, 0.071f, 0.039f, 0.88f));
            skillBarFillTexture ??= CreateSolidTexture(new Color(0.545f, 0.427f, 0.173f, 0.96f));
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

    }
}
