using System;
using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    // ── What kind of weapon is this ──────────────────────────────
    public enum WeaponType
    {
        None,
        Sword,
        Dagger,
        Axe,
        Hammer,
        Warhammer,
        Spear,
        Shield,
        Bow
    }

    // ── Which armor slot this piece occupies ─────────────────────
    public enum ArmorSlot
    {
        Head,
        Chest,
        Hands,
        Feet
    }

    // -- Which ammo type, if any, this item uses (for weapons) or provides (for ammo pickups) ─────────────────────
    public enum AmmoType
    {
        None,
        Arrows
    }

    // ── Stat block shared by weapons and armor ───────────────────

    public class EquipmentStats
    {
        [Tooltip("Flat damage added per swing. Weapons only.")]
        public int damageBonus = 0;

        [Tooltip("0.0 to 1.0. Fraction of incoming damage blocked. Armor only.")]
        [Range(0f, 1f)]
        public float damageReduction = 0f;

        [Tooltip("Multiplier on player move speed while equipped. 1 = no change.")]
        public float moveSpeedMultiplier = 1f;

        [Tooltip("Seconds between allowed swings or raises.")]
        public float attackCooldown = 0.55f;
    }

    // ── Runtime slot — what is currently equipped ────────────────
    [Serializable]
    public class EquippedSlot
    {
        public string catalogItemId = string.Empty;
        public WeaponType weaponType = WeaponType.None;
        public ArmorSlot armorSlot = ArmorSlot.Chest;
        public AmmoType ammoType = AmmoType.None;
        public EquipmentStats stats = new EquipmentStats();
        public GameObject mountedObject = null; // live scene reference

        public bool IsEmpty => string.IsNullOrWhiteSpace(catalogItemId);
    }

    // ── Save data ─────────────────────────────────────────────────
    [Serializable]
    public class EquippedSlotSaveData
    {
        public string catalogItemId = string.Empty;
        public string slotKey       = string.Empty; // "weapon", "shield", "head" etc.
    }

    [Serializable]
    public class EquipmentSaveData
    {
        public List<EquippedSlotSaveData> slots = new List<EquippedSlotSaveData>();
    }

    // ── Slot key constants — use these everywhere ─────────────────
    public static class EquipSlotKey
    {
        public const string Weapon = "weapon";
        public const string Shield = "shield";
        public const string Ammo = "ammo";
        public const string Head = "head";
        public const string Chest = "chest";
        public const string Hands = "hands";
        public const string Feet = "feet";

        public static string FromArmorSlot(ArmorSlot slot) => slot switch
        {
            ArmorSlot.Head => Head,
            ArmorSlot.Chest => Chest,
            ArmorSlot.Hands => Hands,
            ArmorSlot.Feet => Feet,
            _ => Chest
        };

        public static string[] All => new[]
        {
        Weapon, Shield, Ammo, Head, Chest, Hands, Feet
    };
    }

    // ── Default bone name lookup for humanoid rigs ────────────────
    public static class HumanoidBoneNames
    {
        // Unity's Humanoid rig standard names
        public const string RightHand = "RightHand";
        public const string LeftHand  = "LeftHand";
        public const string Head      = "Head";
        public const string Chest     = "Spine2";   // adjust if your rig differs
        public const string Hips      = "Hips";

        public static string ForSlotKey(string slotKey) => slotKey switch
        {
            EquipSlotKey.Weapon => RightHand,
            EquipSlotKey.Shield => LeftHand,
            EquipSlotKey.Ammo   => Hips,
            EquipSlotKey.Head   => Head,
            EquipSlotKey.Chest  => Chest,
            EquipSlotKey.Hands  => RightHand,
            EquipSlotKey.Feet   => Hips,
            _                   => Chest
        };
    }

    // ── Lightweight data mirror of EquipmentDefinition ──────────────
    // Works even when the prefab has no EquipmentDefinition component
    // by inferring equipment type from catalog metadata.
    public class EquipmentInfo
    {
        public bool isWeapon;
        public bool isArmor;
        public bool isAmmo;
        public WeaponType weaponType = WeaponType.None;
        public ArmorSlot armorSlot = ArmorSlot.Chest;
        public AmmoType ammoType = AmmoType.None;
        public EquipmentStats stats = new EquipmentStats();
        public string overrideBoneName = string.Empty;
        public Vector3 mountPositionOffset = Vector3.zero;
        public Vector3 mountRotationOffset = Vector3.zero;
        public Vector3 mountScaleMultiplier = Vector3.zero;

        public static EquipmentInfo FromDefinition(EquipmentDefinition def)
        {
            if (def == null) return null;
            return new EquipmentInfo
            {
                isWeapon = def.isWeapon,
                isArmor = def.isArmor,
                isAmmo = def.isAmmo,
                weaponType = def.weaponType,
                armorSlot = def.armorSlot,
                ammoType = def.ammoType,
                stats = def.stats ?? new EquipmentStats(),
                overrideBoneName = def.overrideBoneName ?? string.Empty,
                mountPositionOffset = def.mountPositionOffset,
                mountRotationOffset = def.mountRotationOffset,
                mountScaleMultiplier = def.mountScaleMultiplier
            };
        }

        public static EquipmentInfo InferFromCatalog(string catalogId, int category, int pickupType)
        {
            if (string.IsNullOrWhiteSpace(catalogId))
                return null;

            // Only infer for Weapons category (3) or Weapon pickup type (2)
            if (category != 3 && pickupType != 2)
                return null;

            string lower = catalogId.ToLowerInvariant();

            if (lower.Contains("arrow"))
            {
                return new EquipmentInfo
                {
                    isAmmo = true,
                    ammoType = AmmoType.Arrows,
                    stats = new EquipmentStats()
                };
            }

            if (lower.Contains("shield"))
            {
                return new EquipmentInfo
                {
                    isWeapon = true,
                    weaponType = WeaponType.Shield,
                    stats = new EquipmentStats
                    {
                        damageReduction = 0.15f,
                        moveSpeedMultiplier = 0.95f,
                        attackCooldown = 0.4f
                    },
                    mountPositionOffset = new Vector3(0f, 0.05f, 0f),
                    mountRotationOffset = new Vector3(0f, 0f, 90f)
                };
            }

            WeaponType wt = InferWeaponType(lower);
            ApplyWeaponDefaults(wt, out EquipmentStats wStats,
                out Vector3 posOff, out Vector3 rotOff);

            return new EquipmentInfo
            {
                isWeapon = true,
                weaponType = wt,
                stats = wStats,
                mountPositionOffset = posOff,
                mountRotationOffset = rotOff
            };
        }

        private static WeaponType InferWeaponType(string lowerItemId)
        {
            if (lowerItemId.Contains("sword")) return WeaponType.Sword;
            if (lowerItemId.Contains("dagger")) return WeaponType.Dagger;
            if (lowerItemId.Contains("ax")) return WeaponType.Axe;
            if (lowerItemId.Contains("hammer")) return WeaponType.Warhammer;
            if (lowerItemId.Contains("spear")) return WeaponType.Spear;
            if (lowerItemId.Contains("bow")) return WeaponType.Bow;
            return WeaponType.None;
        }

        private static void ApplyWeaponDefaults(
            WeaponType wt,
            out EquipmentStats stats,
            out Vector3 positionOffset,
            out Vector3 rotationOffset)
        {
            stats = new EquipmentStats();

            // Mount offsets are Vector3.zero — MountEquipmentOnPlayer
            // will compute the grip position dynamically from the rig's
            // actual bone positions (wrist → middle-finger proximal).
            // Only set non-zero offsets here for weapon types that need
            // an ADDITIONAL fine-tuning shift on top of the dynamic palm.
            positionOffset = Vector3.zero;
            rotationOffset = Vector3.zero;

            switch (wt)
            {
                case WeaponType.Sword:
                    stats.damageBonus = 8;
                    stats.attackCooldown = 0.50f;
                    break;

                case WeaponType.Dagger:
                    stats.damageBonus = 5;
                    stats.attackCooldown = 0.30f;
                    stats.moveSpeedMultiplier = 1.05f;
                    break;

                case WeaponType.Axe:
                    stats.damageBonus = 10;
                    stats.attackCooldown = 0.65f;
                    stats.moveSpeedMultiplier = 0.95f;
                    break;

                case WeaponType.Hammer:
                    stats.damageBonus = 7;
                    stats.attackCooldown = 0.55f;
                    break;

                case WeaponType.Warhammer:
                    stats.damageBonus = 14;
                    stats.attackCooldown = 0.80f;
                    stats.moveSpeedMultiplier = 0.90f;
                    break;

                case WeaponType.Spear:
                    stats.damageBonus = 9;
                    stats.attackCooldown = 0.60f;
                    break;

                case WeaponType.Bow:
                    stats.damageBonus = 6;
                    stats.attackCooldown = 1.00f;
                    break;
            }
        }

        // ── Static fallback mount offsets ────────────────────────────
        // Used ONLY when dynamic palm detection fails (no Animator or
        // no finger bones).  These push along local Y which works for
        // some rigs but not all — the dynamic path is always preferred.
        public static readonly Vector3 FallbackRightHandPosition =
            new Vector3(0f, 0.10f, 0.02f);
        public static readonly Vector3 FallbackRightHandRotation =
            new Vector3(0f, 0f, 0f);

        public static readonly Vector3 FallbackLeftHandPosition =
            new Vector3(0f, 0.08f, 0f);
        public static readonly Vector3 FallbackLeftHandRotation =
            new Vector3(0f, 0f, 90f);
    }
}
