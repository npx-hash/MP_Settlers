using System;
using System.Collections.Generic;
using UnityEngine;

namespace MPSettlers.Gameplay
{
    public enum SkillType
    {
        Attack,
        Ranged,
        Bows,
        Farming,
        Cooking,
        Woodcutting,
        Mining,
        Smithing,
        Crafting,
        Building
    }

    // ── Skill metadata ─────────────────────────────────────────────
    public static class SkillDefinitions
    {
        public static readonly SkillType[] All =
        {
            SkillType.Attack,
            SkillType.Ranged,
            SkillType.Bows,
            SkillType.Farming,
            SkillType.Cooking,
            SkillType.Woodcutting,
            SkillType.Mining,
            SkillType.Smithing,
            SkillType.Crafting,
            SkillType.Building
        };

        public static string GetDisplayName(SkillType skill)
        {
            return skill switch
            {
                SkillType.Attack => "Attack",
                SkillType.Ranged => "Ranged",
                SkillType.Bows => "Bows",
                SkillType.Farming => "Farming",
                SkillType.Cooking => "Cooking",
                SkillType.Woodcutting => "Woodcutting",
                SkillType.Mining => "Mining",
                SkillType.Smithing => "Smithing",
                SkillType.Crafting => "Crafting",
                SkillType.Building => "Building",
                _ => skill.ToString()
            };
        }

        public static string GetDescription(SkillType skill)
        {
            return skill switch
            {
                SkillType.Attack => "Melee combat effectiveness",
                SkillType.Ranged => "Ranged combat accuracy",
                SkillType.Bows => "Bow handling and damage",
                SkillType.Farming => "Crop planting and harvesting",
                SkillType.Cooking => "Food preparation and recipes",
                SkillType.Woodcutting => "Tree felling efficiency",
                SkillType.Mining => "Ore and stone extraction",
                SkillType.Smithing => "Metal working and forging",
                SkillType.Crafting => "General item creation",
                SkillType.Building => "Structure placement and design",
                _ => string.Empty
            };
        }
    }

    // ── XP progression curve ───────────────────────────────────────
    //
    //  Uses a piecewise polynomial curve:
    //    Levels  1–40:  base * level^1.5    (fast ramp, quick dopamine)
    //    Levels 41–80:  base * level^2.2    (noticeable slowdown)
    //    Levels 81–120: base * level^2.8    (serious grind, prestige territory)
    //
    //  Cumulative XP required is precomputed once for O(1) lookups.
    //  Level 1 starts at 0 XP. Level 2 requires ~83 XP. Level 120 requires ~38M XP.

    public static class SkillProgression
    {
        public const int MaxLevel = 120;
        private const float BaseFactor = 10f;

        // cumulativeXp[level] = total XP needed to reach that level.
        // Index 1 = 0 (you start at level 1 with 0 xp).
        // Index 120 = total XP to reach level 120.
        // Index 0 is unused (no level 0).
        private static readonly long[] cumulativeXp;

        static SkillProgression()
        {
            cumulativeXp = new long[MaxLevel + 1];
            cumulativeXp[0] = 0;
            cumulativeXp[1] = 0;

            long total = 0;
            for (int level = 2; level <= MaxLevel; level++)
            {
                long delta = XpDeltaForLevel(level);
                total += delta;
                cumulativeXp[level] = total;
            }
        }

        /// <summary>XP required to go from (level-1) to (level).</summary>
        private static long XpDeltaForLevel(int level)
        {
            if (level <= 1) return 0;

            double exponent;
            if (level <= 40)
                exponent = 1.5;
            else if (level <= 80)
                exponent = 2.2;
            else
                exponent = 2.8;

            return (long)Math.Ceiling(BaseFactor * Math.Pow(level, exponent));
        }

        /// <summary>Total XP required to be at the start of the given level.</summary>
        public static long GetXpForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            return cumulativeXp[level];
        }

        /// <summary>XP needed to go from current level to next level.</summary>
        public static long GetXpToNextLevel(int level)
        {
            if (level >= MaxLevel) return 0;
            level = Mathf.Clamp(level, 1, MaxLevel - 1);
            return cumulativeXp[level + 1] - cumulativeXp[level];
        }

        /// <summary>Determine the level for a given total XP amount.</summary>
        public static int GetLevelForXp(long totalXp)
        {
            if (totalXp <= 0) return 1;

            // Binary search through cumulative table
            int lo = 1;
            int hi = MaxLevel;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (cumulativeXp[mid] <= totalXp)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return lo;
        }

        /// <summary>Progress fraction (0.0–1.0) through the current level.</summary>
        public static float GetProgressToNextLevel(long totalXp)
        {
            int level = GetLevelForXp(totalXp);
            if (level >= MaxLevel) return 1f;

            long levelStart = cumulativeXp[level];
            long levelEnd = cumulativeXp[level + 1];
            long range = levelEnd - levelStart;
            if (range <= 0) return 1f;

            return Mathf.Clamp01((float)(totalXp - levelStart) / range);
        }

        /// <summary>Total XP across the whole progression table (for display).</summary>
        public static long GetMaxTotalXp()
        {
            return cumulativeXp[MaxLevel];
        }
    }

    // ── Per-skill runtime data ─────────────────────────────────────

    public class SkillRuntimeData
    {
        public SkillType skillType;
        public long totalXp;

        public int Level => SkillProgression.GetLevelForXp(totalXp);
        public float Progress => SkillProgression.GetProgressToNextLevel(totalXp);
        public long XpToNext => SkillProgression.GetXpToNextLevel(Level);
        public long XpIntoLevel => totalXp - SkillProgression.GetXpForLevel(Level);

        public SkillRuntimeData(SkillType type)
        {
            skillType = type;
            totalXp = 0;
        }
    }

    // ── Skill state manager ────────────────────────────────────────

    public class SkillState
    {
        private readonly Dictionary<SkillType, SkillRuntimeData> skills = new();

        public SkillState()
        {
            foreach (SkillType skill in SkillDefinitions.All)
            {
                skills[skill] = new SkillRuntimeData(skill);
            }
        }

        public SkillRuntimeData Get(SkillType skill)
        {
            return skills.TryGetValue(skill, out SkillRuntimeData data) ? data : null;
        }






        /// <summary>Award XP to a skill. Returns the new level (handles multi-level-ups).</summary>
        public int AddXp(SkillType skill, long amount)
        {
            if (amount <= 0) return GetLevel(skill);

            SkillRuntimeData data = Get(skill);
            if (data == null) return 1;

            int previousLevel = data.Level;

            // Clamp at max level XP
            long maxXp = SkillProgression.GetXpForLevel(SkillProgression.MaxLevel);
            data.totalXp = Math.Min(data.totalXp + amount, maxXp);

            return data.Level;
        }

        public int GetLevel(SkillType skill)
        {
            SkillRuntimeData data = Get(skill);
            return data?.Level ?? 1;
        }

        public long GetXp(SkillType skill)
        {
            SkillRuntimeData data = Get(skill);
            return data?.totalXp ?? 0;
        }

        public float GetProgress(SkillType skill)
        {
            SkillRuntimeData data = Get(skill);
            return data?.Progress ?? 0f;
        }

        public int GetTotalLevel()
        {
            int total = 0;
            foreach (SkillRuntimeData data in skills.Values)
                total += data.Level;
            return total;
        }

        public long GetTotalXp()
        {
            long total = 0;
            foreach (SkillRuntimeData data in skills.Values)
                total += data.totalXp;
            return total;
        }

        public IEnumerable<SkillRuntimeData> AllSkills => skills.Values;






        // ── Save/Load ──────────────────────────────────────────────

        public SkillsSaveData Capture()
        {
            SkillsSaveData save = new();
            foreach (SkillRuntimeData data in skills.Values)
            {
                save.entries.Add(new SkillEntrySaveData
                {
                    skillName = data.skillType.ToString(),
                    totalXp = (int)Math.Min(data.totalXp, int.MaxValue)
                });
            }
            return save;
        }

        public void Restore(SkillsSaveData save)
        {
            // Reset all to 0 first
            foreach (SkillRuntimeData data in skills.Values)
                data.totalXp = 0;

            if (save?.entries == null) return;

            foreach (SkillEntrySaveData entry in save.entries)
            {
                if (string.IsNullOrWhiteSpace(entry.skillName)) continue;
                if (!Enum.TryParse(entry.skillName, true, out SkillType type)) continue;
                if (!skills.TryGetValue(type, out SkillRuntimeData data)) continue;

                long maxXp = SkillProgression.GetXpForLevel(SkillProgression.MaxLevel);
                data.totalXp = Math.Min(Math.Max(0, (long)entry.totalXp), maxXp);
            }
        }
    }

    // ── Save data ──────────────────────────────────────────────────

    [Serializable]
    public class SkillEntrySaveData
    {
        public string skillName;
        public int totalXp; // JsonUtility does not serialize long; max ~38M fits int
    }

    [Serializable]
    public class SkillsSaveData
    {
        public List<SkillEntrySaveData> entries = new();
    }
}
