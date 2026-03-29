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
        private void DrawEscMenu()
        {
            float screenW = Screen.width;
            float screenH = Screen.height;

            // Dim overlay
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, screenW, screenH), panelTexture);

            // Centered panel
            float panelW = Mathf.Clamp(340f * currentUiScale, 280f, 480f);
            float buttonH = Mathf.Clamp(42f * currentUiScale, 36f, 58f);
            float buttonGap = Mathf.RoundToInt(10f * currentUiScale);
            float titleH = Mathf.Clamp(48f * currentUiScale, 40f, 66f);
            float innerPad = Mathf.RoundToInt(24f * currentUiScale);
            int buttonCount = 4;
            float panelH = titleH + innerPad + (buttonH * buttonCount) + (buttonGap * (buttonCount - 1)) + innerPad + innerPad;

            float panelX = (screenW - panelW) * 0.5f;
            float panelY = (screenH - panelH) * 0.5f;
            Rect panelRect = new Rect(panelX, panelY, panelW, panelH);

            // Panel background
            GUI.color = new Color(0.040f, 0.051f, 0.031f, 0.96f);
            GUI.DrawTexture(panelRect, journalShellTexture);

            // Border
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.50f);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.yMax - 1f, panelRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 1f, panelRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(panelRect.xMax - 1f, panelRect.y, 1f, panelRect.height), amberAccentTexture);

            // Top accent
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.92f);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), amberAccentTexture);

            GUI.color = prev;

            // Title
            Rect titleRect = new Rect(panelRect.x, panelRect.y + innerPad, panelRect.width, titleH);
            GUI.Label(titleRect, "PAUSED", journalTitleStyle);

            // Separator under title
            prev = GUI.color;
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.35f);
            GUI.DrawTexture(new Rect(panelRect.x + innerPad, titleRect.yMax, panelRect.width - innerPad * 2f, 1f), amberAccentTexture);
            GUI.color = prev;

            // Buttons area
            float buttonsY = titleRect.yMax + innerPad;
            float buttonX = panelRect.x + innerPad;
            float buttonW = panelRect.width - innerPad * 2f;

            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Resume"))
            {
                CloseEscMenu();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Settings"))
            {
                EscMenuOpenSettings();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Controls / Help"))
            {
                CloseEscMenu();
                selectedInGameMenuTab = InGameMenuTab.Overview;
                OpenInGameMenu();
            }

            buttonsY += buttonH + buttonGap;
            if (DrawEscMenuButton(new Rect(buttonX, buttonsY, buttonW, buttonH), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        private bool DrawEscMenuButton(Rect rect, string label)
        {
            Color prev = GUI.color;
            bool hover = rect.Contains(Event.current.mousePosition);

            // Button background
            GUI.color = hover
                ? new Color(0.788f, 0.659f, 0.298f, 0.22f)
                : new Color(0.10f, 0.12f, 0.08f, 0.80f);
            GUI.DrawTexture(rect, panelTexture);

            // Button border
            GUI.color = hover
                ? new Color(0.788f, 0.659f, 0.298f, 0.60f)
                : new Color(0.788f, 0.659f, 0.298f, 0.25f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), amberAccentTexture);

            GUI.color = prev;

            // Label
            GUI.Label(rect, label, journalTitleStyle);

            // Click detection
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private void DrawHud()
        {
            UiLayout layout = GetUiLayout();
            BuildCatalogItem selectedHotbarItem = GetSelectedHotbarItem();
            float barHeight = Mathf.Clamp(14f * currentUiScale, 11f, 20f);

            DrawBorderedPanel(layout.playerPanelRect, () =>
            {
                GUILayout.Label("Settler", headingStyle);
                GUILayout.Space(4f);

                DrawStatBarRow(
                    "HP",
                    health / (float)MaxHealth,
                    statBarHpTexture,
                    $"{health}/{MaxHealth}",
                    barHeight);

                DrawStatBarRow(
                    "Food",
                    Mathf.Clamp01(storedFoodInventory.Values.Sum() / 10f),
                    statBarHungerTexture,
                    FormatInventorySummary(storedFoodInventory, 2),
                    barHeight);

                DrawStatBarRow(
                    "Arms",
                    Mathf.Clamp01(storedWeaponInventory.Values.Sum() / 6f),
                    statBarStaminaTexture,
                    FormatInventorySummary(storedWeaponInventory, 2),
                    barHeight);
            });

            if (showActionHints)
            {
                DrawBorderedPanel(layout.helpPanelRect, () =>
                {
                    GUILayout.Label("Actions", headingStyle);
                    GUILayout.Space(2f);

                    foreach (string line in GetActionPanelLines())
                    {
                        GUILayout.Label(line, line.Contains('|') ? smallMutedStyle : labelStyle);
                    }
                });
            }

            DrawBorderedPanel(layout.modePanelRect, () =>
            {
                string modeText = deleteMode ? "DELETE" : devBuildMode ? "DEV BUILD" : "RESOURCE";
                string statusText = selectedHotbarItem != null
                    ? selectedHotbarItem.displayName
                    : $"Slot {GetHotbarKeyLabel(selectedHotbarIndex)} Empty";

                string hintText = deleteMode
                    ? "Aim + LMB to remove"
                    : devBuildMode
                        ? "Free placement enabled"
                        : "B build | X delete";

                Color modeColor = deleteMode
                    ? new Color(0.84f, 0.32f, 0.28f)
                    : devBuildMode
                        ? new Color(0.62f, 0.82f, 0.48f)
                        : new Color(0.82f, 0.64f, 0.24f);

                Rect badgeRect = GUILayoutUtility.GetRect(
                    10f,
                    Mathf.RoundToInt(20f * currentUiScale),
                    GUILayout.ExpandWidth(true));

                Color previousColor = GUI.color;
                GUI.color = new Color(modeColor.r, modeColor.g, modeColor.b, 0.22f);
                GUI.DrawTexture(badgeRect, modeBadgeTexture);
                GUI.color = previousColor;

                modeLabelStyle.normal.textColor = modeColor;
                GUI.Label(badgeRect, modeText, modeLabelStyle);

                GUILayout.Space(4f);
                GUILayout.Label(statusText, labelStyle);
                GUILayout.Label(hintText, smallMutedStyle);
            });
        }

        private void DrawPanel(Rect rect, Action drawer)
        {
            GUILayout.BeginArea(rect, windowStyle);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private UiLayout GetUiLayout()
        {
            return new UiLayout(Screen.width, Screen.height);
        }

        private Rect GetTextPanelRect(Rect anchorRect, string text, GUIStyle style)
        {
            float contentWidth = Mathf.Max(64f, anchorRect.width - (windowStyle.padding.left + windowStyle.padding.right));
            float minHeight = anchorRect.height;
            float textHeight = style.CalcHeight(new GUIContent(text), contentWidth);
            float totalHeight = Mathf.Max(minHeight, textHeight + windowStyle.padding.top + windowStyle.padding.bottom);
            float y = anchorRect.yMax - totalHeight;

            return new Rect(anchorRect.x, y, anchorRect.width, totalHeight);
        }

        private void DrawCrosshair()
        {
            if (!showCrosshair || pointerMode || inGameMenuOpen || crosshairTexture == null)
            {
                return;
            }

            float size = Mathf.Clamp(14f * currentUiScale, 11f, 18f);
            float thickness = Mathf.Max(2f, Mathf.Round(2f * currentUiScale));
            float gap = Mathf.Clamp(4f * currentUiScale, 3f, 6f);
            Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);

            Color previousColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.42f);
            DrawCrosshairLine(center.x - (size * 0.5f) + 1f, center.y - (thickness * 0.5f) + 1f, size, thickness);
            DrawCrosshairLine(center.x - (thickness * 0.5f) + 1f, center.y - (size * 0.5f) + 1f, thickness, size);

            GUI.color = new Color(0.98f, 0.97f, 0.85f, 0.95f);
            DrawCrosshairLine(center.x - (size * 0.5f), center.y - (thickness * 0.5f), (size - gap) * 0.5f, thickness);
            DrawCrosshairLine(center.x + (gap * 0.5f), center.y - (thickness * 0.5f), (size - gap) * 0.5f, thickness);
            DrawCrosshairLine(center.x - (thickness * 0.5f), center.y - (size * 0.5f), thickness, (size - gap) * 0.5f);
            DrawCrosshairLine(center.x - (thickness * 0.5f), center.y + (gap * 0.5f), thickness, (size - gap) * 0.5f);

            GUI.color = previousColor;
        }

        private void DrawCrosshairLine(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), crosshairTexture);
        }

        private void DrawHotbarHud()
        {
            float slotSize = Mathf.Clamp(68f * currentUiScale, 54f, 94f);
            float gap = Mathf.Clamp(8f * currentUiScale, 5f, 14f);
            float barHeight = Mathf.Clamp(6f * currentUiScale, 4f, 9f);

            float totalWidth = (slotSize * HotbarSlotCount) + (gap * (HotbarSlotCount - 1));
            Rect areaRect = new Rect(
                (Screen.width - totalWidth) * 0.5f,
                Screen.height - slotSize - Mathf.Clamp(30f * currentUiScale, 22f, 42f),
                totalWidth,
                slotSize + barHeight + 6f);

            GUILayout.BeginArea(areaRect);
            GUILayout.BeginHorizontal();

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                BuildCatalogItem item = null;
                string itemId = favoriteHotbarItemIds[i];
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    catalogLookup.TryGetValue(itemId, out item);
                }

                bool isSelected = i == selectedHotbarIndex;

                GUILayout.BeginVertical(GUILayout.Width(slotSize));

                Rect topBarRect = GUILayoutUtility.GetRect(slotSize, barHeight, GUILayout.Width(slotSize), GUILayout.Height(barHeight));
                GUI.DrawTexture(topBarRect, statBarBgTexture);

                if (item != null)
                {
                    Rect fillRect = new Rect(topBarRect.x + 1f, topBarRect.y + 1f, topBarRect.width - 2f, topBarRect.height - 2f);
                    GUI.DrawTexture(fillRect, statBarHpTexture);
                }

                Rect slotRect = GUILayoutUtility.GetRect(slotSize, slotSize, GUILayout.Width(slotSize), GUILayout.Height(slotSize));

                if (isSelected)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.22f);
                    GUI.DrawTexture(new Rect(slotRect.x - 2f, slotRect.y - 2f, slotRect.width + 4f, slotRect.height + 4f), modeBadgeTexture);
                    GUI.color = prev;
                }

                GUI.Box(slotRect, GUIContent.none, isSelected ? hotbarSelectedSlotStyle : hotbarSlotStyle);

                Rect keyRect = new Rect(slotRect.x + 6f, slotRect.y + 4f, slotRect.width - 12f, 14f);
                GUI.Label(keyRect, GetHotbarKeyLabel(i), hotbarKeyStyle);

                if (item != null)
                {
                    Rect nameRect = new Rect(slotRect.x + 8f, slotRect.y + 22f, slotRect.width - 16f, slotRect.height - 30f);
                    GUI.Label(nameRect, item.displayName, labelStyle);

                    Rect kindRect = new Rect(slotRect.x + 8f, slotRect.yMax - 16f, slotRect.width - 16f, 12f);
                    GUI.Label(kindRect, GetItemKindLabel(item.kind), smallMutedStyle);
                }
                else
                {
                    Rect emptyRect = new Rect(slotRect.x + 8f, slotRect.y + 24f, slotRect.width - 16f, slotRect.height - 28f);
                    GUI.Label(emptyRect, "Empty", smallMutedStyle);
                }

                GUILayout.EndVertical();

                if (i < HotbarSlotCount - 1)
                {
                    GUILayout.Space(gap);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawBuildPanel()
        {
            if (!buildPanelOpen)
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect panelRect = layout.buildPanelRect;
            BuildCatalogItem selectedItem = GetSelectedItem();

            float previewHeight = Mathf.Clamp(panelRect.width * 0.44f, 140f, 260f);
            float detailsHeight = Mathf.Clamp(90f * currentUiScale, 78f, 124f);
            float footerHeight = Mathf.Clamp(50f * currentUiScale, 42f, 64f);
            float listHeight = Mathf.Max(
                130f,
                panelRect.height - previewHeight - detailsHeight - footerHeight - Mathf.Clamp(96f * currentUiScale, 80f, 120f));

            GUILayout.BeginArea(panelRect, windowStyle);

            GUILayout.Label("Build Catalog", headingStyle);
            GUILayout.Space(6f);

            BuildCategory[] availableCategories = GetAvailableBuildCategories();
            GUILayout.BeginHorizontal();
            if (availableCategories.Length == 0)
            {
                GUILayout.Label("No build categories available.", smallMutedStyle);
            }
            else
            {
                foreach (BuildCategory category in availableCategories)
                {
                    DrawCategoryButton(category);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            List<BuildCatalogItem> items = GetItemsForSelectedCategory();
            buildListScroll = GUILayout.BeginScrollView(buildListScroll, GUILayout.Height(listHeight));

            for (int i = 0; i < items.Count; i++)
            {
                BuildCatalogItem item = items[i];
                bool isSelected = selectedCategory == item.category && selectedIndex == i;

                string costLabel = devBuildMode ? "FREE" : item.cost.ToDisplayString();
                if (!devBuildMode && !CanAfford(item.cost))
                {
                    costLabel = $"NEED {item.cost.ToDisplayString()}";
                }

                string subtitle = GetBuildMenuItemSubtitle(item, costLabel);
                string buttonText = $"{item.displayName}\n{subtitle}";
                GUIStyle itemStyle = isSelected ? selectedCatalogItemButtonStyle : catalogItemButtonStyle;

                if (GUILayout.Button(buttonText, itemStyle, GUILayout.Height(layout.buildItemHeight)))
                {
                    SelectItem(item.category, i);
                }
            }

            if (items.Count == 0)
            {
                GUILayout.Label("No catalog items found for this category.", smallMutedStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10f);
            GUILayout.Label("Selected Preview", headingStyle);
            DrawSelectedPreviewCard(selectedItem, previewHeight);

            GUILayout.Space(8f);
            Rect detailsRect = GUILayoutUtility.GetRect(10f, detailsHeight, GUILayout.ExpandWidth(true), GUILayout.Height(detailsHeight));
            DrawBorderPanel(detailsRect, hudCardTexture, 1f);

            Rect detailsInner = new Rect(detailsRect.x + 10f, detailsRect.y + 8f, detailsRect.width - 20f, detailsRect.height - 16f);

            if (selectedItem != null)
            {
                GUI.Label(new Rect(detailsInner.x, detailsInner.y, detailsInner.width, 20f), selectedItem.displayName, headingStyle);
                GUI.Label(
                    new Rect(detailsInner.x, detailsInner.y + 22f, detailsInner.width, 18f),
                    $"{selectedItem.category}  |  {GetItemKindLabel(selectedItem.kind)}",
                    smallMutedStyle);

                string costText = devBuildMode
                    ? "Cost: Free in DEV Build Mode"
                    : $"Cost: {selectedItem.cost.ToDisplayString()}";

                GUI.Label(new Rect(detailsInner.x, detailsInner.y + 42f, detailsInner.width, 18f), costText, labelStyle);

                if (!devBuildMode && !CanAfford(selectedItem.cost))
                {
                    GUI.Label(
                        new Rect(detailsInner.x, detailsInner.y + 60f, detailsInner.width, 18f),
                        "Missing resources. Gather more or toggle DEV Build.",
                        smallMutedStyle);
                }
            }
            else
            {
                GUI.Label(new Rect(detailsInner.x, detailsInner.y, detailsInner.width, 20f), "No item selected", headingStyle);
                GUI.Label(
                    new Rect(detailsInner.x, detailsInner.y + 24f, detailsInner.width, 36f),
                    "Pick an item from the current category to start placement.",
                    smallMutedStyle);
            }

            GUILayout.Space(8f);

            Rect footerRect = GUILayoutUtility.GetRect(10f, footerHeight, GUILayout.ExpandWidth(true), GUILayout.Height(footerHeight));
            DrawBorderPanel(footerRect, hudCardTexture, 1f);

            Rect footerInner = new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, footerRect.height - 16f);
            GUI.Label(
                new Rect(footerInner.x, footerInner.y, footerInner.width, 16f),
                $"Mode: {(devBuildMode ? "DEV Build" : "Resource Build")}  |  L toggles",
                labelStyle);
            GUI.Label(
                new Rect(footerInner.x, footerInner.y + 18f, footerInner.width, 16f),
                "Wheel/W/S item  A/D tab  F favorite  Enter/LMB choose  B/Esc close",
                smallMutedStyle);

            GUILayout.EndArea();
        }

        private void DrawContextPrompt()
        {
            if (!showActionHints)
            {
                return;
            }

            string prompt = GetContextPrompt();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.contextRect, prompt, promptLabelStyle);

            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padH = Mathf.RoundToInt(14f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);
            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, innerRect.height - 8f),
                prompt,
                promptLabelStyle);
        }

        private void DrawStatusMessage()
        {
            if (string.IsNullOrWhiteSpace(statusMessage) || Time.unscaledTime > statusMessageUntil)
            {
                return;
            }

            UiLayout layout = GetUiLayout();
            Rect rect = GetTextPanelRect(layout.statusRect, statusMessage, labelStyle);

            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padH = Mathf.RoundToInt(14f * currentUiScale);
            float padV = Mathf.RoundToInt(10f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);
            Rect accentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 8f, innerRect.width, innerRect.height - 8f),
                statusMessage,
                labelStyle);
        }

        private void DrawInGameMenu()
        {
            float screenW = Screen.width;
            float screenH = Screen.height;
            float marginFraction = screenW >= 2560f ? 0.025f : screenW >= 1600f ? 0.03f : 0.035f;
            float margin = Mathf.Clamp(
                Mathf.Min(screenW, screenH) * marginFraction, 20f, 80f);

            Rect shellRect = new Rect(
                margin,
                margin,
                Mathf.Max(420f, screenW - (margin * 2f)),
                Mathf.Max(320f, screenH - (margin * 2f)));

            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(
                new Rect(0f, 0f, screenW, screenH),
                panelTexture);

            GUI.color = new Color(0.040f, 0.051f, 0.031f, 0.98f);
            GUI.DrawTexture(shellRect, journalShellTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.45f);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.y, shellRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.yMax - 1f, shellRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.x, shellRect.y, 1f, shellRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(shellRect.xMax - 1f, shellRect.y, 1f, shellRect.height), amberAccentTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.95f);
            GUI.DrawTexture(
                new Rect(shellRect.x, shellRect.y, shellRect.width, 2f),
                amberAccentTexture);

            float navHeight = Mathf.Clamp(82f * currentUiScale, 74f, 140f);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.92f);
            GUI.DrawTexture(
                new Rect(shellRect.x + 22f, shellRect.y + navHeight + 10f,
                    shellRect.width - 44f, 1f),
                amberAccentTexture);

            GUI.color = prev;

            float padding = Mathf.RoundToInt(22f * currentUiScale);
            float footerHeight = Mathf.Clamp(30f * currentUiScale, 26f, 42f);

            Rect navRect = new Rect(
                shellRect.x + padding,
                shellRect.y + padding,
                shellRect.width - (padding * 2f),
                navHeight);

            Rect footerRect = new Rect(
                shellRect.x + padding,
                shellRect.yMax - padding - footerHeight,
                shellRect.width - (padding * 2f),
                footerHeight);

            Rect contentRect = new Rect(
                shellRect.x + padding,
                navRect.yMax + Mathf.RoundToInt(14f * currentUiScale),
                shellRect.width - (padding * 2f),
                footerRect.y - (navRect.yMax + Mathf.RoundToInt(22f * currentUiScale)));

            Rect escRect = new Rect(
                shellRect.xMax - padding - 150f,
                shellRect.y + padding,
                150f,
                24f);
            GUI.Label(escRect, "Esc to close", escHintStyle);

            GUILayout.BeginArea(navRect);
            GUILayout.Label("Settler Journal", journalTitleStyle);
            GUILayout.Label("World state, tracking, and character notes", journalSubtitleStyle);
            GUILayout.Space(Mathf.RoundToInt(10f * currentUiScale));

            GUILayout.BeginHorizontal();
            DrawInGameMenuTabButton(InGameMenuTab.Overview, "Overview");
            DrawInGameMenuTabButton(InGameMenuTab.Skills, "Skills");
            DrawInGameMenuTabButton(InGameMenuTab.Inventory, "Inventory");
            DrawInGameMenuTabButton(InGameMenuTab.Settings, "Settings");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            prev = GUI.color;
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.30f);
            GUI.DrawTexture(
                new Rect(footerRect.x, footerRect.y - 4f, footerRect.width, 1f),
                amberAccentTexture);
            GUI.color = prev;

            GUI.Label(
                new Rect(footerRect.x + 2f, footerRect.y + 4f, footerRect.width, 18f),
                "Journal navigation  •  Esc close  •  A/D or 1-4 switch tabs",
                smallMutedStyle);

            switch (selectedInGameMenuTab)
            {
                case InGameMenuTab.Overview: DrawOverviewTab(contentRect); break;
                case InGameMenuTab.Skills: DrawSkillsTab(contentRect); break;
                case InGameMenuTab.Inventory: DrawInventoryTab(contentRect); break;
                case InGameMenuTab.Settings: DrawInGameSettingsTab(contentRect); break;
            }
        }

        private void DrawInGameMenuTabButton(InGameMenuTab tab, string label)
        {
            bool isSelected = selectedInGameMenuTab == tab;
            float buttonHeight = Mathf.RoundToInt(38f * currentUiScale);
            float minWidth = Mathf.Clamp(110f * currentUiScale, 96f, 240f);
            float tabGap = Mathf.RoundToInt(10f * currentUiScale);

            Rect buttonRect = GUILayoutUtility.GetRect(
                minWidth,
                buttonHeight,
                GUILayout.MinWidth(minWidth),
                GUILayout.MaxWidth(280f),
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
                selectedInGameMenuTab = tab;
            }

            if (isSelected)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.95f);
                GUI.DrawTexture(new Rect(buttonRect.x + 8f, buttonRect.yMax - 2f, buttonRect.width - 16f, 2f), amberAccentTexture);
                GUI.color = previousColor;
            }

            GUILayout.Space(tabGap);
        }

        private void DrawJournalPanel(Rect rect, string title, Action drawer, string subtitle = null)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);

            float padH = Mathf.RoundToInt(16f * currentUiScale);
            float padV = Mathf.RoundToInt(14f * currentUiScale);
            Rect innerRect = new Rect(rect.x + padH, rect.y + padV, rect.width - padH * 2f, rect.height - padV * 2f);

            float titleHeight = Mathf.RoundToInt(22f * currentUiScale);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, titleHeight);
            GUI.Label(titleRect, title, headingStyle);

            Rect accentRect = new Rect(innerRect.x, titleRect.yMax + 5f, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            float subtitleHeight = 0f;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                subtitleHeight = Mathf.Clamp(18f * currentUiScale, 16f, 24f);
                Rect subtitleRect = new Rect(innerRect.x, accentRect.yMax + 6f, innerRect.width, subtitleHeight);
                GUI.Label(subtitleRect, subtitle, journalSubtitleStyle);
            }

            float contentTop = accentRect.yMax + 10f + subtitleHeight + (subtitleHeight > 0f ? 2f : 0f);
            Rect contentRect = new Rect(
                innerRect.x,
                contentTop,
                innerRect.width,
                Mathf.Max(20f, innerRect.yMax - contentTop));

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawOverviewTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);
            float topHeight = Mathf.Clamp(contentRect.height * 0.50f, 220f, 540f);
            float leftWidth = Mathf.Max(200f, contentRect.width * 0.26f);
            float middleWidth = Mathf.Max(240f, contentRect.width * 0.36f);
            float rightWidth = Mathf.Max(180f, contentRect.width - leftWidth - middleWidth - (gutter * 2f));

            Rect mapRect = new(contentRect.x, contentRect.y, leftWidth, topHeight);
            Rect overviewRect = new(mapRect.xMax + gutter, contentRect.y, middleWidth, topHeight);
            Rect timersRect = new(overviewRect.xMax + gutter, contentRect.y, rightWidth, topHeight);

            float bottomHeight = contentRect.height - topHeight - gutter;
            Rect questRect = new(contentRect.x, mapRect.yMax + gutter, leftWidth, bottomHeight);
            Rect socialRect = new(questRect.xMax + gutter, mapRect.yMax + gutter, middleWidth, bottomHeight);
            Rect favoritesRect = new(socialRect.xMax + gutter, mapRect.yMax + gutter, rightWidth, bottomHeight);

            DrawJournalPanel(mapRect, "Area Map", () =>
            {
                if (TryGetTrackedTarget(out PlacedWorldObject trackedObject, out BuildCatalogItem trackedItem, out float trackedDistance))
                {
                    GUILayout.Label($"Tracking: {trackedItem.displayName}  |  {Mathf.RoundToInt(trackedDistance)}m", journalBodyStyle);
                }
                else
                {
                    GUILayout.Label("Tracking: no nearby target", journalSubtitleStyle);
                }

                float mapHeight = Mathf.Max(120f, mapRect.height - 82f);
                Rect mapCanvas = GUILayoutUtility.GetRect(10f, mapHeight, GUILayout.ExpandWidth(true), GUILayout.Height(mapHeight));
                DrawTrackingMap(mapCanvas);
            }, "Live local tracking");

            DrawJournalPanel(overviewRect, "Overview", () =>
            {
                float bar = Mathf.Clamp(13f * currentUiScale, 10f, 16f);

                DrawStatBarRow("Health", health / (float)MaxHealth, statBarHpTexture, $"{health}/{MaxHealth}", bar);
                DrawStatBarRow("Wood", Mathf.Clamp01(wood / 50f), statBarHungerTexture, $"{wood}", bar);
                DrawStatBarRow("Stone", Mathf.Clamp01(stone / 30f), statBarThirstTexture, $"{stone}", bar);
                DrawStatBarRow("Food", Mathf.Clamp01(food / 20f), statBarStaminaTexture, $"{food}", bar);

                GUILayout.Space(6f);
                GUILayout.Label($"Carried: {FormatInventorySummary(storedFoodInventory, 3)}", journalBodyStyle);
                GUILayout.Label($"Arms: {FormatInventorySummary(storedWeaponInventory, 3)}", journalBodyStyle);

                GUILayout.Space(6f);
                GUILayout.Label($"Slot {GetHotbarKeyLabel(selectedHotbarIndex)}  {GetSelectedHotbarItem()?.displayName ?? "Empty"}", journalBodyStyle);
                GUILayout.Label($"{(devBuildMode ? "DEV Build" : "Resource Build")}  |  {GetFacingLabel()}", journalSubtitleStyle);
            }, "Current character state");

            DrawJournalPanel(timersRect, "Timers", () =>
            {
                DrawTimerEntries();
            }, "Planted crops from inventory");

            DrawJournalPanel(questRect, "Quest", () =>
            {
                DrawQuestEntries();
            }, "Current progression goals");

            DrawJournalPanel(socialRect, "Social", () =>
            {
                DrawSocialEntries();
            }, "Settlement and relationship status");

            DrawJournalPanel(favoritesRect, "Favorites", () =>
            {
                DrawFavoritesEntries();
            }, "Pinned build pieces");
        }

        private void DrawSelectedPreviewCard(BuildCatalogItem item, float height)
        {
            Rect outerRect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            DrawBorderPanel(outerRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(outerRect.x + 10f, outerRect.y + 10f, outerRect.width - 20f, outerRect.height - 20f);

            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 20f);
            GUI.Label(titleRect, "Selected Preview", headingStyle);

            Rect accentRect = new Rect(innerRect.x, titleRect.yMax + 4f, innerRect.width, 2f);
            GUI.DrawTexture(accentRect, amberAccentTexture);

            float infoHeight = 42f;
            float previewTop = accentRect.yMax + 8f;
            float previewHeight = Mathf.Max(84f, innerRect.height - (previewTop - innerRect.y) - infoHeight);
            Rect previewRect = new Rect(innerRect.x, previewTop, innerRect.width, previewHeight);

            DrawBorderPanel(previewRect, journalCardTexture, 1f);

            Texture previewTexture = GetBuildPreviewTexture(item);
            if (previewTexture != null && item != null)
            {
                Rect imageRect = new Rect(previewRect.x + 8f, previewRect.y + 8f, previewRect.width - 16f, previewRect.height - 16f);
                GUI.DrawTexture(imageRect, previewTexture, ScaleMode.ScaleToFit, false);

                Rect infoRect = new Rect(innerRect.x, previewRect.yMax + 8f, innerRect.width, infoHeight);
                GUI.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 18f), item.displayName, headingStyle);
                GUI.Label(
                    new Rect(infoRect.x, infoRect.y + 20f, infoRect.width, 18f),
                    $"{item.category}  |  {GetItemKindLabel(item.kind)}",
                    smallMutedStyle);
            }
            else
            {
                string titleText = item == null ? "No item selected" : item.displayName;
                string bodyText = item == null
                    ? "Choose a build piece to inspect it here."
                    : "Preview is loading or unavailable for this item.";

                GUI.Label(
                    new Rect(previewRect.x + 12f, previewRect.center.y - 18f, previewRect.width - 24f, 20f),
                    titleText,
                    headingStyle);

                GUI.Label(
                    new Rect(previewRect.x + 12f, previewRect.center.y + 6f, previewRect.width - 24f, 36f),
                    bodyText,
                    promptLabelStyle);
            }
        }

        private void DrawSkillsTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.62f, 400f, 900f);

            Rect skillsRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect summaryRect = new Rect(skillsRect.xMax + gutter, contentRect.y, contentRect.width - leftWidth - gutter, contentRect.height);

            // ── Left: Skill list ───────────────────────────────────────
            DrawJournalPanel(skillsRect, "Skills", () =>
            {
                GUILayout.Label($"Total Level: {playerSkills.GetTotalLevel()}  |  Max per skill: {SkillProgression.MaxLevel}", journalSubtitleStyle);
                GUILayout.Space(6f);

                float scrollHeight = Mathf.Max(200f, skillsRect.height - 110f);
                skillsTabScroll = GUILayout.BeginScrollView(skillsTabScroll, GUILayout.Height(scrollHeight));

                foreach (SkillType skill in SkillDefinitions.All)
                {
                    DrawSkillRow(skill);
                    GUILayout.Space(4f);
                }

                GUILayout.EndScrollView();
            }, "XP progression for all skills");

            // ── Right: Summary ─────────────────────────────────────────
            DrawJournalPanel(summaryRect, "Summary", () =>
            {
                GUILayout.Label("Character Totals", journalSubtitleStyle);
                GUILayout.Space(10f);

                int totalLevel = playerSkills.GetTotalLevel();
                long totalXp = playerSkills.GetTotalXp();
                int skillCount = SkillDefinitions.All.Length;

                GUILayout.Label($"Combined Level: {totalLevel} / {SkillProgression.MaxLevel * skillCount}", headingStyle);
                GUILayout.Space(4f);
                GUILayout.Label($"Total XP Earned: {FormatXpNumber(totalXp)}", labelStyle);
                GUILayout.Space(14f);

                GUI.DrawTexture(GUILayoutUtility.GetRect(10f, 2f, GUILayout.ExpandWidth(true), GUILayout.Height(2f)), amberAccentTexture);
                GUILayout.Space(10f);

                GUILayout.Label("Skill Levels", journalSubtitleStyle);
                GUILayout.Space(6f);

                foreach (SkillType skill in SkillDefinitions.All)
                {
                    SkillRuntimeData data = playerSkills.Get(skill);
                    int level = data.Level;
                    string levelText = level >= SkillProgression.MaxLevel ? $"Lv {level} (MAX)" : $"Lv {level}";
                    GUILayout.Label($"{SkillDefinitions.GetDisplayName(skill)}: {levelText}", smallMutedStyle);
                }
            }, "Overall progression");
        }

        private void DrawSkillRow(SkillType skill)
        {
            SkillRuntimeData data = playerSkills.Get(skill);
            if (data == null) return;

            int level = data.Level;
            float progress = data.Progress;
            bool isMaxed = level >= SkillProgression.MaxLevel;

            float cardHeight = Mathf.Clamp(72f * currentUiScale, 62f, 96f);
            Rect cardRect = GUILayoutUtility.GetRect(10f, cardHeight, GUILayout.ExpandWidth(true), GUILayout.Height(cardHeight));

            DrawBorderPanel(cardRect, journalCardTexture, 1f);

            float pad = 10f * currentUiScale;
            Rect inner = new Rect(cardRect.x + pad, cardRect.y + 6f * currentUiScale, cardRect.width - pad * 2f, cardRect.height - 12f * currentUiScale);

            // Title row: skill name + level
            float titleH = 18f * currentUiScale;
            string levelLabel = isMaxed ? $"Level {level}  (MAX)" : $"Level {level}";
            GUI.Label(new Rect(inner.x, inner.y, inner.width * 0.55f, titleH), SkillDefinitions.GetDisplayName(skill), headingStyle);
            GUI.Label(new Rect(inner.x + inner.width * 0.55f, inner.y, inner.width * 0.45f, titleH), levelLabel, smallMutedStyle);

            // Description
            float descY = inner.y + titleH + 2f;
            float descH = 14f * currentUiScale;
            GUI.Label(new Rect(inner.x, descY, inner.width, descH), SkillDefinitions.GetDescription(skill), journalSubtitleStyle);

            // XP bar
            float barY = descY + descH + 4f;
            float barH = Mathf.Clamp(10f * currentUiScale, 8f, 13f);
            Rect barRect = new Rect(inner.x, barY, inner.width * 0.70f, barH);

            // Bar background
            Color prev = GUI.color;
            GUI.color = new Color(0.039f, 0.047f, 0.027f, 0.92f);
            GUI.DrawTexture(barRect, skillBarBgTexture);

            // Bar fill
            float fillFraction = isMaxed ? 1f : progress;
            float fillWidth = (barRect.width - 2f) * Mathf.Clamp01(fillFraction);
            if (fillWidth > 0f)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(barRect.x + 1f, barRect.y + 1f, fillWidth, barRect.height - 2f), skillBarFillTexture);
            }

            GUI.color = prev;

            // XP label to the right of bar
            float xpLabelX = barRect.xMax + 6f * currentUiScale;
            float xpLabelW = inner.xMax - xpLabelX;
            string xpText;
            if (isMaxed)
            {
                xpText = FormatXpNumber(data.totalXp) + " XP";
            }
            else
            {
                xpText = $"{FormatXpNumber(data.XpIntoLevel)} / {FormatXpNumber(data.XpToNext)}";
            }

            GUI.Label(new Rect(xpLabelX, barY - 1f, xpLabelW, barH + 4f), xpText, smallMutedStyle);
        }

        private static string FormatXpNumber(long xp)
        {
            if (xp >= 1_000_000) return $"{xp / 1_000_000f:F1}M";
            if (xp >= 10_000) return $"{xp / 1_000f:F1}K";
            if (xp >= 1_000) return $"{xp / 1_000f:F2}K";
            return xp.ToString();
        }

        private void DrawInventoryTab(Rect contentRect)
        {
            float gutter = Mathf.RoundToInt(14f * currentUiScale);

            // ── Proportional column sizing ────────────────────────────
            float totalWidth = contentRect.width - (gutter * 3f);
            float col1Fraction = 0.20f;
            float col2Fraction = 0.30f;
            float col3Fraction = 0.24f;
            float col4Fraction = 0.26f;
            float col1W = Mathf.Max(200f, totalWidth * col1Fraction);
            float col2W = Mathf.Max(260f, totalWidth * col2Fraction);
            float col3W = Mathf.Max(220f, totalWidth * col3Fraction);
            float col4W = Mathf.Max(230f, totalWidth * col4Fraction);

            Rect col1Rect = new Rect(contentRect.x, contentRect.y, col1W, contentRect.height);
            Rect col2Rect = new Rect(col1Rect.xMax + gutter, contentRect.y, col2W, contentRect.height);
            Rect col3Rect = new Rect(col2Rect.xMax + gutter, contentRect.y, col3W, contentRect.height);
            Rect col4Rect = new Rect(col3Rect.xMax + gutter, contentRect.y, col4W, contentRect.height);

            float col1TopH = Mathf.Clamp(col1Rect.height * 0.38f, 190f, 360f);
            Rect resourcesRect = new Rect(col1Rect.x, col1Rect.y, col1Rect.width, col1TopH);
            Rect loadoutRect = new Rect(col1Rect.x, resourcesRect.yMax + gutter,
                col1Rect.width, col1Rect.height - col1TopH - gutter);

            List<JournalInventoryEntry> entries = GetJournalInventoryEntries();
            JournalInventoryEntry? selectedEntry = GetSelectedInventoryEntry(entries);

            // ── Column 1: Resources & Loadout ─────────────────────────
            DrawJournalPanel(resourcesRect, "Resources & Harvest", () =>
            {
                DrawInventoryResourceStrip();
                GUILayout.Space(10f);
                GUILayout.Label("Food", headingStyle);
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                DrawMiniItemSlot("Pouch",
                    storedFoodInventory.Values.Sum() > 0
                        ? $"x{storedFoodInventory.Values.Sum()}" : "Empty");
                DrawMiniItemSlot("Rations", food > 0 ? $"x{food}" : "Empty");
                GUILayout.EndHorizontal();
            }, "Carry totals and stored food");

            DrawJournalPanel(loadoutRect, "Equipped Loadout", () =>
            {
                DrawEquippedLoadout();
            }, "Active gear and armor");

            // ── Column 2: Inventory Grid ──────────────────────────────
            DrawInventoryCenterPanel(col2Rect, entries);

            // ── Column 3: Selected Item + Equip ───────────────────────
            DrawJournalPanel(col3Rect, "Selected Item", () =>
            {
                GUILayout.Label("Details and equip options.", journalSubtitleStyle);
                GUILayout.Space(8f);
                DrawInventorySelectionDetails(selectedEntry);
                GUILayout.Space(10f);
                DrawEquipButton(selectedEntry);
                GUILayout.Space(6f);
                DrawDropButton(selectedEntry);
            }, "Inspection");

            // ── Column 4: Crafting ────────────────────────────────────
            DrawJournalPanel(col4Rect, "Crafting", () =>
            {
                DrawCraftingPanel();
            }, "Craft items from materials");
        }

        private void DrawInventoryCenterPanel(Rect rect, List<JournalInventoryEntry> entries)
        {
            DrawBorderPanel(rect, journalCardTexture, 1f);

            float pad = Mathf.RoundToInt(16f * currentUiScale);
            float padV = Mathf.RoundToInt(14f * currentUiScale);
            Rect inner = new Rect(rect.x + pad, rect.y + padV, rect.width - pad * 2f, rect.height - padV * 2f);

            float titleH = Mathf.RoundToInt(22f * currentUiScale);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, titleH), "Inventory Layout", headingStyle);
            GUI.DrawTexture(new Rect(inner.x, inner.y + titleH + 5f, inner.width, 2f), amberAccentTexture);
            GUI.Label(new Rect(inner.x, inner.y + titleH + 13f, inner.width, Mathf.RoundToInt(18f * currentUiScale)), "Carried inventory", journalSubtitleStyle);

            float gridTop = inner.y + titleH + Mathf.RoundToInt(38f * currentUiScale);
            float gridHeight = Mathf.Max(100f, inner.yMax - gridTop);
            Rect gridRect = new Rect(inner.x, gridTop, inner.width, gridHeight);

            // ── Grid content ──────────────────────────────────────────
            DrawInventoryCardGrid(entries, gridRect);
        }

        private void DrawInventoryCard(JournalInventoryEntry entry, float width, float height)
        {
            Rect cardRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            bool isSelected = string.Equals(selectedInventoryItemId, entry.itemId, StringComparison.Ordinal);
            bool isEquipment = catalogLookup.TryGetValue(entry.itemId, out BuildCatalogItem catItem)
                && GetEquipmentInfo(catItem) != null;

            Color prev = GUI.color;

            GUI.color = new Color(0.090f, 0.102f, 0.058f, 0.97f);
            GUI.DrawTexture(cardRect, journalCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, isSelected ? 0.70f : 0.25f);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), amberAccentTexture);

            if (isSelected)
            {
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.22f);
                GUI.DrawTexture(new Rect(cardRect.x + 1f, cardRect.y + 1f, cardRect.width - 2f, cardRect.height - 2f), modeBadgeTexture);
            }
            else if (entry.isFavorited)
            {
                GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.12f);
                GUI.DrawTexture(new Rect(cardRect.x + 1f, cardRect.y + 1f, cardRect.width - 2f, 20f), modeBadgeTexture);
            }

            GUI.color = prev;

            float innerPad = Mathf.RoundToInt(12f * currentUiScale);
            Rect innerRect = new Rect(cardRect.x + innerPad, cardRect.y + Mathf.RoundToInt(10f * currentUiScale),
                cardRect.width - innerPad * 2f, cardRect.height - Mathf.RoundToInt(20f * currentUiScale));

            float lineH = Mathf.RoundToInt(20f * currentUiScale);
            float smallH = Mathf.RoundToInt(16f * currentUiScale);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 28f, lineH), entry.displayName, headingStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + lineH + 2f, innerRect.width, smallH), entry.categoryLabel, smallMutedStyle);
            GUI.Label(new Rect(innerRect.x, innerRect.y + lineH + smallH + 6f, innerRect.width, lineH), $"x{entry.quantity}", labelStyle);

            string subtitle = entry.isFavorited
                ? "Pinned build item"
                : isEquipment
                    ? "Equippable gear"
                    : entry.categoryLabel == "Food"
                        ? "Stored crop or ration"
                        : entry.categoryLabel == "Resource"
                            ? "Construction material"
                            : "Stored inventory item";

            float subtitleY = innerRect.y + lineH * 2f + smallH + 10f;
            if (subtitleY + smallH <= innerRect.yMax)
            {
                GUI.Label(new Rect(innerRect.x, subtitleY, innerRect.width, smallH + 10f), subtitle, journalSubtitleStyle);
            }

            if (entry.isFavorited)
            {
                GUI.Label(new Rect(innerRect.xMax - 24f, innerRect.y, 24f, smallH), "★", smallMutedStyle);
            }

            if (isEquipment && !entry.isFavorited)
            {
                GUI.Label(new Rect(innerRect.xMax - 24f, innerRect.y, 24f, smallH),
                    IsItemEquipped(entry.itemId) ? "✦" : "⚔", smallMutedStyle);
            }

            if (GUI.Button(cardRect, GUIContent.none, GUIStyle.none))
            {
                selectedInventoryItemId = entry.itemId;
            }
        }

        private void DrawInventoryCardGrid(List<JournalInventoryEntry> entries, Rect gridRect)
        {
            if (entries == null || entries.Count == 0)
            {
                // ── Empty state ───────────────────────────────────────
                DrawBorderPanel(gridRect, hudCardTexture, 1f);

                Rect emptyInner = new Rect(
                    gridRect.x + 12f, gridRect.y + 10f,
                    gridRect.width - 24f, gridRect.height - 20f);

                GUI.Label(new Rect(emptyInner.x, emptyInner.y, emptyInner.width, 20f),
                    "No stored inventory yet", headingStyle);
                GUI.Label(
                    new Rect(emptyInner.x, emptyInner.y + 24f, emptyInner.width, 40f),
                    "Gather crops or pick up crafted gear to populate this layout.",
                    journalSubtitleStyle);
                return;
            }

            // ── Frame background ──────────────────────────────────────
            Color prev = GUI.color;
            GUI.color = new Color(0.050f, 0.060f, 0.035f, 0.92f);
            GUI.DrawTexture(gridRect, hudCardTexture);
            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.15f);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.y, gridRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.yMax - 1f, gridRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.x, gridRect.y, 1f, gridRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(gridRect.xMax - 1f, gridRect.y, 1f, gridRect.height), amberAccentTexture);
            GUI.color = prev;

            // ── Card sizing ───────────────────────────────────────────
            float gap = Mathf.Clamp(12f * currentUiScale, 8f, 16f);
            float pad = Mathf.RoundToInt(12f * currentUiScale);
            float usableWidth = Mathf.Max(160f, gridRect.width - (pad * 2f));

            int columns = usableWidth >= 900f ? 5
                        : usableWidth >= 650f ? 4
                        : usableWidth >= 420f ? 3
                        : 2;
            float rawCardWidth = (usableWidth - (gap * (columns - 1))) / columns;
            float cardWidth  = Mathf.Clamp(rawCardWidth, 100f, 230f);
            float cardHeight = Mathf.Clamp(112f * currentUiScale, 96f, 148f);

            // ── Scroll area — single BeginArea directly on screen rect ──
            Rect scrollOuter = new Rect(
                gridRect.x + pad,
                gridRect.y + pad,
                gridRect.width - (pad * 2f),
                gridRect.height - (pad * 2f));

            GUILayout.BeginArea(scrollOuter);
            inventoryGridScroll = GUILayout.BeginScrollView(
                inventoryGridScroll,
                false, false,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUIStyle.none);

            int index = 0;
            while (index < entries.Count)
            {
                GUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    if (index < entries.Count)
                    {
                        DrawInventoryCard(entries[index], cardWidth, cardHeight);
                        index++;
                    }
                    else
                    {
                        GUILayout.Space(cardWidth);
                    }

                    if (col < columns - 1)
                        GUILayout.Space(gap);
                }

                GUILayout.EndHorizontal();

                if (index < entries.Count)
                    GUILayout.Space(gap);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawQuestEntries()
        {
            int placedStructures = CountPlacedObjects(ItemKind.Structure);
            int favoritedCount = CountFavoritedSlots();
            int totalFood = Mathf.Max(food, storedFoodInventory.Values.Sum());

            DrawQuestRow(
                "Gather 10 Wood",
                wood >= 10,
                $"{Mathf.Min(wood, 10)}/10",
                Mathf.Clamp01(wood / 10f));

            DrawQuestRow(
                "Gather 6 Stone",
                stone >= 6,
                $"{Mathf.Min(stone, 6)}/6",
                Mathf.Clamp01(stone / 6f));

            DrawQuestRow(
                "Collect 3 Food",
                totalFood >= 3,
                $"{Mathf.Min(totalFood, 3)}/3",
                Mathf.Clamp01(totalFood / 3f));

            DrawQuestRow(
                "Place 1 Structure",
                placedStructures >= 1,
                $"{Mathf.Min(placedStructures, 1)}/1",
                Mathf.Clamp01(placedStructures));

            DrawQuestRow(
                "Favorite 3 Pieces",
                favoritedCount >= 3,
                $"{Mathf.Min(favoritedCount, 3)}/3",
                Mathf.Clamp01(favoritedCount / 3f));
        }

        private void DrawQuestRow(string title, bool complete, string progressText, float progress01)
        {
            float rowHeight = Mathf.Clamp(62f * currentUiScale, 54f, 78f);
            Rect rowRect = GUILayoutUtility.GetRect(
                10f, rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            Color prev = GUI.color;

            GUI.color = complete
                ? new Color(0.145f, 0.176f, 0.078f, 0.35f)
                : new Color(0.086f, 0.102f, 0.055f, 0.92f);
            GUI.DrawTexture(rowRect, journalCardTexture);

            GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.18f);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y, 1f, rowRect.height), amberAccentTexture);
            GUI.DrawTexture(new Rect(rowRect.xMax - 1f, rowRect.y, 1f, rowRect.height), amberAccentTexture);

            GUI.color = complete
                ? new Color(0.788f, 0.659f, 0.298f, 0.95f)
                : new Color(0.788f, 0.659f, 0.298f, 0.20f);
            GUI.DrawTexture(
                new Rect(rowRect.x, rowRect.y, 3f, rowRect.height),
                amberAccentTexture);

            GUI.color = prev;

            Rect inner = new Rect(
                rowRect.x + 14f,
                rowRect.y + 8f,
                rowRect.width - 24f,
                rowRect.height - 16f);

            Rect checkRect = new Rect(inner.x, inner.y + 1f, 16f, 16f);

            prev = GUI.color;
            if (complete)
            {
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.18f);
                GUI.DrawTexture(checkRect, modeBadgeTexture);
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.95f);
                GUI.DrawTexture(
                    new Rect(checkRect.x, checkRect.yMax - 1f, checkRect.width, 1f),
                    amberAccentTexture);
            }
            else
            {
                GUI.color = new Color(0.118f, 0.133f, 0.078f, 0.85f);
                GUI.DrawTexture(checkRect, skillBarBgTexture);
                GUI.color = new Color(0.788f, 0.659f, 0.298f, 0.20f);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.y, checkRect.width, 1f), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.yMax - 1f, checkRect.width, 1f), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.x, checkRect.y, 1f, checkRect.height), amberAccentTexture);
                GUI.DrawTexture(new Rect(checkRect.xMax - 1f, checkRect.y, 1f, checkRect.height), amberAccentTexture);
            }
            GUI.color = prev;

            GUIStyle checkStyle = complete ? questDoneStyle : questPendingStyle;
            GUI.Label(checkRect, complete ? "✓" : "○", checkStyle);

            Rect titleRect = new Rect(
                checkRect.xMax + 8f,
                inner.y,
                inner.width - 70f,
                18f);
            GUI.Label(titleRect, title, complete ? questDoneStyle : journalBodyStyle);

            Rect countRect = new Rect(inner.xMax - 52f, inner.y, 52f, 18f);
            GUI.Label(countRect, progressText, smallMutedStyle);

            float barHeight = Mathf.Clamp(6f * currentUiScale, 4f, 8f);
            Rect barBgRect = new Rect(
                checkRect.xMax + 8f,
                inner.y + 26f,
                inner.width - 26f,
                barHeight);

            prev = GUI.color;
            GUI.color = new Color(0.039f, 0.047f, 0.027f, 0.92f);
            GUI.DrawTexture(barBgRect, statBarBgTexture);

            float fillWidth = (barBgRect.width - 2f) * Mathf.Clamp01(progress01);
            if (fillWidth > 0f)
            {
                GUI.color = complete
                    ? new Color(0.788f, 0.659f, 0.298f, 0.95f)
                    : new Color(0.545f, 0.427f, 0.173f, 0.90f);
                GUI.DrawTexture(
                    new Rect(barBgRect.x + 1f, barBgRect.y + 1f, fillWidth, barBgRect.height - 2f),
                    complete ? skillBarFillTexture : statBarHungerTexture);
            }
            GUI.color = prev;

            if (complete)
            {
                GUI.Label(
                    new Rect(checkRect.xMax + 8f, inner.y + 38f, inner.width, 14f),
                    "Complete",
                    smallMutedStyle);
            }
        }

        private void DrawInGameSettingsTab(Rect contentRect)
        {
            // UI scale slider affects only the Settings tab layout.
            float uiScale = Mathf.Clamp(currentUiScale * settingsUiScaleMultiplier, 0.7f, 1.7f);

            float gutter = Mathf.RoundToInt(14f * uiScale);
            float leftWidth = Mathf.Clamp(contentRect.width * 0.56f, 380f, 720f);

            Rect settingsRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect previewRect = new Rect(
                settingsRect.xMax + gutter,
                contentRect.y,
                contentRect.width - leftWidth - gutter,
                contentRect.height);

            // Cache camera settings once when opening the settings tab.
            if (!settingsCameraCacheInitialized && followCamera != null)
            {
                cameraMouseSensitivity = followCamera.MouseSensitivity;
                cameraInvertY = followCamera.InvertY;
                cameraDistance = followCamera.Distance;

                // Derive smoothing multiplier from the currently applied runtime tuning.
                float baseFollowSmoothTime = cameraFeelMode == CameraFeelMode.Calm
                    ? 0.05f
                    : cameraFeelMode == CameraFeelMode.Normal
                        ? 0.035f
                        : 0.025f;

                float baseRotationSmoothSpeed = cameraFeelMode == CameraFeelMode.Calm
                    ? 24f
                    : cameraFeelMode == CameraFeelMode.Normal
                        ? 30f
                        : 36f;

                float fromFollow = baseFollowSmoothTime > 0f ? followCamera.FollowSmoothTime / baseFollowSmoothTime : 1f;
                float fromRotation = baseRotationSmoothSpeed > 0f
                    ? baseRotationSmoothSpeed / Mathf.Max(0.001f, followCamera.RotationSmoothSpeed)
                    : 1f;
                cameraSmoothingMultiplier = Mathf.Clamp((fromFollow + fromRotation) * 0.5f, 0.6f, 1.6f);

                settingsCameraCacheInitialized = true;
            }

            DrawJournalPanel(settingsRect, "In-Game Settings", () =>
            {
                GUILayout.Label("Session settings for HUD, camera, and journal behavior.", journalSubtitleStyle);
                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Display ─────────────────────────────────────────────
                DrawSettingsSection("Display", 6, uiScale, () =>
                {
                    DrawSliderRow(
                        "UI Scale",
                        settingsUiScaleMultiplier,
                        0.85f,
                        1.25f,
                        v => settingsUiScaleMultiplier = v,
                        v => $"{(currentUiScale * v):F2}x",
                        uiScale);

                    bool isFullscreen = Screen.fullScreen;
                    DrawToggleRow(
                        "Fullscreen",
                        isFullscreen,
                        () => Screen.fullScreen = !Screen.fullScreen,
                        uiScale,
                        "FULL",
                        "WINDOW");

                    DrawInfoRow(
                        "Resolution",
                        $"{Screen.width}x{Screen.height}",
                        uiScale);

                    DrawToggleRow(
                        "Show FPS",
                        showFps,
                        () => showFps = !showFps,
                        uiScale);

                    DrawToggleRow(
                        "Show Interaction Hints",
                        showActionHints,
                        () => showActionHints = !showActionHints,
                        uiScale);

                    DrawToggleRow(
                        "Crosshair",
                        showCrosshair,
                        () => showCrosshair = !showCrosshair,
                        uiScale);
                });

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Camera ─────────────────────────────────────────────
                DrawSettingsSection("Camera", 5, uiScale, () =>
                {
                    DrawSliderRow(
                        "Mouse Sensitivity",
                        cameraMouseSensitivity,
                        0.05f,
                        0.40f,
                        v =>
                        {
                            cameraMouseSensitivity = v;
                            followCamera?.ApplyLookSettings(cameraMouseSensitivity, cameraInvertY);
                        },
                        v => v.ToString("F2"),
                        uiScale);

                    DrawSliderRow(
                        "Camera Smoothing",
                        cameraSmoothingMultiplier,
                        0.60f,
                        1.60f,
                        v =>
                        {
                            cameraSmoothingMultiplier = v;
                            ApplyCameraFeel();
                        },
                        v => $"{v:F2}x",
                        uiScale);

                    DrawToggleRow(
                        "Invert Y",
                        cameraInvertY,
                        () =>
                        {
                            cameraInvertY = !cameraInvertY;
                            followCamera?.ApplyLookSettings(cameraMouseSensitivity, cameraInvertY);
                        },
                        uiScale,
                        "YES",
                        "NO");

                    DrawSliderRow(
                        "Camera Distance",
                        cameraDistance,
                        2.0f,
                        10.0f,
                        v =>
                        {
                            cameraDistance = v;
                            followCamera?.ApplyCameraDistance(cameraDistance);
                        },
                        v => $"{v:F1}m",
                        uiScale);

                    DrawCameraFeelRow(
                        uiScale,
                        cameraFeelMode,
                        newMode =>
                        {
                            cameraFeelMode = newMode;
                            ApplyCameraFeel();
                        });
                });

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                // ── Gameplay ───────────────────────────────────────────
                DrawSettingsSection("Gameplay", 4, uiScale, () =>
                {
                    DrawToggleRow(
                        "Pause Game (Journal)",
                        pauseGameWhenJournalOpen,
                        () =>
                        {
                            pauseGameWhenJournalOpen = !pauseGameWhenJournalOpen;

                            // Apply immediately if the journal is already open.
                            if (inGameMenuOpen)
                            {
                                if (pauseGameWhenJournalOpen)
                                {
                                    timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? timeScaleBeforePause : Time.timeScale;
                                    Time.timeScale = 0f;
                                }
                                else
                                {
                                    Time.timeScale = timeScaleBeforePause;
                                }
                            }
                        },
                        uiScale);

                    DrawToggleRow(
                        "DEV Build Mode",
                        devBuildMode,
                        () => devBuildMode = !devBuildMode,
                        uiScale,
                        "DEV",
                        "RES");

                    // Not implemented as automatic behavior in the current codebase;
                    // shown to keep the Settings panel complete.
                    DrawInfoRow("Auto-equip on pickup", "Manual equip only", uiScale);
                    DrawInfoRow("Interact mode", "Uses input action config", uiScale);
                });
            }, "Session-level options");

            // ── Preview (Right column) ─────────────────────────────────
            DrawJournalPanel(previewRect, "Preview", () =>
            {
                GUILayout.Label("Live session preview", journalSubtitleStyle);
                GUILayout.Space(Mathf.RoundToInt(8f * uiScale));

                float hudCardHeight = Mathf.Clamp(240f * uiScale, 200f, 360f);
                float sessionCardHeight = Mathf.Clamp(150f * uiScale, 120f, 260f);

                Rect hudCardRect = GUILayoutUtility.GetRect(
                    Mathf.RoundToInt(10f * uiScale),
                    hudCardHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(hudCardHeight));

                DrawBorderPanel(hudCardRect, journalCardTexture, 1f);

                Rect hudInner = new Rect(
                    hudCardRect.x + 10f * uiScale,
                    hudCardRect.y + 10f * uiScale,
                    hudCardRect.width - (20f * uiScale),
                    hudCardRect.height - (20f * uiScale));

                GUI.Label(new Rect(hudInner.x, hudInner.y, hudInner.width, 18f * uiScale), "HUD Preview", headingStyle);
                GUI.DrawTexture(new Rect(hudInner.x, hudInner.y + 22f * uiScale, hudInner.width, 2f * uiScale), amberAccentTexture);

                float textTop = hudInner.y + 34f * uiScale;
                GUI.Label(new Rect(hudInner.x, textTop, hudInner.width, 16f * uiScale),
                    $"Resolution: {Screen.width}x{Screen.height}  |  Fullscreen: {(Screen.fullScreen ? "ON" : "OFF")}",
                    journalSubtitleStyle);

                string hudFlags = $"{(showCrosshair ? "Crosshair" : "No Crosshair")}  •  {(showActionHints ? "Hints" : "No Hints")}  •  {(showFps ? "FPS On" : "FPS Off")}";
                GUI.Label(new Rect(hudInner.x, textTop + 20f * uiScale, hudInner.width, 16f * uiScale),
                    hudFlags,
                    smallMutedStyle);

                string cameraProfile =
                    $"Camera: {cameraFeelMode}  |  Smoothing {cameraSmoothingMultiplier:F2}  |  Sens {cameraMouseSensitivity:F2}  |  Dist {cameraDistance:F1}m  |  InvertY {(cameraInvertY ? "ON" : "OFF")}";
                GUI.Label(new Rect(hudInner.x, textTop + 38f * uiScale, hudInner.width, 16f * uiScale),
                    cameraProfile,
                    smallMutedStyle);

                Rect uiBarRect = new Rect(
                    hudInner.x,
                    textTop + 58f * uiScale,
                    hudInner.width,
                    Mathf.RoundToInt(14f * uiScale));
                DrawStatBar(
                    uiBarRect,
                    Mathf.Clamp01((currentUiScale * settingsUiScaleMultiplier) / 1.5f),
                    skillBarFillTexture,
                    "UI",
                    $"{(currentUiScale * settingsUiScaleMultiplier):F2}x");

                Rect miniHudRect = new Rect(
                    hudInner.x,
                    hudInner.yMax - (uiScale * 64f),
                    hudInner.width,
                    uiScale * 64f);
                DrawMiniSettingsHudPreview(miniHudRect, uiScale);

                GUILayout.Space(Mathf.RoundToInt(10f * uiScale));

                Rect sessionCardRect = GUILayoutUtility.GetRect(
                    Mathf.RoundToInt(10f * uiScale),
                    sessionCardHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(sessionCardHeight));

                DrawBorderPanel(sessionCardRect, journalCardTexture, 1f);

                Rect sessionInner = new Rect(
                    sessionCardRect.x + 10f * uiScale,
                    sessionCardRect.y + 10f * uiScale,
                    sessionCardRect.width - (20f * uiScale),
                    sessionCardRect.height - (20f * uiScale));

                GUI.Label(new Rect(sessionInner.x, sessionInner.y, sessionInner.width, 18f * uiScale), "Session Preview", headingStyle);
                GUI.DrawTexture(new Rect(sessionInner.x, sessionInner.y + 22f * uiScale, sessionInner.width, 2f * uiScale), amberAccentTexture);

                float sTop = sessionInner.y + 34f * uiScale;
                GUI.Label(new Rect(sessionInner.x, sTop, sessionInner.width, 16f * uiScale),
                    $"Journal Open: Always  |  Game Paused: {(pauseGameWhenJournalOpen ? "Yes" : "No")}",
                    journalSubtitleStyle);
                GUI.Label(new Rect(sessionInner.x, sTop + 20f * uiScale, sessionInner.width, 16f * uiScale),
                    "Cursor: Unlocked UI  •  Look: Fully Paused",
                    smallMutedStyle);
                GUI.Label(new Rect(sessionInner.x, sTop + 38f * uiScale, sessionInner.width, 16f * uiScale),
                    "Close with Esc / Tab (cursor locks + look resumes).",
                    smallMutedStyle);
            }, "Visual feedback");
        }

        private void DrawSettingsSection(string title, int rowCount, float uiScale, Action drawer)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            float headerHeight = Mathf.RoundToInt(50f * uiScale);
            float sectionHeight = (rowCount * rowHeight) + headerHeight;
            Rect sectionRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                sectionHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(sectionHeight));

            DrawBorderPanel(sectionRect, hudCardTexture, 1f);

            float padX = Mathf.RoundToInt(14f * uiScale);
            float padY = Mathf.RoundToInt(12f * uiScale);
            float titleH = Mathf.RoundToInt(20f * uiScale);
            float accentYOffset = titleH + Mathf.RoundToInt(6f * uiScale);
            float contentYOffset = accentYOffset + Mathf.RoundToInt(10f * uiScale);

            Rect innerRect = new Rect(
                sectionRect.x + padX,
                sectionRect.y + padY,
                sectionRect.width - (padX * 2f),
                sectionRect.height - (padY * 2f));

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, titleH), title, headingStyle);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + accentYOffset, innerRect.width, 2f), amberAccentTexture);

            Rect contentRect = new Rect(innerRect.x, innerRect.y + contentYOffset, innerRect.width, innerRect.height - contentYOffset);

            GUILayout.BeginArea(contentRect);
            drawer?.Invoke();
            GUILayout.EndArea();
        }

        private void DrawMiniSettingsHudPreview(Rect rect, float uiScale)
        {
            DrawBorderPanel(rect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 8f * uiScale;

            Rect hudRect = new Rect(
                rect.x + padX,
                rect.y + padY,
                rect.width - (padX * 2f),
                rect.height - (padY * 2f));

            Rect hpRect = new Rect(hudRect.x, hudRect.y + 4f * uiScale, hudRect.width, 6f * uiScale);
            DrawStatBar(hpRect, 0.72f, statBarHpTexture, string.Empty, string.Empty);

            Rect subBarRect = new Rect(hudRect.x, hudRect.y + 12f * uiScale, hudRect.width * 0.56f, 4f * uiScale);
            GUI.DrawTexture(subBarRect, statBarBgTexture);
            GUI.DrawTexture(
                new Rect(
                    subBarRect.x + 1f * uiScale,
                    subBarRect.y + 1f * uiScale,
                    Mathf.Max(0f, subBarRect.width - 2f * uiScale) * 0.65f,
                    subBarRect.height - 2f * uiScale),
                statBarHungerTexture);

            Rect modeRect = new Rect(hudRect.x, hudRect.y + 20f * uiScale, 60f * uiScale, 12f * uiScale);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.82f, 0.64f, 0.24f, 0.22f);
            GUI.DrawTexture(modeRect, modeBadgeTexture);
            GUI.color = previousColor;

            GUI.Label(modeRect, devBuildMode ? "DEV" : "LIVE", smallMutedStyle);

            if (showCrosshair)
            {
                float cx = hudRect.xMax - 18f * uiScale;
                float cy = hudRect.y + (hudRect.height * 0.45f);

                GUI.DrawTexture(new Rect(cx - 6f * uiScale, cy - 1f * uiScale, 12f * uiScale, 2f * uiScale), amberAccentTexture);
                GUI.DrawTexture(new Rect(cx - 1f * uiScale, cy - 6f * uiScale, 2f * uiScale, 12f * uiScale), amberAccentTexture);
            }

            if (showActionHints)
            {
                float hintLabelW = 70f * uiScale;
                GUI.Label(
                    new Rect(hudRect.xMax - hintLabelW, hudRect.yMax - 18f * uiScale, hintLabelW, 14f * uiScale),
                    "HINTS",
                    smallMutedStyle);
                Rect hintLine = new Rect(hudRect.xMax - (40f * uiScale), hudRect.yMax - (8f * uiScale), 30f * uiScale, 2f * uiScale);
                GUI.DrawTexture(hintLine, amberAccentTexture);
            }

            if (showFps)
            {
                GUI.Label(new Rect(hudRect.x, hudRect.yMax - 18f * uiScale, 60f * uiScale, 14f * uiScale), "FPS", smallMutedStyle);
            }
        }

        private void DrawToggleRow(
            string label,
            bool isOn,
            Action onToggle,
            float uiScale,
            string onLabel = "ON",
            string offLabel = "OFF")
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float toggleWidth = Mathf.RoundToInt(74f * uiScale);
            float toggleHeight = Mathf.RoundToInt(26f * uiScale);
            float labelReserved = toggleWidth + 28f * uiScale;
            Rect labelRect = new Rect(innerRect.x, innerRect.y, innerRect.width - labelReserved, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect toggleRect = new Rect(
                innerRect.xMax - toggleWidth,
                innerRect.y + ((innerRect.height - toggleHeight) * 0.5f),
                toggleWidth,
                toggleHeight);

            Rect stateDotRect = new Rect(
                toggleRect.x - 14f * uiScale,
                innerRect.y + ((innerRect.height - 8f * uiScale) * 0.5f),
                8f * uiScale,
                8f * uiScale);

            Color previousColor = GUI.color;
            GUI.color = isOn
                ? new Color(0.82f, 0.64f, 0.24f, 0.95f)
                : new Color(0.30f, 0.32f, 0.28f, 0.9f);
            GUI.DrawTexture(stateDotRect, panelTexture);
            GUI.color = previousColor;

            GUIStyle toggleStyle = isOn ? toggleOnStyle : toggleOffStyle;
            string toggleLabel = isOn ? onLabel : offLabel;

            if (GUI.Button(toggleRect, toggleLabel, toggleStyle))
            {
                onToggle?.Invoke();
            }
        }

        private void DrawInfoRow(string label, string value, float uiScale)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = innerRect.width * 0.50f;
            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect valueRect = new Rect(innerRect.x + labelWidth, innerRect.y, innerRect.width - labelWidth, innerRect.height);
            GUI.Label(valueRect, value, journalBodyStyle);
        }

        private void DrawSliderRow(
            string label,
            float value,
            float min,
            float max,
            Action<float> onValueChanged,
            Func<float, string> valueText,
            float uiScale)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = Mathf.Clamp(innerRect.width * 0.46f, 140f * uiScale, 220f * uiScale);
            float valueWidth = Mathf.Clamp(88f * uiScale, 70f * uiScale, 120f * uiScale);
            float sliderLeft = innerRect.x + labelWidth + 10f * uiScale;
            float sliderWidth = innerRect.xMax - sliderLeft - valueWidth - (6f * uiScale);
            sliderWidth = Mathf.Max(40f * uiScale, sliderWidth);

            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, label, settingsRowLabelStyle);

            Rect sliderRect = new Rect(
                sliderLeft,
                innerRect.y + ((innerRect.height - 10f * uiScale) * 0.5f),
                sliderWidth,
                10f * uiScale);

            float newValue = GUI.HorizontalSlider(sliderRect, value, min, max);
            Rect valueRect = new Rect(
                sliderRect.xMax + 6f * uiScale,
                innerRect.y,
                valueWidth,
                innerRect.height);

            GUI.Label(valueRect, valueText != null ? valueText(newValue) : newValue.ToString("F2"), journalBodyStyle);

            if (!Mathf.Approximately(newValue, value))
            {
                onValueChanged?.Invoke(newValue);
            }
        }

        private void DrawCameraFeelRow(
            float uiScale,
            CameraFeelMode selected,
            Action<CameraFeelMode> onChanged)
        {
            float rowHeight = Mathf.RoundToInt(44f * uiScale);
            Rect rowRect = GUILayoutUtility.GetRect(
                Mathf.RoundToInt(10f * uiScale),
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));

            DrawBorderPanel(rowRect, hudCardTexture, 1f);

            float padX = 10f * uiScale;
            float padY = 6f * uiScale;
            Rect innerRect = new Rect(
                rowRect.x + padX,
                rowRect.y + padY,
                rowRect.width - (padX * 2f),
                rowRect.height - (padY * 2f));

            float labelWidth = Mathf.Clamp(innerRect.width * 0.33f, 120f * uiScale, 180f * uiScale);
            Rect labelRect = new Rect(innerRect.x, innerRect.y, labelWidth, innerRect.height);
            GUI.Label(labelRect, "Camera Feel", settingsRowLabelStyle);

            float buttonsX = innerRect.x + labelWidth + 6f * uiScale;
            Rect buttonsRect = new Rect(buttonsX, innerRect.y, innerRect.width - labelWidth - 6f * uiScale, innerRect.height);

            float gap = 6f * uiScale;
            float buttonWidth = (buttonsRect.width - (gap * 2f)) / 3f;

            CameraFeelMode[] modes = { CameraFeelMode.Calm, CameraFeelMode.Normal, CameraFeelMode.Responsive };
            string[] labels = { "CALM", "NORM", "RESP" };

            for (int i = 0; i < 3; i++)
            {
                bool isSelected = selected == modes[i];
                GUIStyle style = isSelected ? journalActiveTabStyle : journalTabStyle;

                Rect r = new Rect(
                    buttonsRect.x + (i * (buttonWidth + gap)),
                    innerRect.y + ((innerRect.height - 26f * uiScale) * 0.5f),
                    buttonWidth,
                    26f * uiScale);

                if (GUI.Button(r, labels[i], style))
                {
                    onChanged?.Invoke(modes[i]);
                }
            }
        }

        private void DrawTrackingMap(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, hotbarSlotStyle);

            Rect innerRect = new(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            Color previousColor = GUI.color;

            GUI.color = new Color(0.08f, 0.09f, 0.07f, 0.88f);
            GUI.DrawTexture(innerRect, panelTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(innerRect.center.x, innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.center.y, innerRect.width, 1f), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x + (innerRect.width * 0.25f), innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x + (innerRect.width * 0.75f), innerRect.y, 1f, innerRect.height), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + (innerRect.height * 0.25f), innerRect.width, 1f), crosshairTexture);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + (innerRect.height * 0.75f), innerRect.width, 1f), crosshairTexture);

            float range = 40f;
            if (playerTransform != null)
            {
                foreach (PlacedWorldObject placedObject in placedObjects.Values)
                {
                    if (placedObject == null || !catalogLookup.TryGetValue(placedObject.CatalogItemId, out BuildCatalogItem item))
                    {
                        continue;
                    }

                    if (string.Equals(item.prefabName, "Terrain_01", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Vector3 delta = placedObject.transform.position - playerTransform.position;
                    Vector2 planarDelta = new(delta.x, delta.z);
                    if (planarDelta.magnitude > range)
                    {
                        continue;
                    }

                    Vector2 normalized = planarDelta / range;
                    Vector2 point = new(
                        innerRect.center.x + (normalized.x * innerRect.width * 0.45f),
                        innerRect.center.y - (normalized.y * innerRect.height * 0.45f));

                    RenewableNode renewableNode = placedObject.GetComponent<RenewableNode>();
                    InventoryPickup pickup = placedObject.GetComponent<InventoryPickup>();
                    Color dotColor = pickup != null
                        ? new Color(0.94f, 0.86f, 0.42f, 0.95f)
                        : renewableNode != null
                            ? new Color(0.48f, 0.82f, 0.5f, 0.95f)
                            : new Color(0.82f, 0.86f, 0.88f, 0.72f);

                    DrawMapDot(point, 5f, dotColor);
                }

                DrawMapDot(innerRect.center, 7f, new Color(0.98f, 0.96f, 0.84f, 1f));

                if (TryGetTrackedTarget(out PlacedWorldObject trackedObject, out _, out _))
                {
                    Vector3 trackedDelta = trackedObject.transform.position - playerTransform.position;
                    Vector2 trackedPlanar = new(trackedDelta.x, trackedDelta.z);
                    if (trackedPlanar.magnitude <= range)
                    {
                        Vector2 normalized = trackedPlanar / range;
                        Vector2 point = new(
                            innerRect.center.x + (normalized.x * innerRect.width * 0.45f),
                            innerRect.center.y - (normalized.y * innerRect.height * 0.45f));
                        DrawMapDot(point, 8f, new Color(0.98f, 0.67f, 0.3f, 1f));
                    }
                }
            }

            GUI.color = previousColor;
        }

        private void DrawMapDot(Vector2 center, float size, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - (size * 0.5f), center.y - (size * 0.5f), size, size), crosshairTexture);
            GUI.color = previousColor;
        }

        private void DrawInventoryResourceStrip()
        {
            GUILayout.Label("Materials", headingStyle);
            GUILayout.Space(Mathf.RoundToInt(8f * currentUiScale));

            float badgeWidth = Mathf.Clamp(90f * currentUiScale, 76f, 130f);
            float badgeHeight = Mathf.Clamp(30f * currentUiScale, 26f, 38f);

            GUILayout.BeginHorizontal();
            DrawResourceBadge($"Wood: {wood}", badgeWidth, badgeHeight);
            GUILayout.Space(6f);
            DrawResourceBadge($"Stone: {stone}", badgeWidth, badgeHeight);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            int storedFood = storedFoodInventory.Values.Sum();
            int storedWeapons = storedWeaponInventory.Values.Sum();

            GUILayout.BeginHorizontal();
            DrawResourceBadge($"Food: {storedFood}", badgeWidth, badgeHeight);
            GUILayout.Space(6f);
            DrawResourceBadge($"Arms: {storedWeapons}", badgeWidth, badgeHeight);
            GUILayout.EndHorizontal();
        }

        private void DrawMiniItemSlot(string label, string value)
        {
            float width = Mathf.Clamp(94f * currentUiScale, 78f, 140f);
            float height = Mathf.Clamp(72f * currentUiScale, 62f, 100f);
            bool isEmpty = string.Equals(value, "Empty", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "0", StringComparison.Ordinal);

            Rect slotRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            DrawBorderPanel(slotRect, hudCardTexture, 1f);

            Rect innerRect = new Rect(slotRect.x + 8f, slotRect.y + 6f, slotRect.width - 16f, slotRect.height - 12f);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 16f), label, smallMutedStyle);
            GUI.DrawTexture(new Rect(innerRect.x, innerRect.y + 18f, innerRect.width, 1f), amberAccentTexture);

            string displayValue = isEmpty ? "---" : value;
            GUIStyle valueStyle = isEmpty ? smallMutedStyle : headingStyle;

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, 22f),
                displayValue,
                valueStyle);

            GUI.Label(
                new Rect(innerRect.x, innerRect.y + 48f, innerRect.width, 14f),
                isEmpty ? "Empty" : "Stored",
                journalSubtitleStyle);
        }

        private List<JournalInventoryEntry> GetJournalInventoryEntries()
        {
            List<JournalInventoryEntry> entries = new();

            HashSet<string> favoriteIds = new();
            for (int i = 0; i < favoriteHotbarItemIds.Length; i++)
            {
                string favoriteId = favoriteHotbarItemIds[i];
                if (!string.IsNullOrWhiteSpace(favoriteId))
                {
                    favoriteIds.Add(favoriteId);
                }
            }

            if (wood > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "resource_wood",
                    "Wood",
                    wood,
                    "Resource",
                    false));
            }

            if (stone > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "resource_stone",
                    "Stone",
                    stone,
                    "Resource",
                    false));
            }

            if (food > 0)
            {
                entries.Add(new JournalInventoryEntry(
                    "carry_food",
                    "Food On Hand",
                    food,
                    "Food",
                    false));
            }

            foreach (KeyValuePair<string, int> pair in storedFoodInventory.OrderBy(k => k.Key))
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                string displayName = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                entries.Add(new JournalInventoryEntry(
                    pair.Key,
                    displayName,
                    pair.Value,
                    "Food",
                    favoriteIds.Contains(pair.Key)));
            }

            foreach (KeyValuePair<string, int> pair in storedWeaponInventory.OrderBy(k => k.Key))
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                string displayName = catalogLookup.TryGetValue(pair.Key, out BuildCatalogItem item)
                    ? item.displayName
                    : pair.Key;

                entries.Add(new JournalInventoryEntry(
                    pair.Key,
                    displayName,
                    pair.Value,
                    "Gear",
                    favoriteIds.Contains(pair.Key)));
            }

            return entries
                .OrderByDescending(e => e.isFavorited)
                .ThenBy(e => e.categoryLabel)
                .ThenBy(e => e.displayName)
                .ToList();
        }

        private JournalInventoryEntry? GetSelectedInventoryEntry(List<JournalInventoryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            // Priority 1: explicit inventory card selection
            if (!string.IsNullOrWhiteSpace(selectedInventoryItemId))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].itemId, selectedInventoryItemId, StringComparison.Ordinal))
                    {
                        return entries[i];
                    }
                }
            }

            // Priority 2: hotbar favorite selection
            string hotbarItemId = null;
            if (selectedHotbarIndex >= 0 && selectedHotbarIndex < favoriteHotbarItemIds.Length)
            {
                hotbarItemId = favoriteHotbarItemIds[selectedHotbarIndex];
            }

            if (!string.IsNullOrWhiteSpace(hotbarItemId))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].itemId, hotbarItemId, StringComparison.Ordinal))
                    {
                        return entries[i];
                    }
                }
            }

            // Priority 3: first favorited, then first entry
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].isFavorited)
                {
                    return entries[i];
                }
            }

            return entries[0];
        }

        private string GetInventoryCardLabel(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "Unknown";
            }

            return displayName.Length <= 18 ? displayName : $"{displayName[..16]}..";
        }

        private void DrawInventorySelectionDetails(JournalInventoryEntry? selectedEntry)
        {
            if (!selectedEntry.HasValue)
            {
                Rect emptyRect = GUILayoutUtility.GetRect(
                    10f, 120f,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(120f));

                DrawBorderPanel(emptyRect, journalCardTexture, 1f);

                Rect inner = new Rect(
                    emptyRect.x + 12f, emptyRect.y + 10f,
                    emptyRect.width - 24f, emptyRect.height - 20f);

                GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f),
                    "No item selected", headingStyle);
                GUI.DrawTexture(
                    new Rect(inner.x, inner.y + 24f, inner.width, 2f),
                    amberAccentTexture);
                GUI.Label(
                    new Rect(inner.x, inner.y + 34f, inner.width, 40f),
                    "Pick an inventory card to inspect its details.",
                    journalSubtitleStyle);
                return;
            }

            JournalInventoryEntry entry = selectedEntry.Value;

            bool isEquipment = catalogLookup.TryGetValue(
                entry.itemId, out BuildCatalogItem eqItem) &&
                GetEquipmentInfo(eqItem) != null;

            bool currentlyEquipped = IsItemEquipped(entry.itemId);

            float cardHeight = Mathf.Clamp(
                isEquipment ? 230f * currentUiScale : 164f * currentUiScale,
                isEquipment ? 200f : 148f,
                isEquipment ? 360f : 260f);

            Rect cardRect = GUILayoutUtility.GetRect(
                10f, cardHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(cardHeight));

            DrawBorderPanel(cardRect, journalCardTexture, 1f);

            Rect inner2 = new Rect(
                cardRect.x + 12f, cardRect.y + 10f,
                cardRect.width - 24f, cardRect.height - 20f);

            GUI.Label(
                new Rect(inner2.x, inner2.y, inner2.width - 28f, 20f),
                entry.displayName, headingStyle);

            if (currentlyEquipped)
            {
                GUI.Label(
                    new Rect(inner2.xMax - 60f, inner2.y, 60f, 16f),
                    "✦ Equip'd", questDoneStyle);
            }

            GUI.DrawTexture(
                new Rect(inner2.x, inner2.y + 24f, inner2.width, 2f),
                amberAccentTexture);

            GUI.Label(
                new Rect(inner2.x, inner2.y + 34f, inner2.width, 18f),
                entry.categoryLabel, smallMutedStyle);

            GUI.Label(
                new Rect(inner2.x, inner2.y + 56f, inner2.width, 20f),
                $"Quantity: x{entry.quantity}", labelStyle);

            if (isEquipment)
            {
                EquipmentInfo def = GetEquipmentInfo(eqItem);

                GUI.DrawTexture(
                    new Rect(inner2.x, inner2.y + 82f, inner2.width, 1f),
                    amberAccentTexture);

                string typeLine = "Item";

                if (def.isAmmo)
                    typeLine = $"Ammo  ·  {def.ammoType}";
                else if (def.isWeapon)
                    typeLine = $"Weapon  ·  {def.weaponType}";
                else if (def.isArmor)
                    typeLine = $"Armor  ·  {def.armorSlot}";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 90f, inner2.width, 16f),
                    typeLine, journalSubtitleStyle);

                string statsLine = string.Empty;

                if (def.isAmmo)
                {
                    string ammoLabel = def.ammoType == AmmoType.Arrows ? "Used by bows" : "Ammo item";
                    statsLine = $"Ammo Stack x{entry.quantity}   {ammoLabel}";
                }
                else if (def.stats != null)
                {
                    statsLine = def.isWeapon
                        ? $"Damage +{def.stats.damageBonus}   " +
                          $"Cooldown {def.stats.attackCooldown:F2}s   " +
                          $"Speed ×{def.stats.moveSpeedMultiplier:F2}"
                        : $"Block {Mathf.RoundToInt(def.stats.damageReduction * 100f)}%   " +
                          $"Speed ×{def.stats.moveSpeedMultiplier:F2}";
                }

                if (!string.IsNullOrWhiteSpace(statsLine))
                {
                    GUI.Label(
                        new Rect(inner2.x, inner2.y + 110f, inner2.width, 32f),
                        statsLine, journalBodyStyle);
                }

                string slotHint = currentlyEquipped
                    ? $"In slot: {GetEquippedSlotKey(entry.itemId)}"
                    : "Not equipped";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 148f, inner2.width, 16f),
                    slotHint, smallMutedStyle);
            }
            else
            {
                string infoText = entry.categoryLabel == "Food"
                    ? "Consumable or harvested supply."
                    : "Stored inventory item.";

                GUI.Label(
                    new Rect(inner2.x, inner2.y + 82f, inner2.width, 28f),
                    infoText, journalSubtitleStyle);
            }
        }
    }
}
