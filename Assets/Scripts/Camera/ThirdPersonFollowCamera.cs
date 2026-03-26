using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using MPSettlers.Characters;

namespace MPSettlers.CameraSystem
{
    public class ThirdPersonFollowCamera : MonoBehaviour
    {
        private enum CameraViewMode
        {
            ThirdPerson,
            FirstPerson
        }

        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string lookActionName = "Look";
        [SerializeField] private InputActionReference lookActionOverride;

        [Header("View")]
        [SerializeField] private CameraViewMode defaultViewMode = CameraViewMode.ThirdPerson;
        [SerializeField] private Key toggleViewKey = Key.V;
        [SerializeField] private bool hideTargetRenderersInFirstPerson = true;

        [Header("Third Person")]
        [SerializeField] private Vector3 pivotOffset = new(0f, 1.6f, 0f);
        [SerializeField] private Vector3 shoulderOffset = new(0.45f, 0f, 0f);
        [SerializeField] private float distance = 5f;
        [SerializeField] private float minDistance = 1.25f;

        [Header("First Person")]
        [SerializeField] private Vector3 firstPersonPivotOffset = new(0f, 1.65f, 0f);

        [Header("Look")]
        [SerializeField] private float minPitch = -30f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float stickSensitivity = 180f;
        [SerializeField] private bool invertY;

        [Header("Smoothing")]
        [SerializeField] private float followSmoothTime = 0.05f;
        [SerializeField] private float rotationSmoothSpeed = 24f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.2f;
        [SerializeField] private float collisionPadding = 0.1f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnEnable = true;

        private InputAction lookAction;
        private bool enabledLookAction;
        private Vector3 currentVelocity;
        private float yaw;
        private float pitch = 18f;
        private CameraViewMode currentViewMode;
        private Renderer[] targetRenderers;
        private bool uiCursorModeEnabled;

        public Transform Target
        {
            get => target;
            set
            {
                target = value;
                if (playerInput == null && target != null)
                {
                    playerInput = target.GetComponent<PlayerInput>();
                }

                CacheTargetRenderers();
                ApplyViewModeSettings();
            }
        }

        private void Reset()
        {
            PlayerMovementController movementController = FindAnyObjectByType<PlayerMovementController>();
            if (movementController != null)
            {
                target = movementController.transform;
            }
        }

        private void Awake()
        {
            if (target == null)
            {
                PlayerMovementController movementController = FindAnyObjectByType<PlayerMovementController>();
                if (movementController != null)
                {
                    target = movementController.transform;
                }
            }

            if (playerInput == null && target != null)
            {
                playerInput = target.GetComponent<PlayerInput>();
            }

            if (target != null)
            {
                yaw = target.eulerAngles.y;
            }

            currentViewMode = defaultViewMode;
            CacheTargetRenderers();
            ApplyViewModeSettings();
        }

        private void OnEnable()
        {
            lookAction = ResolveLookAction();
            EnableLookActionIfNeeded();
            CacheTargetRenderers();
            ApplyViewModeSettings();

            if (lockCursorOnEnable)
            {
                SetCursorState(!uiCursorModeEnabled);
            }
        }

        private void OnDisable()
        {
            if (enabledLookAction && lookAction != null)
            {
                lookAction.Disable();
                enabledLookAction = false;
            }

            SetTargetRenderersVisible(true);

            if (lockCursorOnEnable)
            {
                SetCursorState(false);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            HandleViewToggleInput();
            UpdateRotation();
            UpdateCameraPosition();
            UpdateCursorStateFromInput();
        }

        private void OnValidate()
        {
            distance = Mathf.Max(0.5f, distance);
            minDistance = Mathf.Clamp(minDistance, 0.25f, distance);
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            collisionPadding = Mathf.Max(0f, collisionPadding);
            followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
            rotationSmoothSpeed = Mathf.Max(0f, rotationSmoothSpeed);
            maxPitch = Mathf.Max(minPitch, maxPitch);
            firstPersonPivotOffset.y = Mathf.Max(0.1f, firstPersonPivotOffset.y);
        }

        private InputAction ResolveLookAction()
        {
            if (lookActionOverride != null)
            {
                return lookActionOverride.action;
            }

            if (playerInput == null || playerInput.actions == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(actionMapName))
            {
                InputActionMap map = playerInput.actions.FindActionMap(actionMapName, false);
                if (map != null)
                {
                    return map.FindAction(lookActionName, false);
                }
            }

            return playerInput.actions.FindAction(lookActionName, false);
        }

        private void EnableLookActionIfNeeded()
        {
            if (playerInput != null)
            {
                playerInput.ActivateInput();
                return;
            }

            if (lookAction != null && !lookAction.enabled)
            {
                lookAction.Enable();
                enabledLookAction = true;
            }
        }

        private void UpdateRotation()
        {
            // Skip look input while UI cursor mode is active (menu open)
            if (uiCursorModeEnabled)
                return;

            Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
            bool isPointerInput = lookAction != null && lookAction.activeControl != null &&
                                  lookAction.activeControl.device is Pointer;

            float yawDelta;
            float pitchDelta;

            if (isPointerInput)
            {
                yawDelta = lookInput.x * mouseSensitivity;
                pitchDelta = lookInput.y * mouseSensitivity;
            }
            else
            {
                yawDelta = lookInput.x * stickSensitivity * Time.unscaledDeltaTime;
                pitchDelta = lookInput.y * stickSensitivity * Time.unscaledDeltaTime;
            }

            yaw += yawDelta;
            pitch += invertY ? pitchDelta : -pitchDelta;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            if (currentViewMode == CameraViewMode.FirstPerson)
            {
                target.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private void UpdateCameraPosition()
        {
            Quaternion targetRotation = Quaternion.Euler(pitch, yaw, 0f);

            if (currentViewMode == CameraViewMode.FirstPerson)
            {
                Vector3 desiredCameraPosition = target.position + (targetRotation * firstPersonPivotOffset);
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desiredCameraPosition,
                    ref currentVelocity,
                    followSmoothTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);
                return;
            }

            Vector3 pivot = target.position + pivotOffset;
            Vector3 lookOffset = targetRotation * shoulderOffset;
            Vector3 desiredThirdPersonPosition = pivot + lookOffset - (targetRotation * Vector3.forward * distance);
            Vector3 resolvedPosition = ResolveCollisionPosition(pivot + lookOffset, desiredThirdPersonPosition);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                resolvedPosition,
                ref currentVelocity,
                followSmoothTime);

            Quaternion lookRotation = Quaternion.LookRotation((pivot - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, rotationSmoothSpeed * Time.deltaTime);
        }

        private Vector3 ResolveCollisionPosition(Vector3 pivot, Vector3 desiredCameraPosition)
        {
            Vector3 direction = desiredCameraPosition - pivot;
            float targetDistance = direction.magnitude;
            if (targetDistance <= Mathf.Epsilon)
            {
                return desiredCameraPosition;
            }

            direction /= targetDistance;

            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    direction,
                    out RaycastHit hit,
                    targetDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                float clippedDistance = Mathf.Max(minDistance, hit.distance - collisionPadding);
                return pivot + (direction * clippedDistance);
            }

            return desiredCameraPosition;
        }

        private void UpdateCursorStateFromInput()
        {
            if (!lockCursorOnEnable || Keyboard.current == null || uiCursorModeEnabled)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetCursorState(false);
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetCursorState(true);
            }
        }

        private void HandleViewToggleInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            KeyControl toggleControl = Keyboard.current[toggleViewKey];
            if (toggleControl != null && toggleControl.wasPressedThisFrame)
            {
                currentViewMode = currentViewMode == CameraViewMode.ThirdPerson
                    ? CameraViewMode.FirstPerson
                    : CameraViewMode.ThirdPerson;
                ApplyViewModeSettings();
            }
        }

        private void CacheTargetRenderers()
        {
            if (target == null)
            {
                targetRenderers = System.Array.Empty<Renderer>();
                return;
            }

            targetRenderers = target.GetComponentsInChildren<Renderer>(true);
        }

        private void ApplyViewModeSettings()
        {
            SetTargetRenderersVisible(!hideTargetRenderersInFirstPerson || currentViewMode != CameraViewMode.FirstPerson);
        }

        private void SetTargetRenderersVisible(bool isVisible)
        {
            if (targetRenderers == null)
            {
                return;
            }

            foreach (Renderer targetRenderer in targetRenderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.enabled = isVisible;
            }
        }

        private static void SetCursorState(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void SetUiCursorMode(bool isEnabled)
        {
            uiCursorModeEnabled = isEnabled;
            if (lockCursorOnEnable)
            {
                SetCursorState(!uiCursorModeEnabled);
            }
        }

        public void ApplyRuntimeTuning(float newFollowSmoothTime, float newRotationSmoothSpeed)
        {
            followSmoothTime = Mathf.Max(0.01f, newFollowSmoothTime);
            rotationSmoothSpeed = Mathf.Max(0f, newRotationSmoothSpeed);
        }
    }
}
