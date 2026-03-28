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
        private void DrawEquippedLoadout()
        {
            // ── Weapon row ────────────────────────────────────────────
            GUILayout.Label("Weapons", headingStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Weapon, GetWeaponSlotDisplayLabel());
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Shield, GetOffhandSlotDisplayLabel());
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Ammo, "Ammo");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);

            // ── Armor rows ────────────────────────────────────────────
            GUILayout.Label("Armor", headingStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Head, "Head");
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Chest, "Chest");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            DrawEquipSlotWidget(EquipSlotKey.Hands, "Hands");
            GUILayout.Space(6f);
            DrawEquipSlotWidget(EquipSlotKey.Feet, "Feet");
            GUILayout.EndHorizontal();

            // ── Stat summary ──────────────────────────────────────────
            GUILayout.Space(Mathf.RoundToInt(14f * currentUiScale));
            float reduction = GetTotalDamageReduction();
            int dmgBonus = GetWeaponDamageBonus();
            float speed = GetEquipmentSpeedMultiplier();

            float statHeight = Mathf.Clamp(76f * currentUiScale, 66f, 120f);
            Rect statCardRect = GUILayoutUtility.GetRect(
                10f, statHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(statHeight));

            DrawBorderPanel(statCardRect, journalCardTexture, 1f);

            Rect inner = new Rect(
                statCardRect.x + 12f,
                statCardRect.y + 10f,
                statCardRect.width - 24f,
                statCardRect.height - 20f);

            GUI.Label(
                new Rect(inner.x, inner.y, inner.width, 18f),
                "Loadout Stats",
                headingStyle);

            GUI.Label(
                new Rect(inner.x, inner.y + 24f, inner.width, 22f),
                $"Damage Bonus: +{dmgBonus}     Reduction: {Mathf.RoundToInt(reduction * 100f)}%     Speed: {speed:F2}x",
                journalSubtitleStyle);
        }

        private void DrawEquipSlotWidget(string slotKey, string label)
        {
            float width = Mathf.Clamp(90f * currentUiScale, 76f, 160f);
            float height = Mathf.Clamp(78f * currentUiScale, 66f, 125f);

            bool hasItem = equippedSlots.TryGetValue(slotKey, out EquippedSlot slot)
                           && !slot.IsEmpty;

            Rect slotRect = GUILayoutUtility.GetRect(
                width, height,
                GUILayout.Width(width),
                GUILayout.Height(height));

            Color prev = GUI.color;

            GUI.color = hasItem
                ? new Color(0.145f, 0.176f, 0.078f, 0.95f)
                : new Color(0.067f, 0.078f, 0.043f, 0.90f);
            GUI.DrawTexture(slotRect, journalCardTexture);

            GUI.color = hasItem
                ? new Color(0.788f, 0.659f, 0.298f, 0.55f)
                : new Color(0.788f, 0.659f, 0.298f, 0.18f);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.y, slotRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.yMax - 1f, slotRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.x, slotRect.y, 1f, slotRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(slotRect.xMax - 1f, slotRect.y, 1f, slotRect.height), amberAccentTexture);

            GUI.color = prev;

            Rect inner = new Rect(
                slotRect.x + 6f,
                slotRect.y + 6f,
                slotRect.width - 12f,
                slotRect.height - 12f);

            GUI.Label(
                new Rect(inner.x, inner.y, inner.width, 14f),
                label, smallMutedStyle);

            if (hasItem && catalogLookup.TryGetValue(slot.catalogItemId, out BuildCatalogItem item))
            {
                GUI.Label(
                    new Rect(inner.x, inner.y + 16f, inner.width, 18f),
                    item.displayName, journalBodyStyle);

                string statLine = string.Empty;

                if (slot.ammoType != AmmoType.None)
                {
                    statLine = slot.ammoType == AmmoType.Arrows
                        ? "Arrows"
                        : "Ammo";
                }
                else if (slot.stats.damageBonus > 0)
                {
                    statLine = $"+{slot.stats.damageBonus} dmg";
                }
                else if (slot.stats.damageReduction > 0f)
                {
                    statLine = $"{Mathf.RoundToInt(slot.stats.damageReduction * 100f)}% block";
                }

                if (!string.IsNullOrWhiteSpace(statLine))
                {
                    GUI.Label(
                        new Rect(inner.x, inner.y + 36f, inner.width, 14f),
                        statLine, smallMutedStyle);
                }

                if (GUI.Button(
                    new Rect(inner.x, inner.yMax - 14f, inner.width, 14f),
                    "unequip", smallMutedStyle))
                {
                    UnequipSlot(slotKey);
                }
            }
            else
            {
                GUI.Label(
                    new Rect(inner.x, inner.y + 18f, inner.width, 16f),
                    "Empty", smallMutedStyle);
            }
        }

        private string GetWeaponSlotDisplayLabel()
        {
            if (!equippedSlots.TryGetValue(EquipSlotKey.Weapon, out EquippedSlot slot) || slot.IsEmpty)
                return "R.Hand";

            return slot.weaponType switch
            {
                WeaponType.Dagger => "Dagger",
                WeaponType.Sword => "Sword",
                WeaponType.Axe => "Axe",
                WeaponType.Hammer => "Hammer",
                WeaponType.Warhammer => "Warhmr",
                WeaponType.Spear => "Spear",
                WeaponType.Bow => "Bow",
                _ => "R.Hand"
            };
        }

        private string GetOffhandSlotDisplayLabel()
        {
            if (equippedSlots.TryGetValue(EquipSlotKey.Weapon, out EquippedSlot slot) && !slot.IsEmpty)
            {
                if (slot.weaponType == WeaponType.Bow)
                    return "Grip";
            }

            return "L.Hand";
        }

        private void DrawEquipButton(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue)
                return;

            JournalInventoryEntry entry = selectedEntry.Value;

            if (!catalogLookup.TryGetValue(entry.itemId, out BuildCatalogItem item))
                return;

            EquipmentInfo def = GetEquipmentInfo(item);

            if (def == null || (!def.isWeapon && !def.isArmor && !def.isAmmo))
                return;

            bool equipped = IsItemEquipped(entry.itemId);

            float btnHeight = Mathf.Clamp(38f * currentUiScale, 32f, 56f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnHeight));

            Color prev = GUI.color;

            GUI.color = equipped
                ? new Color(0.400f, 0.200f, 0.080f, 0.90f)
                : new Color(0.145f, 0.176f, 0.078f, 0.95f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.55f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.xMax - 1f, btnRect.y, 1f, btnRect.height), amberAccentTexture);

            GUI.color = prev;

            string itemVerbLabel = item.displayName;

            if (def.isAmmo && def.ammoType == AmmoType.Arrows)
                itemVerbLabel = "Arrows";
            else if (def.isWeapon && def.weaponType != WeaponType.None)
                itemVerbLabel = def.weaponType.ToString();
            else if (def.isArmor)
                itemVerbLabel = def.armorSlot.ToString();

            string btnLabel = equipped
                ? $"Unequip {itemVerbLabel}"
                : $"Equip {itemVerbLabel}";

            if (GUI.Button(btnRect, btnLabel, journalActiveTabStyle))
            {
                ToggleEquip(entry.itemId);
            }
        }

        private void DrawDropButton(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue || selectedEntry.Value.quantity <= 0)
                return;

            JournalInventoryEntry entry = selectedEntry.Value;

            float btnHeight = Mathf.Clamp(32f * currentUiScale, 28f, 48f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnHeight));

            Color prev = GUI.color;
            GUI.color = new Color(0.300f, 0.140f, 0.060f, 0.85f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.40f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.color = prev;

            if (GUI.Button(btnRect, $"Drop {entry.displayName} (X)", journalActiveTabStyle))
            {
                TryDropSelectedInventoryItem();
            }
        }

        private void DrawContainerStoragePanel()
        {
            if (!TryGetActiveContainer(out PlacedWorldObject placedObject, out ContainerRuntimeStorage containerStorage, out int capacity))
            {
                CloseContainerStorage();
                return;
            }

            float used = GetContainerUsedCapacity(containerStorage);
            string containerName = GetContainerDisplayName(placedObject);

            float shellMargin = Mathf.Clamp(18f * currentUiScale, 14f, 28f);
            Rect shellRect = new(
                shellMargin,
                shellMargin,
                Screen.width - (shellMargin * 2f),
                Screen.height - (shellMargin * 2f));

            DrawBorderPanel(shellRect, journalShellTexture, 2f);

            float pad = Mathf.RoundToInt(18f * currentUiScale);
            Rect inner = new(shellRect.x + pad, shellRect.y + pad, shellRect.width - (pad * 2f), shellRect.height - (pad * 2f));
            float titleH = Mathf.Clamp(30f * currentUiScale, 24f, 36f);

            GUI.Label(new Rect(inner.x, inner.y, inner.width, titleH), $"{containerName} Storage", journalTitleStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + titleH + 4f, inner.width, 2f), amberAccentTexture);
            GUI.Label(new Rect(inner.x, inner.y + titleH + 10f, inner.width, 18f * currentUiScale),
                $"Container ID: {placedObject.UniqueId}  |  Capacity: {used}/{capacity}",
                journalSubtitleStyle);

            float controlsTop = inner.y + titleH + 32f;
            float controlsH = Mathf.Clamp(34f * currentUiScale, 28f, 42f);
            Rect controlsRect = new(inner.x, controlsTop, inner.width, controlsH);
            DrawContainerTransferControls(controlsRect);

            float columnsTop = controlsRect.yMax + Mathf.RoundToInt(8f * currentUiScale);
            float columnsH = inner.yMax - columnsTop - Mathf.RoundToInt(40f * currentUiScale);
            float gutter = Mathf.RoundToInt(12f * currentUiScale);
            float colWidth = (inner.width - gutter) * 0.5f;
            Rect playerRect = new(inner.x, columnsTop, colWidth, columnsH);
            Rect containerRect = new(playerRect.xMax + gutter, columnsTop, colWidth, columnsH);

            DrawContainerPlayerSide(playerRect, containerStorage, capacity);
            DrawContainerStoredSide(containerRect, containerStorage, capacity);

            Rect footerRect = new(inner.x, inner.yMax - Mathf.RoundToInt(30f * currentUiScale), inner.width, Mathf.RoundToInt(24f * currentUiScale));
            GUI.Label(footerRect, "Esc / Tab close", smallMutedStyle);
        }

        private void DrawContainerTransferControls(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transfer Amount", journalBodyStyle, GUILayout.Width(150f * currentUiScale));

            int[] values = { 1, 5, 10, 20 };
            for (int i = 0; i < values.Length; i++)
            {
                bool selected = containerTransferAmount == values[i];
                if (GUILayout.Button(values[i].ToString(), selected ? journalActiveTabStyle : journalTabStyle, GUILayout.Width(52f * currentUiScale)))
                {
                    containerTransferAmount = values[i];
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", journalTabStyle, GUILayout.Width(90f * currentUiScale)))
            {
                CloseContainerStorage();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawContainerPlayerSide(Rect rect, ContainerRuntimeStorage storage, int capacity)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);
            Rect inner = new(rect.x + 10f * currentUiScale, rect.y + 10f * currentUiScale, rect.width - 20f * currentUiScale, rect.height - 20f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f * currentUiScale), "Player Inventory", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + 22f * currentUiScale, inner.width, 1f), amberAccentTexture);

            Rect body = new(inner.x, inner.y + 28f * currentUiScale, inner.width, inner.height - 28f * currentUiScale);
            GUILayout.BeginArea(body);
            DrawContainerResourceRow("Wood", wood, storage.wood, () => DepositResourceToContainer(ResourceType.Wood, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Wood, storage));
            DrawContainerResourceRow("Stone", stone, storage.stone, () => DepositResourceToContainer(ResourceType.Stone, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Stone, storage));
            DrawContainerResourceRow("Food", food, storage.food, () => DepositResourceToContainer(ResourceType.Food, storage, capacity), () => WithdrawResourceFromContainer(ResourceType.Food, storage));

            GUILayout.Space(6f);
            GUI.DrawTexture(GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)), amberAccentTexture);
            GUILayout.Label("Item Stacks", smallMutedStyle);
            containerPlayerItemsScroll = GUILayout.BeginScrollView(containerPlayerItemsScroll, GUILayout.Height(Mathf.Max(80f, body.height - 148f * currentUiScale)));
            DrawContainerItemTransferRows(storedFoodInventory, storage.storedFoodItems, capacity, depositDirection: true);
            DrawContainerItemTransferRows(storedWeaponInventory, storage.storedWeapons, capacity, depositDirection: true);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawContainerStoredSide(Rect rect, ContainerRuntimeStorage storage, int capacity)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);
            Rect inner = new(rect.x + 10f * currentUiScale, rect.y + 10f * currentUiScale, rect.width - 20f * currentUiScale, rect.height - 20f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f * currentUiScale), "Container Contents", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + 22f * currentUiScale, inner.width, 1f), amberAccentTexture);

            int used = GetContainerUsedCapacity(storage);
            GUI.Label(new Rect(inner.x, inner.y + 26f * currentUiScale, inner.width, 18f * currentUiScale), $"Used: {used}/{capacity}", journalSubtitleStyle);

            Rect body = new(inner.x, inner.y + 46f * currentUiScale, inner.width, inner.height - 46f * currentUiScale);
            GUILayout.BeginArea(body);
            GUILayout.Label($"Wood: {storage.wood}", journalBodyStyle);
            GUILayout.Label($"Stone: {storage.stone}", journalBodyStyle);
            GUILayout.Label($"Food: {storage.food}", journalBodyStyle);

            GUILayout.Space(6f);
            GUI.DrawTexture(GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)), amberAccentTexture);
            GUILayout.Label("Stored Item Stacks", smallMutedStyle);
            containerStoredItemsScroll = GUILayout.BeginScrollView(containerStoredItemsScroll, GUILayout.Height(Mathf.Max(90f, body.height - 112f * currentUiScale)));
            DrawContainerItemTransferRows(storage.storedFoodItems, storedFoodInventory, capacity, depositDirection: false);
            DrawContainerItemTransferRows(storage.storedWeapons, storedWeaponInventory, capacity, depositDirection: false);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawContainerResourceRow(string label, int carried, int stored, Action onDeposit, Action onWithdraw)
        {
            float btnH = Mathf.Clamp(26f * currentUiScale, 22f, 34f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {carried} / {stored}", journalBodyStyle, GUILayout.Width(150f * currentUiScale));
            if (GUILayout.Button("Deposit", journalTabStyle, GUILayout.Height(btnH), GUILayout.Width(80f * currentUiScale)) && carried > 0)
            {
                onDeposit?.Invoke();
            }

            if (GUILayout.Button("Take", journalTabStyle, GUILayout.Height(btnH), GUILayout.Width(70f * currentUiScale)) && stored > 0)
            {
                onWithdraw?.Invoke();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawContainerItemTransferRows(Dictionary<string, int> sourceInventory, Dictionary<string, int> targetInventory, int capacity, bool depositDirection)
        {
            foreach (KeyValuePair<string, int> pair in sourceInventory.OrderBy(k => k.Key).ToList())
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{GetDisplayNameForItemId(pair.Key)} x{pair.Value}", smallMutedStyle, GUILayout.Width(190f * currentUiScale));
                string buttonLabel = depositDirection ? "Deposit" : "Take";
                if (GUILayout.Button(buttonLabel, journalTabStyle, GUILayout.Width(80f * currentUiScale)))
                {
                    if (depositDirection)
                    {
                        DepositItemToContainer(pair.Key, sourceInventory, targetInventory, capacity);
                    }
                    else
                    {
                        WithdrawItemFromContainer(pair.Key, sourceInventory, targetInventory);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCraftingPanel()
        {
            if (craftingRecipes == null || craftingRecipes.Count == 0)
            {
                GUILayout.Label("No recipes available.", journalSubtitleStyle);
                return;
            }

            selectedRecipeIndex = Mathf.Clamp(selectedRecipeIndex, 0, craftingRecipes.Count - 1);
            CraftingRecipe selected = craftingRecipes[selectedRecipeIndex];
            bool canCraft = CanAffordRecipe(selected);

            // ── Recipe list (scrollable) ──────────────────────────────
            GUILayout.Label("Recipes", headingStyle);
            GUILayout.Space(4f);

            float recipeRowH = Mathf.Clamp(30f * currentUiScale, 26f, 42f);
            float listHeight = Mathf.Min(craftingRecipes.Count * (recipeRowH + 2f), 200f * currentUiScale);

            craftingScrollPos = GUILayout.BeginScrollView(
                craftingScrollPos,
                false, false,
                GUILayout.Height(listHeight));

            for (int i = 0; i < craftingRecipes.Count; i++)
            {
                CraftingRecipe recipe = craftingRecipes[i];
                bool affordable = CanAffordRecipe(recipe);
                bool isSelected = i == selectedRecipeIndex;

                Rect rowRect = GUILayoutUtility.GetRect(
                    10f, recipeRowH,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(recipeRowH));

                Color prev = GUI.color;

                if (isSelected)
                {
                    GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                    GUI.DrawTexture(rowRect, modeBadgeTexture);
                }

                GUI.color = affordable
                    ? new Color(0.85f, 0.75f, 0.55f, 1f)
                    : new Color(0.50f, 0.45f, 0.40f, 0.70f);

                string marker = isSelected ? "▸ " : "  ";
                string affordTag = affordable ? "" : "  [need materials]";
                GUI.Label(
                    new Rect(rowRect.x + 6f, rowRect.y, rowRect.width - 12f, rowRect.height),
                    $"{marker}{recipe.displayName}{affordTag}",
                    journalBodyStyle);

                GUI.color = prev;

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    rowRect.Contains(Event.current.mousePosition))
                {
                    selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();

            // ── Selected recipe details ───────────────────────────────
            GUILayout.Space(8f);
            GUI.DrawTexture(
                GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)),
                amberAccentTexture);
            GUILayout.Space(6f);

            GUILayout.Label(selected.displayName, headingStyle);
            GUILayout.Space(2f);
            GUILayout.Label(selected.description, journalSubtitleStyle);
            GUILayout.Space(6f);

            string costText = GetRecipeCostDisplay(selected);
            GUILayout.Label($"Cost: {costText}", canCraft ? journalBodyStyle : smallMutedStyle);

            string resultName = GetDisplayNameForItemId(selected.resultItemId);
            string resultType = selected.resultIsStructure ? "Structure (build menu)" : "Item (inventory)";
            GUILayout.Label($"Result: {selected.resultCount}x {resultName}  ({resultType})", journalBodyStyle);

            // Resource summary
            GUILayout.Space(4f);
            GUILayout.Label(
                $"Available: {wood} wood  ·  {stone} stone  ·  {food} food",
                smallMutedStyle);

            // ── Craft button ──────────────────────────────────────────
            GUILayout.Space(8f);
            float btnH = Mathf.Clamp(38f * currentUiScale, 32f, 52f);
            Rect btnRect = GUILayoutUtility.GetRect(
                10f, btnH,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(btnH));

            Color prevBtn = GUI.color;
            GUI.color = canCraft
                ? new Color(0.145f, 0.176f, 0.078f, 0.95f)
                : new Color(0.10f, 0.10f, 0.10f, 0.50f);
            GUI.DrawTexture(btnRect, modeBadgeTexture);

            GUI.color = canCraft
                ? new Color(0.788f, 0.659f, 0.298f, 0.55f)
                : new Color(0.40f, 0.35f, 0.30f, 0.30f);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.yMax - 1f, btnRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(btnRect.xMax - 1f, btnRect.y, 1f, btnRect.height), amberAccentTexture);
            GUI.color = prevBtn;

            string craftLabel = canCraft ? $"Craft {selected.displayName}" : "Insufficient Materials";

            if (GUI.Button(btnRect, craftLabel, journalActiveTabStyle) && canCraft)
            {
                CraftRecipe(selected);
            }
        }

        private string GetInventoryEntryDescription(JournalInventoryEntry entry)
        {
            return entry.categoryLabel switch
            {
                "Food" => "Food items can be consumed in the field for recovery. Use Storage to deposit extras.",
                "Gear" => "Weapons and armor stored in your inventory.",
                "Resource" => "Raw material carried on your person.",
                _ => "A stored item in the settler journal."
            };
        }

        private bool IsItemFavorited(string itemId)
        {
            return favoriteHotbarItemIds.Any(candidate => string.Equals(candidate, itemId, StringComparison.Ordinal));
        }

        private string GetFavoritedSlotLabels(string itemId)
        {
            List<string> slots = new();
            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                if (string.Equals(favoriteHotbarItemIds[i], itemId, StringComparison.Ordinal))
                {
                    slots.Add(GetHotbarKeyLabel(i));
                }
            }

            return slots.Count > 0 ? string.Join(", ", slots) : "-";
        }

        private void DrawCharacterPortraitPlaceholder(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Rect headRect = new(rect.x + (rect.width * 0.31f), rect.y + 14f, rect.width * 0.38f, rect.width * 0.38f);
            Rect torsoRect = new(rect.x + (rect.width * 0.28f), headRect.yMax + 8f, rect.width * 0.44f, rect.height * 0.34f);
            Rect legsRect = new(rect.x + (rect.width * 0.34f), torsoRect.yMax + 4f, rect.width * 0.32f, rect.height * 0.16f);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.24f, 0.28f, 0.18f, 0.95f);
            GUI.DrawTexture(headRect, journalAccentTexture);
            GUI.DrawTexture(torsoRect, chipTexture);
            GUI.DrawTexture(legsRect, hotbarSelectedTexture);
            GUI.color = previousColor;
        }

        private void DrawEquipmentSlotGrid()
        {
            string[] slotLabels =
            {
                "Head",
                "Chest",
                "Hands",
                "Legs",
                "Feet",
                "Tool"
            };

            int columns = 3;
            int rows = Mathf.CeilToInt(slotLabels.Length / (float)columns);
            float size = Mathf.Clamp(56f * currentUiScale, 44f, 64f);

            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= slotLabels.Length)
                    {
                        break;
                    }

                    Rect slotRect = GUILayoutUtility.GetRect(size, size + 18f, GUILayout.Width(size + 24f), GUILayout.Height(size + 18f));
                    Rect boxRect = new(slotRect.x, slotRect.y, size, size);
                    GUI.Box(boxRect, GUIContent.none, hotbarSlotStyle);
                    GUI.Label(new Rect(slotRect.x, boxRect.yMax + 2f, size + 18f, 16f), slotLabels[index], journalSubtitleStyle);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }
        }

        private void DrawSkillTrackCard(string title, int tier, string description)
        {
            const int maxTier = 5;

            tier = Mathf.Clamp(tier, 0, maxTier);

            float cardHeight = Mathf.Clamp(98f * currentUiScale, 84f, 130f);
            Rect cardRect = GUILayoutUtility.GetRect(10f, cardHeight, GUILayout.ExpandWidth(true), GUILayout.Height(cardHeight));

            DrawBorderPanel(cardRect, journalCardTexture, 1f);

            Rect innerRect = new Rect(cardRect.x + 10f, cardRect.y + 8f, cardRect.width - 20f, cardRect.height - 16f);

            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            Rect titleRect = new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, 20f);
            GUI.Label(titleRect, $"{title}  |  Tier {tier}/{maxTier}", headingStyle);

            Rect descRect = new Rect(innerRect.x, innerRect.y + 30f, innerRect.width, 18f);
            GUI.Label(descRect, description, journalSubtitleStyle);

            float barHeight = Mathf.Clamp(10f * currentUiScale, 8f, 13f);
            Rect barRect = new Rect(innerRect.x, innerRect.y + 54f, innerRect.width, barHeight);
            GUI.DrawTexture(barRect, skillBarBgTexture);

            float segmentGap = 2f;
            float segmentWidth = (barRect.width - (segmentGap * (maxTier - 1))) / maxTier;

            for (int i = 0; i < maxTier; i++)
            {
                Rect segmentRect = new Rect(
                    barRect.x + (i * (segmentWidth + segmentGap)),
                    barRect.y + 1f,
                    segmentWidth,
                    barRect.height - 2f);

                if (i < tier)
                {
                    GUI.DrawTexture(segmentRect, skillBarFillTexture);
                }
                else
                {
                    Color previousColor = GUI.color;
                    GUI.color = new Color(0.18f, 0.18f, 0.16f, 0.75f);
                    GUI.DrawTexture(segmentRect, skillBarBgTexture);
                    GUI.color = previousColor;
                }
            }

            Rect footerRect = new Rect(innerRect.x, innerRect.y + 68f, innerRect.width, 16f);
            GUI.Label(footerRect, $"Progress: {tier}/{maxTier} tiers unlocked", smallMutedStyle);
        }

        private void DrawSkillBranchGrid()
        {
            int columns = 3;
            int rows = 2;
            float gap = Mathf.Clamp(10f * currentUiScale, 8f, 12f);
            float nodeSize = Mathf.Clamp(58f * currentUiScale, 50f, 74f);

            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    Rect nodeRect = GUILayoutUtility.GetRect(nodeSize, nodeSize, GUILayout.Width(nodeSize), GUILayout.Height(nodeSize));
                    GUI.Box(nodeRect, GUIContent.none, hotbarSlotStyle);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }

        private void DrawTimerEntries()
        {
            List<TimerDisplayEntry> entries = GetTimerDisplayEntries();
            if (entries.Count == 0)
            {
                GUILayout.Label("No active player timers yet.", journalSubtitleStyle);
                GUILayout.Space(2f);
                GUILayout.Label("Plant crop seeds to start countdowns.", journalSubtitleStyle);
                return;
            }

            GUILayout.Label("Active Growth", headingStyle);
            GUILayout.Space(6f);

            float barHeight = Mathf.Clamp(11f * currentUiScale, 9f, 14f);

            for (int i = 0; i < entries.Count; i++)
            {
                TimerDisplayEntry entry = entries[i];
                DrawStatBarRow(
                    entry.name,
                    entry.progress,
                    statBarStaminaTexture,
                    $"{entry.timeLabel}  |  {entry.distanceLabel}",
                    barHeight);

                if (i < entries.Count - 1)
                {
                    GUILayout.Space(4f);
                }
            }
        }

        private List<TimerDisplayEntry> GetTimerDisplayEntries()
        {
            List<(float distance, TimerDisplayEntry entry)> entries = new();

            if (playerTransform == null)
            {
                return new List<TimerDisplayEntry>();
            }

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                RenewableNode renewableNode = placedObject != null ? placedObject.GetComponent<RenewableNode>() : null;
                if (placedObject == null || !placedObject.PlacedByPlayer || renewableNode == null || renewableNode.IsHarvestable)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    continue;
                }

                if (item.renewableVisualMode != RenewableVisualMode.Crop)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                float remaining = Mathf.Max(0f, renewableNode.RemainingRegrowSeconds);

                float assumedTotalGrow = Mathf.Max(remaining, 60f);
                float elapsed = Mathf.Max(0f, assumedTotalGrow - remaining);
                float progress = Mathf.Clamp01(elapsed / assumedTotalGrow);

                entries.Add((distance, new TimerDisplayEntry
                {
                    name = item.displayName,
                    progress = progress,
                    timeLabel = FormatDurationShort(remaining),
                    distanceLabel = $"{Mathf.RoundToInt(distance)}m"
                }));
            }

            return entries
                .OrderBy(e => e.distance)
                .Take(6)
                .Select(e => e.entry)
                .ToList();
        }

        private List<string> GetTimerEntries()
        {
            List<(float distance, string text)> entries = new();
            if (playerTransform == null)
            {
                return new List<string>();
            }

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                if (renewableNode == null)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                string regrowState = FormatDurationShort(renewableNode.RemainingRegrowSeconds);
                entries.Add((distance, $"{item.displayName}  |  {regrowState}  |  {Mathf.RoundToInt(distance)}m"));
            }

            return entries
                .OrderBy(entry => entry.distance)
                .Take(6)
                .Select(entry => entry.text)
                .ToList();
        }

        private void DrawSocialEntries()
        {
            GUILayout.Label("No settlers discovered yet.", journalBodyStyle);
            GUILayout.Label("Villagers, factions, and social links will appear here.", journalSubtitleStyle);

            GUILayout.Space(10f);
            GUILayout.Label("Settlement", headingStyle);
            GUILayout.Space(6f);

            float barHeight = Mathf.Clamp(11f * currentUiScale, 9f, 14f);
            int structures = CountPlacedObjects(ItemKind.Structure);
            int harvestables = CountHarvestableRenewables();

            DrawStatBarRow(
                "Structures",
                Mathf.Clamp01(structures / 10f),
                skillBarFillTexture,
                $"{structures}",
                barHeight);

            GUILayout.Space(4f);

            DrawStatBarRow(
                "Harvestable",
                Mathf.Clamp01(harvestables / 8f),
                statBarStaminaTexture,
                $"{harvestables}",
                barHeight);

            GUILayout.Space(8f);
            GUI.DrawTexture(
                GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f)),
                amberAccentTexture);
            GUILayout.Space(6f);

            GUILayout.Label("Tracking", headingStyle);
            GUILayout.Space(2f);
            GUILayout.Label(GetTrackedTargetSummary(), journalBodyStyle);
            GUILayout.Label("Nearby targets and future settlers will surface here.", journalSubtitleStyle);
        }

        private void DrawFavoritesEntries()
        {
            bool hasAnyFavorite = false;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!string.IsNullOrWhiteSpace(favoriteHotbarItemIds[i]))
                {
                    hasAnyFavorite = true;
                    break;
                }
            }

            if (!hasAnyFavorite)
            {
                GUILayout.Label("No favorites pinned yet.", journalBodyStyle);
                GUILayout.Space(2f);
                GUILayout.Label("Use the build menu or hotbar favorite flow to pin pieces here.", journalSubtitleStyle);
                return;
            }

            GUILayout.Label("Pinned Hotbar", headingStyle);
            GUILayout.Space(6f);

            int rows = Mathf.CeilToInt(HotbarSlotCount / 2f);
            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();

                DrawFavoriteEntry(row);

                int rightIndex = row + rows;
                if (rightIndex < HotbarSlotCount)
                {
                    GUILayout.Space(10f);
                    DrawFavoriteEntry(rightIndex);
                }

                GUILayout.EndHorizontal();

                if (row < rows - 1)
                {
                    GUILayout.Space(6f);
                }
            }
        }

        private void DrawFavoriteEntry(int slotIndex)
        {
            float rowHeight = Mathf.Clamp(54f * currentUiScale, 46f, 68f);
            Rect rowRect = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, journalCardTexture, 1f);

            Rect innerRect = new Rect(rowRect.x + 8f, rowRect.y + 7f, rowRect.width - 16f, rowRect.height - 14f);

            string itemId = favoriteHotbarItemIds[slotIndex];
            bool isSelectedSlot = slotIndex == selectedHotbarIndex;

            Rect slotBadgeRect = new Rect(innerRect.xMax - 34f, innerRect.y + 2f, 34f, 16f);
            if (isSelectedSlot)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(new Rect(rowRect.x + 1f, rowRect.y + 1f, rowRect.width - 2f, rowRect.height - 2f), modeBadgeTexture);
                GUI.color = previousColor;
            }

            GUI.Label(slotBadgeRect, GetHotbarKeyLabel(slotIndex), smallMutedStyle);

            if (string.IsNullOrWhiteSpace(itemId) || !catalogLookup.TryGetValue(itemId, out BuildCatalogItem item))
            {
                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 40f, 18f), "Empty Slot", headingStyle);
                GUI.Label(
                    new Rect(innerRect.x, innerRect.y + 20f, innerRect.width - 40f, 16f),
                    "No build piece pinned",
                    journalSubtitleStyle);
                return;
            }

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 40f, 18f), item.displayName, headingStyle);
            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 20f, innerRect.width - 40f, 16f),
                $"{item.category}  |  {GetItemKindLabel(item.kind)}",
                journalSubtitleStyle);

            if (isSelectedSlot)
            {
                GUI.Label(
                    new Rect(innerRect.x, innerRect.y + 34f, innerRect.width - 40f, 14f),
                    "Selected on hotbar",
                    smallMutedStyle);
            }
        }

        private int CountFavoritedSlots()
        {
            int count = 0;

            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(favoriteHotbarItemIds[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private string GetTrackedTargetSummary()
        {
            if (!TryGetTrackedTarget(out _, out BuildCatalogItem trackedItem, out float distance) || trackedItem == null)
            {
                return "No nearby target";
            }

            string typeLabel = GetItemKindLabel(trackedItem.kind);
            return $"{trackedItem.displayName}  |  {typeLabel}  |  {Mathf.RoundToInt(distance)}m";
        }

        private string FormatDurationShort(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));

            if (totalSeconds < 60)
            {
                return $"{totalSeconds}s";
            }

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
            {
                return secs > 0
                    ? $"{hours}h {minutes}m {secs}s"
                    : $"{hours}h {minutes}m";
            }

            return secs > 0
                ? $"{minutes}m {secs}s"
                : $"{minutes}m";
        }

        private void DrawInventoryEntries(Dictionary<string, int> inventory)
        {
            if (inventory.Count == 0)
            {
                GUILayout.Label("None", journalSubtitleStyle);
                return;
            }

            foreach (KeyValuePair<string, int> entry in inventory.OrderBy(pair => pair.Key))
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                GUILayout.Label($"{GetDisplayNameForItemId(entry.Key)} x{entry.Value}", journalBodyStyle);
            }
        }

        private bool TryGetTrackedTarget(out PlacedWorldObject trackedObject, out BuildCatalogItem trackedItem, out float trackedDistance)
        {
            trackedObject = null;
            trackedItem = null;
            trackedDistance = 0f;

            if (playerTransform == null || placedObjects == null || placedObjects.Count == 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                if (placedObject == null)
                {
                    continue;
                }

                if (!catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item) || item == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, placedObject.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    trackedObject = placedObject;
                    trackedItem = item;
                    trackedDistance = distance;
                }
            }

            return trackedObject != null && trackedItem != null;
        }

        private int CountPlacedObjects(ItemKind itemKind)
        {
            return placedObjects.Values.Count(placedObject => placedObject != null && placedObject.ItemKind == itemKind);
        }

        private int CountHarvestableRenewables()
        {
            int count = 0;

            foreach (PlacedWorldObject placedObject in placedObjects.Values)
            {
                if (placedObject == null)
                {
                    continue;
                }

                RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                if (renewableNode != null && renewableNode.IsHarvestable)
                {
                    count++;
                }
            }

            return count;
        }

        private string GetFacingLabel()
        {
            if (playerTransform == null)
            {
                return "Unknown";
            }

            float yaw = playerTransform.eulerAngles.y;
            return yaw switch
            {
                >= 315f or < 45f => "North",
                >= 45f and < 135f => "East",
                >= 135f and < 225f => "South",
                _ => "West"
            };
        }

        private void ApplyCameraFeel()
        {
            if (followCamera == null)
            {
                return;
            }

            float baseFollowSmoothTime;
            float baseRotationSmoothSpeed;

            switch (cameraFeelMode)
            {
                case CameraFeelMode.Calm:
                    baseFollowSmoothTime = 0.05f;
                    baseRotationSmoothSpeed = 24f;
                    break;

                case CameraFeelMode.Normal:
                    baseFollowSmoothTime = 0.035f;
                    baseRotationSmoothSpeed = 30f;
                    break;

                case CameraFeelMode.Responsive:
                    baseFollowSmoothTime = 0.025f;
                    baseRotationSmoothSpeed = 36f;
                    break;

                default:
                    baseFollowSmoothTime = 0.035f;
                    baseRotationSmoothSpeed = 30f;
                    break;
            }

            float smoothing = Mathf.Clamp(cameraSmoothingMultiplier, 0.6f, 1.6f);
            followCamera.ApplyRuntimeTuning(
                baseFollowSmoothTime * smoothing,
                baseRotationSmoothSpeed / smoothing);
        }

        private string GetContextPrompt()
        {
            if (buildPanelOpen)
            {
                string modeName = devBuildMode ? "DEV Build" : "Resource Build";
                BuildCatalogItem selectedItem = GetSelectedItem();
                string selectedName = selectedItem != null ? selectedItem.displayName : "None";
                return $"Build Menu  |  {modeName}  |  {selectedName}\nA/D tab  W/S item  F favorite  Enter/LMB choose  L toggle mode  B/Esc close";
            }

            if (placementActive && plantingMode)
            {
                string seedName = GetDisplayNameForItemId(plantingSeedId);
                storedFoodInventory.TryGetValue(plantingSeedId, out int seedsLeft);
                string validity = placementHasValidTarget ? "Ready to plant." : "Move away from the player.";
                return $"Planting {seedName}  |  {seedsLeft} remaining\n{validity}  LMB/Enter plant  R rotate  Esc/RMB cancel";
            }

            if (placementActive)
            {
                BuildCatalogItem selectedItem = GetSelectedItem();
                if (selectedItem == null)
                {
                    return "Choose a build item from the panel.";
                }

                string buildCost = devBuildMode ? "Free in DEV Build Mode" : selectedItem.cost.ToDisplayString();
                string validity = placementHasValidTarget ? "Placement ready." : "Move away from the player.";
                return $"{selectedItem.displayName}  |  {buildCost}\n{validity}  LMB/Enter place  R rotate  G snap  X delete aimed  Esc/RMB cancel";
            }

            if (deleteMode)
            {
                if (targetedPlacedObject != null && catalogLookup.TryGetValue(targetedPlacedObject.CatalogItemId, out BuildCatalogItem item))
                {
                    return $"Delete {item.displayName}  |  Refund: {item.cost.ToDisplayString()}\nLMB or X to remove  |  Esc/RMB exit delete mode";
                }

                return "Delete Tool active.\nAim at a placed object and press LMB or X to remove it.";
            }

            if (targetedPickup != null)
            {
                float holdPercent = GetInteractHoldPercent();
                string progressText = holdPercent > 0f ? $" ({Mathf.RoundToInt(holdPercent * 100f)}%)" : string.Empty;
                string stackHint = targetedPickup.StackCount > 1 ? $"  ({targetedPickup.StackCount} on ground)" : string.Empty;
                return $"Hold Interact to {targetedPickup.GetInteractionLabel()}{progressText}{stackHint}";
            }

            if (targetedPlacedObject != null && TryGetContainerDefinition(targetedPlacedObject, out string containerLabel, out int capacity))
            {
                ContainerRuntimeStorage storage = GetOrCreateContainerStorage(targetedPlacedObject.UniqueId);
                int used = GetContainerUsedCapacity(storage);
                return $"Press Interact to open {containerLabel} storage ({used}/{capacity})";
            }

            if (targetedRenewable != null)
            {
                if (targetedRenewable.IsHarvestable)
                {
                    float holdPercent = GetInteractHoldPercent();
                    string progressText = holdPercent > 0f ? $" ({Mathf.RoundToInt(holdPercent * 100f)}%)" : string.Empty;
                    return $"Hold Interact to gather +{targetedRenewable.YieldAmount} {targetedRenewable.ResourceType.ToString().ToLowerInvariant()}{progressText}";
                }

                string label = targetedRenewable.GetStatusLabel();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }

            return string.Empty;
        }

        private IEnumerable<string> GetActionPanelLines()
        {
            if (buildPanelOpen)
            {
                yield return "Build menu active";
                yield return "A/D tabs | W/S items";
                yield return "Enter choose | F favorite | L mode";
                yield break;
            }

            if (placementActive && plantingMode)
            {
                yield return "Planting mode";
                yield return "Enter/LMB plant | R rotate | Esc cancel";
                yield break;
            }

            if (placementActive)
            {
                yield return "Placement active";
                yield return "Enter/LMB place | R rotate | G snap";
                yield return "Wheel/1-0 slot | Esc cancel";
                yield break;
            }

            if (deleteMode)
            {
                yield return "Delete tool active";
                yield return "Aim at a placed object";
                yield return "LMB remove | X exit";
                yield break;
            }

            if (targetedPickup != null || targetedRenewable != null)
            {
                yield return "Gather or collect";
                yield return "Hold Interact to use target";
                yield return "B build | Wheel/1-0 hotbar";
                yield break;
            }

            yield return "Explore and build";
            yield return "B build | X delete | H heal | P plant";
            yield return "Wheel or 1-0 selects hotbar";
        }

        private void DrawCategoryButton(BuildCategory category)
        {
            bool isSelected = selectedCategory == category;
            string label = category.ToString();

            float buttonHeight = Mathf.Clamp(36f * currentUiScale, 30f, 48f);
            float minWidth = Mathf.Clamp(96f * currentUiScale, 86f, 140f);

            Rect buttonRect = GUILayoutUtility.GetRect(
                minWidth,
                buttonHeight,
                GUILayout.MinWidth(minWidth),
                GUILayout.Height(buttonHeight));

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.18f);
                GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y + 1f, buttonRect.width, buttonRect.height - 2f), modeBadgeTexture);
                GUI.color = previousColor;
            }

            GUIStyle style = isSelected ? journalActiveTabStyle : journalTabStyle;
            if (GUI.Button(buttonRect, label, style))
            {
                selectedCategory = category;
                selectedIndex = 0;
            }

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.95f);
                GUI.DrawTexture(new Rect(buttonRect.x + 10f, buttonRect.yMax - 2f, buttonRect.width - 20f, 2f), amberAccentTexture);
                GUI.color = previousColor;
            }

            GUILayout.Space(6f);
        }

        private void DrawResourceBadge(string text, float width, float height)
        {
            Rect badgeRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            DrawBorderPanel(badgeRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(badgeRect.x + 8f, badgeRect.y + 4f, badgeRect.width - 16f, badgeRect.height - 8f);

            if (text.Contains(":"))
            {
                string[] parts = text.Split(':');
                string left = parts.Length > 0 ? parts[0].Trim() : text;
                string right = parts.Length > 1 ? string.Join(":", parts.Skip(1)).Trim() : string.Empty;

                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width * 0.52f, innerRect.height), left, smallMutedStyle);
                GUI.Label(new Rect(innerRect.x + (innerRect.width * 0.5f), innerRect.y, innerRect.width * 0.5f, innerRect.height), right, labelStyle);
            }
            else
            {
                GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, innerRect.height), text, resourceBadgeStyle);
            }
        }

        private void DrawStatBar(Rect rect, float fraction, Texture2D fillTexture, string label, string valueText)
        {
            fraction = Mathf.Clamp01(fraction);

            GUI.DrawTexture(rect, statBarBgTexture);

            Rect innerRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            float fillWidth = innerRect.width * fraction;

            if (fillWidth > 0f)
            {
                Texture2D textureToUse = fillTexture != null ? fillTexture : statBarHpTexture;
                GUI.DrawTexture(new Rect(innerRect.x, innerRect.y, fillWidth, innerRect.height), textureToUse);
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                GUI.Label(
                    new Rect(rect.x + 6f, rect.y - 1f, rect.width * 0.5f, rect.height + 2f),
                    label,
                    smallMutedStyle);
            }

            if (!string.IsNullOrWhiteSpace(valueText))
            {
                GUI.Label(
                    new Rect(rect.x + (rect.width * 0.45f), rect.y - 1f, rect.width * 0.5f - 6f, rect.height + 2f),
                    valueText,
                    labelStyle);
            }
        }

        private void DrawStatBarRow(string label, float fraction, Texture2D fillTexture, string valueText, float barHeight)
        {
            float rowHeight = Mathf.Max(barHeight + 6f, Mathf.RoundToInt(20f * currentUiScale));
            Rect rowRect = GUILayoutUtility.GetRect(
                10f, rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            float labelWidth = Mathf.Clamp(rowRect.width * 0.22f, 52f, 100f);
            float valueWidth = Mathf.Clamp(rowRect.width * 0.28f, 60f, 140f);
            float gap = Mathf.RoundToInt(10f * currentUiScale);

            GUI.Label(
                new Rect(rowRect.x, rowRect.y - 1f, labelWidth, rowRect.height + 2f),
                label,
                smallMutedStyle);

            GUI.Label(
                new Rect(rowRect.xMax - valueWidth, rowRect.y - 1f, valueWidth, rowRect.height + 2f),
                valueText,
                valueLabelStyle);

            Rect barRect = new Rect(
                rowRect.x + labelWidth + gap,
                rowRect.y + Mathf.Max(0f, (rowRect.height - barHeight) * 0.5f),
                rowRect.width - labelWidth - valueWidth - (gap * 2f),
                barHeight);

            Color prev = GUI.color;

            GUI.color = new Color(0.039f, 0.047f, 0.027f, 0.92f);
            GUI.DrawTexture(barRect, statBarBgTexture);

            float fillWidth = (barRect.width - 2f) * Mathf.Clamp01(fraction);
            if (fillWidth > 0f)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(
                    new Rect(barRect.x + 1f, barRect.y + 1f, fillWidth, barRect.height - 2f),
                    fillTexture != null ? fillTexture : statBarHpTexture);
            }

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.10f);
            GUI.DrawTexture(
                new Rect(barRect.x, barRect.y, barRect.width, 1f),
                amberAccentTexture);

            GUI.color = prev;
        }

        private void DrawBorderedPanel(Rect rect, Action drawer)
        {
            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.32f);
            GUI.DrawTexture(
                new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height),
                panelTexture);

            GUI.color = new Color(0.067f, 0.078f, 0.043f, 0.94f);
            GUI.DrawTexture(rect, hudCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.25f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), amberAccentTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.50f);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width, 1f),
                amberAccentTexture);

            GUI.color = prev;

            float padH = Mathf.RoundToInt(12f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect contentRect = new Rect(
                rect.x + padH,
                rect.y + padV,
                rect.width - padH * 2f,
                rect.height - padV * 2f);

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawQuestRow(string label, bool complete, string progress)
        {
            float rowHeight = Mathf.RoundToInt(26f * currentUiScale);
            Rect rowRect = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));

            if (complete)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.2f, 0.32f, 0.18f, 0.25f);
                GUI.DrawTexture(rowRect, panelTexture);
                GUI.color = previousColor;
            }

            float checkSize = Mathf.RoundToInt(14f * currentUiScale);
            Rect checkRect = new(rowRect.x + 8f, rowRect.y + ((rowHeight - checkSize) * 0.5f), checkSize, checkSize);

            if (complete)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.42f, 0.72f, 0.38f, 0.98f);
                GUI.DrawTexture(checkRect, questCheckTexture);
                GUI.color = previousColor;
            }
            else
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.3f, 0.32f, 0.26f, 0.6f);
                GUI.DrawTexture(checkRect, panelTexture);
                GUI.color = previousColor;
            }

            GUIStyle textStyle = complete ? questDoneStyle : questPendingStyle;
            Rect labelRect = new(checkRect.xMax + 8f, rowRect.y, rowRect.width - checkRect.xMax - 80f, rowHeight);
            GUI.Label(labelRect, label, textStyle);

            Rect progressRect = new(rowRect.xMax - 70f, rowRect.y, 62f, rowHeight);
            GUI.Label(progressRect, progress, valueLabelStyle);

            GUILayout.Space(2f);
        }

        private void DrawBorderPanel(Rect rect, Texture2D fillTexture, float borderThickness)
        {
            Color prev = GUI.color;

            GUI.color = new Color(0.067f, 0.078f, 0.043f, 0.95f);
            GUI.DrawTexture(rect, fillTexture != null ? fillTexture : panelTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.22f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderThickness), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderThickness, rect.width, borderThickness), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, borderThickness, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - borderThickness, rect.y, borderThickness, rect.height), amberAccentTexture);

            GUI.color = prev;
        }

        private void EnsureGuiStyles()
        {
            float targetUiScale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.75f, 1.6f);

            if (windowStyle != null)
            {
                if (Mathf.Abs(targetUiScale - currentUiScale) > 0.01f)
                {
                    ApplyGuiScale(targetUiScale);
                }

                return;
            }

            EnsureGuiTextures();

            windowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12),
                fontSize = 12,
                border = new RectOffset(1, 1, 1, 1),
                normal =
                {
                    background = panelTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                }
            };

            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.910f, 0.875f, 0.753f) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            smallMutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal =
                {
                    background = buttonTexture,
                    textColor = new Color(0.9f, 0.92f, 0.84f)
                },
                hover =
                {
                    background = chipTexture,
                    textColor = Color.white
                },
                active =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                }
            };

            hotbarSlotStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4),
                fontSize = 11,
                normal =
                {
                    background = hotbarTexture,
                    textColor = new Color(0.85f, 0.88f, 0.8f)
                }
            };

            hotbarSelectedSlotStyle = new GUIStyle(hotbarSlotStyle)
            {
                normal =
                {
                    background = hotbarSelectedTexture,
                    textColor = new Color(0.98f, 0.94f, 0.72f)
                }
            };

            hotbarKeyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.96f, 0.88f) }
            };

            tabButtonStyle = new GUIStyle(buttonStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            selectedTabButtonStyle = new GUIStyle(buttonStyle)
            {
                normal =
                {
                    background = chipTexture,
                    textColor = new Color(0.98f, 0.97f, 0.82f)
                }
            };

            catalogItemButtonStyle = new GUIStyle(buttonStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8)
            };

            selectedCatalogItemButtonStyle = new GUIStyle(catalogItemButtonStyle)
            {
                normal =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                },
                hover =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                },
                active =
                {
                    background = buttonSelectedTexture,
                    textColor = new Color(0.15f, 0.14f, 0.08f)
                }
            };

            resourceBadgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 4, 4),
                normal =
                {
                    background = chipTexture,
                    textColor = new Color(0.93f, 0.94f, 0.86f)
                }
            };

            promptLabelStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            journalShellStyle = new GUIStyle(windowStyle)
            {
                normal =
                {
                    background = journalShellTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                },
                padding = new RectOffset(20, 20, 18, 18)
            };

            journalSectionStyle = new GUIStyle(windowStyle)
            {
                normal =
                {
                    background = journalCardTexture,
                    textColor = new Color(0.91f, 0.92f, 0.86f)
                },
                padding = new RectOffset(14, 14, 12, 12)
            };

            journalTitleStyle = new GUIStyle(headingStyle)
            {
                fontSize = 24,
                normal = { textColor = new Color(0.910f, 0.875f, 0.753f) }
            };

            journalSubtitleStyle = new GUIStyle(smallMutedStyle)
            {
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            journalTabStyle = new GUIStyle(buttonStyle)
            {
                normal =
                {
                    background = journalTabTexture,
                    textColor = new Color(0.353f, 0.333f, 0.251f)
                },
                hover =
                {
                    background = chipTexture,
                    textColor = new Color(0.788f, 0.659f, 0.298f)
                }
            };

            journalActiveTabStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    background = journalTabActiveTexture,
                    textColor = new Color(0.910f, 0.796f, 0.471f)
                },
                hover =
                {
                    background = journalTabActiveTexture,
                    textColor = new Color(0.910f, 0.796f, 0.471f)
                }
            };

            journalBodyStyle = new GUIStyle(labelStyle)
            {
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            modeLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.910f, 0.796f, 0.471f) }
            };

            keyHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.64f, 0.54f) }
            };

            statLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            valueLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            settingsRowLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.847f, 0.824f, 0.706f) }
            };

            toggleOnStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = toggleOnTexture,
                    textColor = new Color(0.98f, 0.98f, 0.95f)
                }
            };

            toggleOffStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = toggleOffTexture,
                    textColor = new Color(0.58f, 0.6f, 0.52f)
                }
            };

            questDoneStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.788f, 0.659f, 0.298f) }
            };

            questPendingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.604f, 0.565f, 0.439f) }
            };

            hudCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                border = new RectOffset(1, 1, 1, 1),
                normal =
                {
                    background = hudCardTexture,
                    textColor = new Color(0.93f, 0.93f, 0.88f)
                }
            };

            escHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.353f, 0.333f, 0.251f) }
            };

            ApplyGuiScale(targetUiScale);
        }

        private void ApplyGuiScale(float targetUiScale)
        {
            currentUiScale = targetUiScale;

            windowStyle.padding = new RectOffset(
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale));

            headingStyle.fontSize = Mathf.RoundToInt(15f * targetUiScale);
            labelStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            smallMutedStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            buttonStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            tabButtonStyle.fontSize = buttonStyle.fontSize;
            selectedTabButtonStyle.fontSize = buttonStyle.fontSize;
            catalogItemButtonStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            selectedCatalogItemButtonStyle.fontSize = catalogItemButtonStyle.fontSize;
            catalogItemButtonStyle.padding = new RectOffset(
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale),
                Mathf.RoundToInt(8f * targetUiScale));
            selectedCatalogItemButtonStyle.padding = catalogItemButtonStyle.padding;

            hotbarSlotStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            hotbarSlotStyle.padding = new RectOffset(
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale));

            hotbarSelectedSlotStyle.fontSize = hotbarSlotStyle.fontSize;
            hotbarSelectedSlotStyle.padding = hotbarSlotStyle.padding;
            hotbarKeyStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            resourceBadgeStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            resourceBadgeStyle.padding = new RectOffset(
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(6f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale),
                Mathf.RoundToInt(4f * targetUiScale));
            promptLabelStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            journalShellStyle.padding = new RectOffset(
                Mathf.RoundToInt(20f * targetUiScale),
                Mathf.RoundToInt(20f * targetUiScale),
                Mathf.RoundToInt(18f * targetUiScale),
                Mathf.RoundToInt(18f * targetUiScale));
            journalSectionStyle.padding = new RectOffset(
                Mathf.RoundToInt(14f * targetUiScale),
                Mathf.RoundToInt(14f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale));
            journalTitleStyle.fontSize = Mathf.RoundToInt(22f * targetUiScale);
            journalSubtitleStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            journalTabStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            journalActiveTabStyle.fontSize = journalTabStyle.fontSize;
            journalBodyStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);

            modeLabelStyle.fontSize = Mathf.RoundToInt(13f * targetUiScale);
            keyHintStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            statLabelStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            valueLabelStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
            settingsRowLabelStyle.fontSize = Mathf.RoundToInt(13f * targetUiScale);
            toggleOnStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            toggleOffStyle.fontSize = Mathf.RoundToInt(11f * targetUiScale);
            questDoneStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            questPendingStyle.fontSize = Mathf.RoundToInt(12f * targetUiScale);
            hudCardStyle.padding = new RectOffset(
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(12f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale),
                Mathf.RoundToInt(10f * targetUiScale));
            escHintStyle.fontSize = Mathf.RoundToInt(10f * targetUiScale);
        }

        private string GetBuildMenuItemSubtitle(BuildCatalogItem item, string costLabel)
        {
            if (item == null)
            {
                return string.Empty;
            }

            string kindLabel = GetItemKindLabel(item.kind);
            string subtitle = $"{kindLabel}  |  {costLabel}";

            if (item.renewableVisualMode == RenewableVisualMode.Crop)
            {
                subtitle += "  |  Crop";
            }

            return subtitle;
        }

        private void DrawPlacementPrompt()
        {
            if (!placementActive)
            {
                return;
            }

            BuildCatalogItem selectedItem = GetSelectedItem();
            string title = selectedItem != null ? selectedItem.displayName : "Placement";
            string subtitle = placementHasValidTarget
                ? "Ready to place"
                : "Invalid position";

            string controls = placementSnapEnabled
                ? "Enter / LMB place  •  R rotate  •  G snap"
                : "Enter / LMB place  •  R rotate  •  Esc cancel";

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.contextRect, $"{title}\n{subtitle}\n{controls}", promptLabelStyle);

            DrawBorderPanel(rect, hudCardTexture, 1f);

            Rect innerRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 18f), title, headingStyle);

            Color previousColor = GUI.color;
            GUI.color = placementHasValidTarget
                ? new Color(0.62f, 0.82f, 0.48f, 0.9f)
                : new Color(0.84f, 0.32f, 0.28f, 0.9f);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + 22f, innerRect.width, 2f), amberAccentTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(innerRect.x, innerRect.y + 30f, innerRect.width, 16f), subtitle, labelStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + 48f, innerRect.width, 18f), controls, smallMutedStyle);
        }

        private string GetItemKindLabel(ItemKind kind)
        {
            return kind switch
            {
                ItemKind.Pickup => "Pickup",
                ItemKind.RenewableNode => "Renewable",
                _ => "Structure"
            };
        }

        private Texture GetBuildPreviewTexture(BuildCatalogItem item)
        {
            if (item == null || item.prefab == null)
            {
                CleanupPreviewInstance();
                previewCatalogId = string.Empty;
                return null;
            }

            EnsurePreviewStage();
            if (previewCamera == null || previewRenderTexture == null)
            {
                return null;
            }

            if (!string.Equals(previewCatalogId, item.id, StringComparison.Ordinal))
            {
                RebuildPreviewInstance(item);
            }

            if (previewInstance == null)
            {
                return null;
            }

            QueuePreviewRender();
            return previewRenderTexture;
        }

        private void EnsurePreviewStage()
        {
            if (previewStageRoot == null)
            {
                previewStageRoot = new GameObject("Build Preview Stage")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                previewStageRoot.transform.position = new Vector3(0f, -2000f, 0f);
            }

            if (previewRenderTexture == null)
            {
                previewRenderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "MP Settlers Build Preview"
                };
                previewRenderTexture.Create();
            }

            if (previewCamera == null)
            {
                GameObject cameraObject = new("Build Preview Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                cameraObject.transform.SetParent(previewStageRoot.transform, false);
                previewCamera = cameraObject.AddComponent<Camera>();
                previewCamera.enabled = false;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0.13f, 0.15f, 0.1f, 1f);
                previewCamera.fieldOfView = 26f;
                previewCamera.nearClipPlane = 0.01f;
                previewCamera.farClipPlane = 100f;
                previewCamera.allowHDR = false;
                previewCamera.allowMSAA = true;
                previewCamera.cullingMask = 1 << PreviewLayer;
            }

            if (previewLight == null)
            {
                GameObject lightObject = new("Build Preview Light")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                lightObject.transform.SetParent(previewStageRoot.transform, false);
                previewLight = lightObject.AddComponent<Light>();
                previewLight.type = LightType.Directional;
                previewLight.intensity = 1.15f;
                previewLight.color = new Color(1f, 0.97f, 0.91f);
                previewLight.shadows = LightShadows.None;
                previewLight.cullingMask = 1 << PreviewLayer;
                previewLight.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            }

            ExcludePreviewLayerFromMainCamera();
        }

        private void RebuildPreviewInstance(BuildCatalogItem item)
        {
            CleanupPreviewInstance();
            previewCatalogId = item.id;

            previewInstance = Instantiate(item.prefab, previewStageRoot.transform);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            previewInstance.name = $"{item.prefab.name}_Preview";
            previewInstance.transform.position = previewStageRoot.transform.position;
            previewInstance.transform.rotation = Quaternion.Euler(0f, 24f, 0f);
            SetLayerRecursively(previewInstance.transform, PreviewLayer);
            DisableComponentsForPreview(previewInstance);
            AlignObjectToGround(previewInstance);
            PositionPreviewCamera(previewInstance);
            QueuePreviewRender();
        }

        private void DisableComponentsForPreview(GameObject instanceObject)
        {
            foreach (MonoBehaviour behaviour in instanceObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Animator animator in instanceObject.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }

            foreach (Collider colliderComponent in instanceObject.GetComponentsInChildren<Collider>(true))
            {
                colliderComponent.enabled = false;
            }

            foreach (CharacterController controller in instanceObject.GetComponentsInChildren<CharacterController>(true))
            {
                controller.enabled = false;
            }

            foreach (Rigidbody rigidbodyComponent in instanceObject.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbodyComponent.isKinematic = true;
                rigidbodyComponent.useGravity = false;
            }

            foreach (Light lightComponent in instanceObject.GetComponentsInChildren<Light>(true))
            {
                lightComponent.enabled = false;
            }

            foreach (AudioSource audioSource in instanceObject.GetComponentsInChildren<AudioSource>(true))
            {
                audioSource.enabled = false;
            }

            foreach (Canvas canvas in instanceObject.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = false;
            }
        }

        private void PositionPreviewCamera(GameObject instanceObject)
        {
            if (previewCamera == null || instanceObject == null || !TryGetRenderableBounds(instanceObject, out Bounds bounds))
            {
                return;
            }

            Vector3 focusPoint = bounds.center + Vector3.up * (bounds.size.y * 0.08f);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            float distance = Mathf.Clamp(radius / Mathf.Sin(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad), 2.5f, 18f);
            Vector3 viewDirection = new Vector3(-0.58f, 0.3f, -1f).normalized;
            previewCamera.transform.position = focusPoint - (viewDirection * distance);
            previewCamera.transform.LookAt(focusPoint);
        }

        private void ExcludePreviewLayerFromMainCamera()
        {
            if (mainCamera != null)
            {
                mainCamera.cullingMask &= ~(1 << PreviewLayer);
            }
        }

        private void EnsureGuiTextures()
        {
            panelTexture ??= CreateSolidTexture(new Color(0.040f, 0.051f, 0.031f, 0.88f));
            buttonTexture ??= CreateSolidTexture(new Color(0.086f, 0.102f, 0.055f, 0.92f));
            buttonSelectedTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.96f));
            hotbarTexture ??= CreateSolidTexture(new Color(0.078f, 0.094f, 0.055f, 0.92f));
            hotbarSelectedTexture ??= CreateSolidTexture(new Color(0.224f, 0.212f, 0.098f, 0.98f));
            chipTexture ??= CreateSolidTexture(new Color(0.145f, 0.176f, 0.078f, 0.95f));
            crosshairTexture ??= CreateSolidTexture(Color.white);
            journalShellTexture ??= CreateSolidTexture(new Color(0.040f, 0.051f, 0.031f, 0.97f));
            journalCardTexture ??= CreateSolidTexture(new Color(0.086f, 0.102f, 0.055f, 0.92f));
            journalTabTexture ??= CreateSolidTexture(new Color(0.067f, 0.078f, 0.043f, 0.98f));
            journalTabActiveTexture ??= CreateSolidTexture(new Color(0.145f, 0.176f, 0.078f, 0.99f));
            journalAccentTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 1.00f));
            statBarBgTexture ??= CreateSolidTexture(new Color(0.039f, 0.047f, 0.027f, 0.92f));
            statBarHpTexture ??= CreateSolidTexture(new Color(0.769f, 0.267f, 0.220f, 0.98f));
            statBarStaminaTexture ??= CreateSolidTexture(new Color(0.353f, 0.541f, 0.235f, 0.98f));
            statBarHungerTexture ??= CreateSolidTexture(new Color(0.545f, 0.376f, 0.141f, 0.98f));
            statBarThirstTexture ??= CreateSolidTexture(new Color(0.290f, 0.494f, 0.659f, 0.98f));
            amberAccentTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 1.00f));
            borderDarkTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.20f));
            hudCardTexture ??= CreateSolidTexture(new Color(0.067f, 0.078f, 0.043f, 0.90f));
            modeBadgeTexture ??= CreateSolidTexture(new Color(0.110f, 0.133f, 0.071f, 0.96f));
            toggleOnTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.25f));
            toggleOffTexture ??= CreateSolidTexture(new Color(0.118f, 0.133f, 0.078f, 0.92f));
            questCheckTexture ??= CreateSolidTexture(new Color(0.788f, 0.659f, 0.298f, 0.98f));
            skillBarBgTexture ??= CreateSolidTexture(new Color(0.063f, 0.071f, 0.039f, 0.88f));
            skillBarFillTexture ??= CreateSolidTexture(new Color(0.545f, 0.427f, 0.173f, 0.96f));
        }

        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void CleanupGuiTextures()
        {
            DestroyTexture(panelTexture);
            DestroyTexture(buttonTexture);
            DestroyTexture(buttonSelectedTexture);
            DestroyTexture(hotbarTexture);
            DestroyTexture(hotbarSelectedTexture);
            DestroyTexture(chipTexture);
            DestroyTexture(crosshairTexture);
            DestroyTexture(journalShellTexture);
            DestroyTexture(journalCardTexture);
            DestroyTexture(journalTabTexture);
            DestroyTexture(journalTabActiveTexture);
            DestroyTexture(journalAccentTexture);
            DestroyTexture(statBarBgTexture);
            DestroyTexture(statBarHpTexture);
            DestroyTexture(statBarStaminaTexture);
            DestroyTexture(statBarHungerTexture);
            DestroyTexture(statBarThirstTexture);
            DestroyTexture(amberAccentTexture);
            DestroyTexture(borderDarkTexture);
            DestroyTexture(hudCardTexture);
            DestroyTexture(modeBadgeTexture);
            DestroyTexture(toggleOnTexture);
            DestroyTexture(toggleOffTexture);
            DestroyTexture(questCheckTexture);
            DestroyTexture(skillBarBgTexture);
            DestroyTexture(skillBarFillTexture);
        }

        private void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        private void QueuePreviewRender()
        {
            if (previewRenderQueued || !isActiveAndEnabled || previewCamera == null || previewRenderTexture == null)
            {
                return;
            }

            previewRenderQueued = true;
            StartCoroutine(RenderPreviewNextFrame());
        }

        private IEnumerator RenderPreviewNextFrame()
        {
            yield return new WaitForEndOfFrame();
            previewRenderQueued = false;

            if (!buildPanelOpen || previewCamera == null || previewRenderTexture == null || previewInstance == null)
            {
                yield break;
            }

            UniversalRenderPipeline.SingleCameraRequest request = new()
            {
                destination = previewRenderTexture
            };

            if (RenderPipeline.SupportsRenderRequest(previewCamera, request))
            {
                RenderPipeline.SubmitRenderRequest(previewCamera, request);
            }
        }

        private void CleanupPreviewResources()
        {
            CleanupPreviewInstance();
            previewRenderQueued = false;

            if (previewRenderTexture != null)
            {
                previewRenderTexture.Release();
                Destroy(previewRenderTexture);
                previewRenderTexture = null;
            }

            if (previewStageRoot != null)
            {
                Destroy(previewStageRoot);
                previewStageRoot = null;
            }

            previewCamera = null;
            previewLight = null;
            previewCatalogId = string.Empty;
        }

        private void CleanupPreviewInstance()
        {
            if (previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
            }
        }

    }
}
