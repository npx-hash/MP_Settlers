using UnityEngine;
using UnityEngine.InputSystem;

namespace MPSettlers.Characters
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float rotationSpeed = 720f;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundedPull = -2f;
        [SerializeField] private Transform moveReference;

        [Header("Input")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string sprintActionName = "Sprint";
        [SerializeField] private InputActionReference moveActionOverride;
        [SerializeField] private InputActionReference jumpActionOverride;
        [SerializeField] private InputActionReference sprintActionOverride;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private float verticalVelocity;
        private bool enabledMoveAction;
        private bool enabledJumpAction;
        private bool enabledSprintAction;

        public Vector2 MoveInput { get; private set; }
        public Vector3 WorldMoveDirection { get; private set; }
        public Vector3 PlanarVelocity { get; private set; }
        public bool IsGrounded => characterController != null && characterController.isGrounded;
        public bool IsSprinting { get; private set; }
        public bool HasMovementInput => MoveInput.sqrMagnitude > 0.001f;
        public bool JumpedThisFrame { get; private set; }
        public float VerticalVelocity => verticalVelocity;
        public float NormalizedSpeed { get; private set; }
        public bool InputSuppressed { get; set; }
        public float ExternalSpeedMultiplier { get; set; } = 1f;

        private void Reset()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();
            if (Camera.main != null)
            {
                moveReference = Camera.main.transform;
            }
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }

            if (moveReference == null && Camera.main != null)
            {
                moveReference = Camera.main.transform;
            }
        }

        private void OnEnable()
        {
            CacheActions();
            EnableStandaloneActions();
        }

        private void OnDisable()
        {
            DisableAction(moveAction, ref enabledMoveAction);
            DisableAction(jumpAction, ref enabledJumpAction);
            DisableAction(sprintAction, ref enabledSprintAction);
        }

        private void Update()
        {
            RefreshMoveReference();
            ReadInput();
            ApplyMovement();
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = -Mathf.Abs(gravity);
            groundedPull = -Mathf.Abs(groundedPull);
        }

        private void CacheActions()
        {
            moveAction = ResolveAction(moveActionOverride, moveActionName);
            jumpAction = ResolveAction(jumpActionOverride, jumpActionName);
            sprintAction = ResolveAction(sprintActionOverride, sprintActionName);
        }

        private InputAction ResolveAction(InputActionReference actionOverride, string actionName)
        {
            if (actionOverride != null)
            {
                return actionOverride.action;
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
                    if (playerInput.currentActionMap != map)
                    {
                        playerInput.SwitchCurrentActionMap(actionMapName);
                    }

                    return map.FindAction(actionName, false);
                }
            }

            return playerInput.actions.FindAction(actionName, false);
        }

        private void EnableStandaloneActions()
        {
            if (playerInput != null)
            {
                playerInput.ActivateInput();
                return;
            }

            EnableAction(moveAction, ref enabledMoveAction);
            EnableAction(jumpAction, ref enabledJumpAction);
            EnableAction(sprintAction, ref enabledSprintAction);
        }

        private static void EnableAction(InputAction action, ref bool actionWasEnabled)
        {
            if (action == null || action.enabled)
            {
                return;
            }

            action.Enable();
            actionWasEnabled = true;
        }

        private static void DisableAction(InputAction action, ref bool actionWasEnabled)
        {
            if (!actionWasEnabled || action == null)
            {
                return;
            }

            action.Disable();
            actionWasEnabled = false;
        }

        private void RefreshMoveReference()
        {
            if (moveReference == null && Camera.main != null)
            {
                moveReference = Camera.main.transform;
            }
        }

        private void ReadInput()
        {
            if (InputSuppressed)
            {
                MoveInput = Vector2.zero;
                IsSprinting = false;
                return;
            }

            MoveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            IsSprinting = sprintAction != null && sprintAction.IsPressed();
        }

        private void ApplyMovement()
        {
            JumpedThisFrame = false;

            bool isGrounded = characterController.isGrounded;
            if (isGrounded && verticalVelocity < groundedPull)
            {
                verticalVelocity = groundedPull;
            }

            Vector3 desiredDirection = GetDesiredMoveDirection(MoveInput);
            WorldMoveDirection = desiredDirection;
            float speedMultiplier = Mathf.Max(0f, ExternalSpeedMultiplier);
            float currentSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * speedMultiplier;
            PlanarVelocity = desiredDirection * currentSpeed;
            float maxSpeed = Mathf.Max(walkSpeed, sprintSpeed) * speedMultiplier;
            NormalizedSpeed = maxSpeed > 0f ? Mathf.Clamp01(PlanarVelocity.magnitude / maxSpeed) : 0f;

            if (!InputSuppressed && jumpAction != null && jumpAction.WasPressedThisFrame() && isGrounded)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                JumpedThisFrame = true;
            }

            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = PlanarVelocity;
            velocity.y = verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);

            if (desiredDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime);
            }
        }

        private Vector3 GetDesiredMoveDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            Transform reference = moveReference;
            Vector3 forward = reference != null ? reference.forward : Vector3.forward;
            Vector3 right = reference != null ? reference.right : Vector3.right;

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 direction = (forward * input.y) + (right * input.x);
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }
    }
}
