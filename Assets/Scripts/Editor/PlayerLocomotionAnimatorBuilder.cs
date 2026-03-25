using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MPSettlers.EditorTools
{
    public static class PlayerLocomotionAnimatorBuilder
    {
        private const string ControllerPath = "Assets/Created/Animation/PlayerLocomotion_Generated.controller";

        private const string IdleClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/Idle.fbx";
        private const string RunForwardClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/RunForward.fbx";
        private const string RunBackwardClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/RunBackward.fbx";
        private const string RunLeftClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/StrafeLeft.fbx";
        private const string RunRightClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/StrafeRight.fbx";
        private const string RunBackwardLeftClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/RunBackwardLeft.fbx";
        private const string RunBackwardRightClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/RunBackwardRight.fbx";
        private const string SprintClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/Sprint.fbx";
        private const string JumpClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/JumpWhileRunning.fbx";
        private const string FallClipPath = "Assets/Downloaded/Blink/Art/Animations/Animations_Starter_Pack/Movement/FallingLoop.fbx";

        [MenuItem("Tools/MP Settlers/Create Player Locomotion Animator")]
        public static void CreatePlayerLocomotionAnimator()
        {
            AnimationClip idleClip = LoadClip(IdleClipPath);
            AnimationClip runForwardClip = LoadClip(RunForwardClipPath);
            AnimationClip runBackwardClip = LoadClip(RunBackwardClipPath);
            AnimationClip runLeftClip = LoadClip(RunLeftClipPath);
            AnimationClip runRightClip = LoadClip(RunRightClipPath);
            AnimationClip runBackwardLeftClip = LoadClip(RunBackwardLeftClipPath);
            AnimationClip runBackwardRightClip = LoadClip(RunBackwardRightClipPath);
            AnimationClip sprintClip = LoadClip(SprintClipPath);
            AnimationClip jumpClip = LoadClip(JumpClipPath);
            AnimationClip fallClip = LoadClip(FallClipPath);

            if (idleClip == null || runForwardClip == null || runBackwardClip == null ||
                runLeftClip == null || runRightClip == null || sprintClip == null ||
                jumpClip == null || fallClip == null)
            {
                EditorUtility.DisplayDialog(
                    "Player Animator",
                    "One or more movement clips could not be found. Make sure the Blink animation pack is imported under Assets/Downloaded/Blink.",
                    "OK");
                return;
            }

            EnsureDirectory("Assets/Created");
            EnsureDirectory("Assets/Created/Animation");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null)
            {
                AssignToSelection(controller);
                EditorGUIUtility.PingObject(controller);
                return;
            }

            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AddParameters(controller);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState locomotionState = stateMachine.AddState("Locomotion");
            AnimatorState sprintState = stateMachine.AddState("Sprint");
            AnimatorState jumpState = stateMachine.AddState("Jump");
            AnimatorState fallState = stateMachine.AddState("Fall");

            stateMachine.defaultState = locomotionState;

            locomotionState.motion = CreateLocomotionTree(
                controller,
                idleClip,
                runForwardClip,
                runBackwardClip,
                runLeftClip,
                runRightClip,
                runBackwardLeftClip,
                runBackwardRightClip);
            sprintState.motion = sprintClip;
            jumpState.motion = jumpClip;
            fallState.motion = fallClip;

            AddTransition(locomotionState, sprintState, 0.08f, ("Sprinting", AnimatorConditionMode.If, 0f), ("Moving", AnimatorConditionMode.If, 0f));
            AddTransition(sprintState, locomotionState, 0.08f, ("Sprinting", AnimatorConditionMode.IfNot, 0f));
            AddTransition(sprintState, locomotionState, 0.08f, ("Moving", AnimatorConditionMode.IfNot, 0f));

            AddTransition(locomotionState, jumpState, 0.02f, ("Jump", AnimatorConditionMode.If, 0f));
            AddTransition(sprintState, jumpState, 0.02f, ("Jump", AnimatorConditionMode.If, 0f));

            AddTransition(jumpState, fallState, 0.05f, ("VerticalVelocity", AnimatorConditionMode.Less, 0f));
            AddTransition(locomotionState, fallState, 0.05f, ("Grounded", AnimatorConditionMode.IfNot, 0f), ("VerticalVelocity", AnimatorConditionMode.Less, 0f));
            AddTransition(sprintState, fallState, 0.05f, ("Grounded", AnimatorConditionMode.IfNot, 0f), ("VerticalVelocity", AnimatorConditionMode.Less, 0f));

            AddTransition(fallState, locomotionState, 0.08f, ("Grounded", AnimatorConditionMode.If, 0f));
            AddTransition(jumpState, locomotionState, 0.08f, ("Grounded", AnimatorConditionMode.If, 0f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssignToSelection(controller);
            EditorGUIUtility.PingObject(controller);

            EditorUtility.DisplayDialog(
                "Player Animator",
                "Created PlayerLocomotion_Generated.controller and assigned it to the selected Animator when possible.",
                "OK");
        }

        private static void AddParameters(AnimatorController controller)
        {
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveMagnitude", AnimatorControllerParameterType.Float);
            controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Sprinting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        }

        private static BlendTree CreateLocomotionTree(
            AnimatorController controller,
            AnimationClip idleClip,
            AnimationClip runForwardClip,
            AnimationClip runBackwardClip,
            AnimationClip runLeftClip,
            AnimationClip runRightClip,
            AnimationClip runBackwardLeftClip,
            AnimationClip runBackwardRightClip)
        {
            BlendTree blendTree = new BlendTree
            {
                name = "LocomotionTree",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };

            AssetDatabase.AddObjectToAsset(blendTree, controller);

            blendTree.AddChild(idleClip, Vector2.zero);
            blendTree.AddChild(runForwardClip, new Vector2(0f, 1f));
            blendTree.AddChild(runBackwardClip, new Vector2(0f, -1f));
            blendTree.AddChild(runLeftClip, new Vector2(-1f, 0f));
            blendTree.AddChild(runRightClip, new Vector2(1f, 0f));

            if (runBackwardLeftClip != null)
            {
                blendTree.AddChild(runBackwardLeftClip, new Vector2(-1f, -1f));
            }

            if (runBackwardRightClip != null)
            {
                blendTree.AddChild(runBackwardRightClip, new Vector2(1f, -1f));
            }

            return blendTree;
        }

        private static void AddTransition(
            AnimatorState fromState,
            AnimatorState toState,
            float duration,
            params (string parameter, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            AnimatorStateTransition transition = fromState.AddTransition(toState);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;

            foreach ((string parameter, AnimatorConditionMode mode, float threshold) in conditions)
            {
                transition.AddCondition(mode, threshold, parameter);
            }
        }

        private static AnimationClip LoadClip(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        }

        private static void AssignToSelection(AnimatorController controller)
        {
            if (controller == null)
            {
                return;
            }

            GameObject selectedObject = Selection.activeGameObject;
            if (selectedObject == null)
            {
                return;
            }

            Animator animator = selectedObject.GetComponent<Animator>();
            if (animator == null)
            {
                animator = selectedObject.GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                return;
            }

            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(animator);
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parentFolder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string folderName = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parentFolder) && !string.IsNullOrEmpty(folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
        }
    }
}
