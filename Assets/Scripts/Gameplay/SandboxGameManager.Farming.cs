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
        // ══════════════════════════════════════════════════════════════
        //  Seed Planting
        // ══════════════════════════════════════════════════════════════

        private void BeginPlantingSeed(string seedId)
        {
            if (!seedToCropMap.TryGetValue(seedId, out string cropId))
                return;
            if (!catalogLookup.TryGetValue(cropId, out BuildCatalogItem cropItem))
                return;

            // Check player has seeds
            if (!storedFoodInventory.TryGetValue(seedId, out int seedCount) || seedCount <= 0)
            {
                SetStatusMessage("No seeds remaining.");
                return;
            }

            // Enter placement mode with the crop as ghost preview
            plantingMode = true;
            plantingSeedId = seedId;
            placementActive = true;
            deleteMode = false;
            buildPanelOpen = false;
            pointerMode = false;
            placementYaw = 0f;

            // Use crop item for the ghost
            CleanupGhost();
            EnsureGhost(cropItem);
            SetStatusMessage($"Select a spot to plant {GetDisplayNameForItemId(seedId)}.");
        }

        private void TryPlantSeed()
        {
            if (!plantingMode || string.IsNullOrWhiteSpace(plantingSeedId))
                return;

            if (!seedToCropMap.TryGetValue(plantingSeedId, out string cropId))
            {
                SetStatusMessage("Unknown seed type.");
                CancelPlacement();
                return;
            }

            if (!catalogLookup.TryGetValue(cropId, out BuildCatalogItem cropItem))
            {
                SetStatusMessage("Crop not found in catalog.");
                CancelPlacement();
                return;
            }

            if (!placementHasValidTarget)
            {
                SetStatusMessage("Move the placement farther from the player.");
                return;
            }

            // Consume 1 seed
            if (!storedFoodInventory.TryGetValue(plantingSeedId, out int seedCount) || seedCount <= 0)
            {
                SetStatusMessage("No seeds remaining.");
                CancelPlacement();
                return;
            }

            RemoveInventoryCount(storedFoodInventory, plantingSeedId, 1);

            // Spawn the crop at growth 0 — it will grow over time via RenewableNode.Update()
            GameObject planted = SpawnCatalogItem(
                cropItem,
                Guid.NewGuid().ToString("N"),
                pendingPlacementPosition,
                pendingPlacementRotation,
                0f, // seedling — growth starts at zero
                registerForSave: true,
                placedByPlayer: true,
                alignToGround: true);

            if (planted == null)
            {
                // Refund seed on failure
                AddInventoryCount(storedFoodInventory, plantingSeedId, 1);
                SetStatusMessage("Failed to plant seed.");
                CancelPlacement();
                return;
            }

            // Enable growth label so the player can see progress
            RenewableNode node = planted.GetComponent<RenewableNode>();
            if (node != null)
            {
                // showGrowthLabel is already set from the catalog item, but ensure it's on for planted crops
                // (The catalog entry for TomatoPlant/Cabbage may or may not have it enabled)
            }

            SaveWorld();

            string seedName = GetDisplayNameForItemId(plantingSeedId);
            SetStatusMessage($"Planted {seedName}! It will take about {Mathf.RoundToInt(cropItem.renewableRegrowSeconds)}s to grow.");

            // Check if player has more seeds to continue planting
            storedFoodInventory.TryGetValue(plantingSeedId, out int remaining);
            if (remaining > 0)
            {
                CleanupGhost();
                EnsureGhost(cropItem);
            }
            else
            {
                SetStatusMessage($"Planted {seedName}! No more seeds of this type.");
                CancelPlacement();
            }
        }

        private void TryBeginPlantingFirstSeed()
        {
            // Find the first seed the player has in inventory
            foreach (KeyValuePair<string, string> pair in seedToCropMap)
            {
                if (storedFoodInventory.TryGetValue(pair.Key, out int count) && count > 0)
                {
                    BeginPlantingSeed(pair.Key);
                    return;
                }
            }

            SetStatusMessage("No seeds in inventory. Craft seeds from food at the crafting panel.");
        }
    }
}
