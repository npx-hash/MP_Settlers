using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    public class ArmorMount : MonoBehaviour
    {
        // ── Tracks what is mounted in each slot ───────────────────
        private readonly Dictionary<string, GameObject> mountedArmor
            = new(System.StringComparer.Ordinal);

        // ── Find a bone anywhere in the hierarchy ─────────────────
        public Transform FindBone(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
                return null;

            // Try Animator humanoid bone mapping first
            Transform animBone = FindBoneViaAnimator(boneName);
            if (animBone != null)
                return animBone;

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

        // ── Mount an armor piece onto a bone ──────────────────────
        public GameObject MountArmor(
            string     slotKey,
            string     boneName,
            GameObject prefab,
            Vector3    positionOffset,
            Vector3    rotationOffset,
            Vector3    scaleMultiplier)
        {
            // Remove existing piece in this slot first
            DismountArmor(slotKey);

            Transform bone = FindBone(boneName);
            if (bone == null)
            {
                Debug.LogWarning(
                    $"ArmorMount: bone '{boneName}' not found on {gameObject.name}. " +
                    $"Set overrideBoneName on EquipmentDefinition to fix this.");
                return null;
            }

            GameObject instance = Instantiate(prefab, bone);
            instance.transform.localPosition    = positionOffset;
            instance.transform.localEulerAngles = rotationOffset;

            // Apply scale multiplier — useful for helmets / gloves
            // that are the same mesh as the pickup but need to be smaller
            Vector3 baseScale = instance.transform.localScale;
            instance.transform.localScale = new Vector3(
                baseScale.x * scaleMultiplier.x,
                baseScale.y * scaleMultiplier.y,
                baseScale.z * scaleMultiplier.z);

            instance.name = $"[Armor] {slotKey} {prefab.name}";

            // Strip colliders and scripts so armor
            // doesn't interfere with gameplay
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity  = false;
            }

            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;

            mountedArmor[slotKey] = instance;
            return instance;
        }

        // ── Remove armor from a slot ──────────────────────────────
        public void DismountArmor(string slotKey)
        {
            if (!mountedArmor.TryGetValue(slotKey, out GameObject existing))
                return;

            if (existing != null)
                Destroy(existing);

            mountedArmor.Remove(slotKey);
        }

        // ── Remove all mounted armor ──────────────────────────────
        public void DismountAll()
        {
            foreach (var key in new List<string>(mountedArmor.Keys))
                DismountArmor(key);
        }

        // ── Check if a slot has armor mounted ─────────────────────
        public bool HasArmorInSlot(string slotKey)
        {
            return mountedArmor.TryGetValue(slotKey, out GameObject obj)
                && obj != null;
        }
    }
}
