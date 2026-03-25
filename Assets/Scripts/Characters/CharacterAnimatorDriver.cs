using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Characters
{
    public class CharacterAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private PlayerMovementController movementController;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform animationSpace;
        [SerializeField] private float floatDampTime = 0.1f;

        [Header("Float Parameters")]
        [SerializeField] private string moveXParameter = "MoveX";
        [SerializeField] private string moveYParameter = "MoveY";
        [SerializeField] private string moveSpeedParameter = "MoveSpeed";
        [SerializeField] private string moveMagnitudeParameter = "MoveMagnitude";
        [SerializeField] private string verticalVelocityParameter = "VerticalVelocity";

        [Header("Bool Parameters")]
        [SerializeField] private string groundedParameter = "Grounded";
        [SerializeField] private string sprintingParameter = "Sprinting";
        [SerializeField] private string movingParameter = "Moving";

        [Header("Trigger Parameters")]
        [SerializeField] private string jumpTriggerParameter = "Jump";

        private readonly Dictionary<string, int> parameterHashes = new();
        private readonly HashSet<int> availableParameters = new();

        private void Reset()
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            movementController = GetComponent<PlayerMovementController>();
            if (movementController == null)
            {
                movementController = GetComponentInParent<PlayerMovementController>();
            }
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();
            }

            if (movementController == null)
            {
                movementController = GetComponentInParent<PlayerMovementController>();
            }

            if (animationSpace == null && animator != null)
            {
                animationSpace = animator.transform;
            }

            CacheAnimatorParameters();
        }

        private void OnValidate()
        {
            floatDampTime = Mathf.Max(0f, floatDampTime);
        }

        private void LateUpdate()
        {
            if (animator == null || movementController == null)
            {
                return;
            }

            Transform referenceSpace = animationSpace != null ? animationSpace : animator.transform;
            Vector3 localMoveDirection = referenceSpace.InverseTransformDirection(movementController.WorldMoveDirection);
            localMoveDirection.y = 0f;

            SetFloat(moveXParameter, localMoveDirection.x);
            SetFloat(moveYParameter, localMoveDirection.z);
            SetFloat(moveSpeedParameter, movementController.NormalizedSpeed);
            SetFloat(moveMagnitudeParameter, movementController.PlanarVelocity.magnitude);
            SetFloat(verticalVelocityParameter, movementController.VerticalVelocity);

            SetBool(groundedParameter, movementController.IsGrounded);
            SetBool(sprintingParameter, movementController.IsSprinting);
            SetBool(movingParameter, movementController.HasMovementInput);

            if (movementController.JumpedThisFrame)
            {
                SetTrigger(jumpTriggerParameter);
            }
        }

        private void CacheAnimatorParameters()
        {
            availableParameters.Clear();
            parameterHashes.Clear();

            if (animator == null)
            {
                return;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                availableParameters.Add(parameter.nameHash);
                parameterHashes[parameter.name] = parameter.nameHash;
            }
        }

        private bool TryGetParameterHash(string parameterName, out int parameterHash)
        {
            parameterHash = default;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            if (!parameterHashes.TryGetValue(parameterName, out parameterHash))
            {
                parameterHash = Animator.StringToHash(parameterName);
                parameterHashes[parameterName] = parameterHash;
            }

            return availableParameters.Contains(parameterHash);
        }

        private void SetFloat(string parameterName, float value)
        {
            if (TryGetParameterHash(parameterName, out int parameterHash))
            {
                animator.SetFloat(parameterHash, value, floatDampTime, Time.deltaTime);
            }
        }

        private void SetBool(string parameterName, bool value)
        {
            if (TryGetParameterHash(parameterName, out int parameterHash))
            {
                animator.SetBool(parameterHash, value);
            }
        }

        private void SetTrigger(string parameterName)
        {
            if (TryGetParameterHash(parameterName, out int parameterHash))
            {
                animator.SetTrigger(parameterHash);
            }
        }
    }
}
