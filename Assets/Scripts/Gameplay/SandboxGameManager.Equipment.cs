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
    public partial class SandboxGameManager
    {
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
    }
}
