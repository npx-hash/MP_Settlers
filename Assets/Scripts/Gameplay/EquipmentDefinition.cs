using UnityEngine;

namespace MPSettlers.Gameplay
{
    // Add this as a component on your weapon/armor/ammo prefabs.
    public class EquipmentDefinition : MonoBehaviour
    {
        [Header("Classification")]
        public bool isWeapon = false;
        public bool isArmor = false;
        public bool isAmmo = false;


        [Header("Weapon")]
        public WeaponType weaponType = WeaponType.None;

        [Header("Armor")]
        public ArmorSlot armorSlot = ArmorSlot.Chest;

        [Header("Ammo")]
        public AmmoType ammoType = AmmoType.None;
        public int ammoQuantity = 0;

        [Header("Stats")]
        public EquipmentStats stats = new EquipmentStats();

        [Header("Mount")]
        [Tooltip("Bone name to attach to. Leave blank to use defaults.")]
        public string overrideBoneName = string.Empty;

        [Tooltip("Local position offset once attached to bone.")]
        public Vector3 mountPositionOffset = Vector3.zero;

        [Tooltip("Local euler rotation offset once attached to bone.")]
        public Vector3 mountRotationOffset = Vector3.zero;

        [Tooltip("Scale multiplier applied to the mounted mesh. " +
                 "Use to shrink helmets or gloves if they appear too large. " +
                 "Set to 0,0,0 to use original scale.")]
        public Vector3 mountScaleMultiplier = Vector3.zero;
    }
}