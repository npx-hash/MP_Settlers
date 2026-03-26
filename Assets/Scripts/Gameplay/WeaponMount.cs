using UnityEngine;

namespace MPSettlers.Gameplay
{
    public class WeaponMount : MonoBehaviour
    {
        // ── Find a bone anywhere in the hierarchy by name ─────────
        public Transform FindBone(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
                return null;

            // Try Animator humanoid bone mapping first — works regardless of
            // the actual bone Transform names in the rig hierarchy.
            Transform animBone = FindBoneViaAnimator(boneName);
            if (animBone != null)
                return animBone;

            // Fall back to recursive name search
            return FindBoneRecursive(transform, boneName);
        }

        private Transform FindBoneViaAnimator(string boneName)
        {
            Animator animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                return null;

            HumanBodyBones? mapped = boneName switch
            {
                "RightHand" => HumanBodyBones.RightHand,
                "LeftHand"  => HumanBodyBones.LeftHand,
                "Head"      => HumanBodyBones.Head,
                "Spine2"    => HumanBodyBones.Chest,
                "Hips"      => HumanBodyBones.Hips,
                _           => null
            };

            if (!mapped.HasValue)
                return null;

            return animator.GetBoneTransform(mapped.Value);
        }

        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (string.Equals(parent.name, boneName,
                System.StringComparison.OrdinalIgnoreCase))
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, boneName);
                if (found != null)
                    return found;
            }

            return null;
        }

        // ── Compute the local offset from a hand bone to the mid-palm ─
        // Uses the actual bone positions so it works regardless of
        // the rig's local-axis conventions.
        public Vector3 GetPalmOffset(string handBoneName)
        {
            GetPalmOffsetAndRotation(handBoneName, out Vector3 pos, out _);
            return pos;
        }

        // ── Compute both position and grip-aligned rotation ─────────
        // Position: mid-palm (55 % wrist → middle-finger-proximal).
        // Rotation: aligns child's local +Y with the wrist→finger
        //           direction so weapon handles point along the grip.
        public void GetPalmOffsetAndRotation(
            string handBoneName,
            out Vector3 positionOffset,
            out Vector3 rotationOffset)
        {
            positionOffset = Vector3.zero;
            rotationOffset = Vector3.zero;

            Animator animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                return;

            bool isRight = string.Equals(handBoneName, "RightHand",
                System.StringComparison.OrdinalIgnoreCase);

            HumanBodyBones handBone = isRight
                ? HumanBodyBones.RightHand
                : HumanBodyBones.LeftHand;

            HumanBodyBones fingerBone = isRight
                ? HumanBodyBones.RightMiddleProximal
                : HumanBodyBones.LeftMiddleProximal;

            Transform hand = animator.GetBoneTransform(handBone);
            Transform finger = animator.GetBoneTransform(fingerBone);

            if (hand == null || finger == null)
                return;

            // The grip center is roughly 55% of the way from wrist to
            // the base of the middle finger — the natural grip area.
            Vector3 worldGripCenter = Vector3.Lerp(
                hand.position, finger.position, 0.55f);

            // Convert to the hand bone's local space so it works as
            // a localPosition offset on a child transform.
            positionOffset = hand.InverseTransformPoint(worldGripCenter);

            // Compute grip-aligned rotation: align child +Y with the
            // local wrist→finger direction so weapon handles run along
            // the natural grip axis.
            Vector3 localGripDir = hand.InverseTransformDirection(
                (finger.position - hand.position).normalized);

            if (localGripDir.sqrMagnitude > 0.001f)
            {
                Quaternion gripRot = Quaternion.FromToRotation(Vector3.up, localGripDir);
                rotationOffset = gripRot.eulerAngles;
            }
        }

        // ── Mount a prefab onto a named bone, return the instance ─
        public GameObject MountOnBone(
            string     boneName,
            GameObject prefab,
            Vector3    positionOffset,
            Vector3    rotationOffset)
        {
            Transform bone = FindBone(boneName);
            if (bone == null)
            {
                Debug.LogWarning(
                    $"WeaponMount: bone '{boneName}' not found on {gameObject.name}. " +
                    $"Check HumanoidBoneNames or set overrideBoneName on EquipmentDefinition.");
                return null;
            }

            GameObject instance = Instantiate(prefab, bone);
            instance.transform.localPosition = positionOffset;
            instance.transform.localEulerAngles = rotationOffset;
            instance.name = $"[Mounted] {prefab.name}";

            // Disable all colliders and scripts on the mounted copy
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is not WeaponMount)
                    mb.enabled = false;
            }

            return instance;
        }

        // ── Destroy a previously mounted instance ─────────────────
        public void Dismount(GameObject mountedInstance)
        {
            if (mountedInstance != null)
                Destroy(mountedInstance);
        }
    }
}
