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
    public partial class SandboxGameManager : MonoBehaviour
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
        private const int StarterWoodSupply = 24;
        private const int StarterStoneSupply = 16;
        private const int StarterFoodSupply = 6;
        private const int StarterTomatoSeedCount = 4;

        private static readonly BuildCategory[] BuildCategoryOrder =
        {
            BuildCategory.Town,
            BuildCategory.Farm,
            BuildCategory.Food,
            BuildCategory.Weapons
        };

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
        private readonly Dictionary<string, BuildCatalogItem> catalogPrefabLookup = new(StringComparer.OrdinalIgnoreCase);
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

        // ── Skills / XP Progression ────────────────────────────────
        private readonly SkillState playerSkills = new();
        private Vector2 skillsTabScroll;

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
                playerMovement.ExternalSpeedMultiplier = GetEquipmentSpeedMultiplier();
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

        private void SetStatusMessage(string message, float duration = 2.5f)
        {
            statusMessage = message ?? string.Empty;
            statusMessageUntil = Time.unscaledTime + duration;
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

        private void AwardSkillXp(SkillType skill, long amount)
        {
            if (amount <= 0) return;
            int prevLevel = playerSkills.GetLevel(skill);
            int newLevel = playerSkills.AddXp(skill, amount);
            if (newLevel > prevLevel)
            {
                SetStatusMessage($"{SkillDefinitions.GetDisplayName(skill)} leveled up! Level {newLevel}");
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
    }
}
