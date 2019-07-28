using UnityModManagerNet;
using System;
using System.Reflection;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;

namespace CL29
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool enableLinearScale = false;
        public int maxLevel = Main.normalMax;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
    public class Main
    {
        public static UnityModManagerNet.UnityModManager.ModEntry.ModLogger logger;
        public static UnityModManager.ModEntry ModEntry;
        public static Settings ModSettings;
        public const int normalMax = 29;
        public const int absoluteMax = 64;
        public const int casterMax = 60;
        private static bool modEnabled = true;
        private static bool tablePatched = false;

        static readonly Dictionary<Type, bool> typesPatched = new Dictionary<Type, bool>();
        static readonly List<String> failedPatches = new List<String>();
        static readonly List<String> failedLoading = new List<String>();

        [System.Diagnostics.Conditional("DEBUG")]
        public static void DebugLog(string msg)
        {
            if (logger != null) logger.Log(msg);
        }
        public static void DebugError(Exception ex)
        {
            if (logger != null) logger.Log(ex.ToString() + "\n" + ex.StackTrace);
        }
        static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                ModSettings = UnityModManager.ModSettings.Load<Settings>(modEntry);
                modEnabled = modEntry.Active;
                modEntry.OnSaveGUI = OnSaveGui;
                modEntry.OnToggle = OnToggle;
                modEntry.OnGUI = OnGui;
                logger = modEntry.Logger;
                Main.DebugLog("Loading CL29");
                var harmony = Harmony12.HarmonyInstance.Create(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                DebugError(ex);
                throw ex;
            }
            return true;
        }

        internal static Exception Error(String message)
        {
            logger?.Log(message);
            return new InvalidOperationException(message);
        }



        /* up max characterlevel to 29, still need to fix some stuff */
        public static bool updateXPTable()
        {
            /* add XP using the doubling-previous-level-need rule from PNP */
            /* beyond 29 impossible, as it requires > 2**31 XP (specifically, 2151900000) ... */
            /* ... unless changing the scale */
            // need to redo this every time, might have changed
            {
                int thebase;
                try
                {
                    thebase = Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses[20];
                }
                catch (Exception e)
                {// BlueprintLibrary.LibraryObject not loaded yet
                    return false;
                }
                int theincr = 2 * (thebase - Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses[19]);
                int ctr = 0;
                System.Array.Resize(ref Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses, 1 + absoluteMax); // in case we switched back, full table
                for (int i = 21; i <= absoluteMax; i++)
                {
                    if (!ModSettings.enableLinearScale)
                    {
                        thebase += theincr;
                        theincr *= 2;
                        if (i < 30)
                            Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses.SetValue(thebase, i);
                        else
                            Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses.SetValue(0x7FFFFFFF - (absoluteMax - i), i);
                    }
                    else
                    {
                        ctr += (i - 20);
                        Game.Instance.BlueprintRoot.Progression.XPTable.Bonuses.SetValue(thebase + theincr * ctr, i);
                    }
                }
            }
            /* extend the feat-per-character-level table */
            //if (Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries.Length < 15)
            if (!tablePatched)
            {
                System.Array.Resize(ref Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries, 20); // we stop at Lvl 40, otherwise the UI crashes
                for (int i = 10; i < 20; i++)
                {
                    Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries[i] =
                        new LevelEntry();
                    Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries[i].Level = i * 2 + 1;
                    Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries[i].Features =
                            Game.Instance.BlueprintRoot.Progression.FeatsProgression.LevelEntries[9].Features;
                }
            }

            string[] spontidlist = { "b3a505fb61437dc4097f43c3f8f9a4cf" /* sorcerer */};
            foreach (string spontid in spontidlist)
            {
                BlueprintCharacterClass sorcerer = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(spontid);
                if (sorcerer == null)
                    logger.Error($"Can't find blueprint # {spontid} for spontaneous caster.");
                //if (sorcerer != null && sorcerer.Spellbook.SpellsPerDay.Levels.Length < (1 + casterMax))
                if (sorcerer != null && !tablePatched)
                {
                    logger.Warning($"Fixing Spells for {sorcerer.Name}");
                    System.Array.Resize(ref sorcerer.Spellbook.SpellsPerDay.Levels, (1 + casterMax));
                    System.Array.Resize(ref sorcerer.Spellbook.SpellsKnown.Levels, (1 + casterMax));
                    for (int i = 21; i < (1 + casterMax); i++)
                    {
                        sorcerer.Spellbook.SpellsPerDay.Levels[i] = new SpellsLevelEntry();
                        sorcerer.Spellbook.SpellsPerDay.Levels[i].Count = new int[10]; // going beyond LvL9 crashes the UI
                        sorcerer.Spellbook.SpellsPerDay.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 10; j++)
                        {
                            sorcerer.Spellbook.SpellsPerDay.Levels[i].Count[j] =
                                sorcerer.Spellbook.SpellsPerDay.Levels[i - 1].Count[j];
                        }
                        // add a Lvl9 + Lvl1, Lvl9 + Lvl2, etc. instead
                        sorcerer.Spellbook.SpellsPerDay.Levels[i].Count[9]++;
                        sorcerer.Spellbook.SpellsPerDay.Levels[i].Count[((i - 21) % 9) + 1]++;

                        sorcerer.Spellbook.SpellsKnown.Levels[i] = new SpellsLevelEntry();
                        sorcerer.Spellbook.SpellsKnown.Levels[i].Count = new int[10]; // going beyond LvL9 crashes the UI
                        sorcerer.Spellbook.SpellsKnown.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 10; j++)
                        {
                            sorcerer.Spellbook.SpellsKnown.Levels[i].Count[j] =
                                sorcerer.Spellbook.SpellsKnown.Levels[i - 1].Count[j];
                        }
                        // arbitrary no bonus, fixme
                    }
                }
            }

            string[] learnidlist = { "ba34257984f4c41408ce1dc2004e342e" /* wizard */,
                                 "67819271767a9dd4fbfd4ae700befea0" /* cleric */,
                                 "610d836f3a3a9ed42a4349b62f002e96" /* druid */};
            // "799265ebe0ed27641b6d415251943d03" /* crusader */
            foreach (string learnid in learnidlist)
            {
                BlueprintCharacterClass wizard = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(learnid);
                if (wizard == null)
                    logger.Error($"Can't find blueprint # {learnid} for learned caster.");
                //if (wizard != null && wizard.Spellbook.SpellsPerDay.Levels.Length < 41)
                if (wizard != null && !tablePatched)
                {
                    logger.Warning($"Fixing Spells for {wizard.Name}");
                    System.Array.Resize(ref wizard.Spellbook.SpellsPerDay.Levels, (1 + casterMax));
                    for (int i = 21; i < (1 + casterMax); i++)
                    {
                        wizard.Spellbook.SpellsPerDay.Levels[i] = new SpellsLevelEntry();
                        wizard.Spellbook.SpellsPerDay.Levels[i].Count = new int[10]; // going beyond LvL9 crashes the UI
                        wizard.Spellbook.SpellsPerDay.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 10; j++)
                        {
                            wizard.Spellbook.SpellsPerDay.Levels[i].Count[j] = wizard.Spellbook.SpellsPerDay.Levels[i - 1].Count[j];
                        }
                        // add a Lvl9 + Lvl1, Lvl9 + Lvl2, etc. instead
                        wizard.Spellbook.SpellsPerDay.Levels[i].Count[9]++;
                        wizard.Spellbook.SpellsPerDay.Levels[i].Count[((i - 21) % 9) + 1]++;
                    }
                }
            }

            string[] semispontidlist = {"772c83a25e2268e448e841dcd548235f" /* bard */,
                                "f1a70d9e1b0b41e49874e1fa9052a1ce" /* inquisitor */};
            foreach (string learnid in semispontidlist)
            {
                BlueprintCharacterClass bard = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(learnid);
                if (bard == null)
                    logger.Error($"Can't find blueprint # {learnid} for spontaneous half-caster.");
                //if (bard != null && bard.Spellbook.SpellsPerDay.Levels.Length < 41)
                if (bard != null && !tablePatched)
                {
                    logger.Warning($"Fixing Spells for {bard.Name}");
                    System.Array.Resize(ref bard.Spellbook.SpellsPerDay.Levels, (1 + casterMax));
                    System.Array.Resize(ref bard.Spellbook.SpellsKnown.Levels, (1 + casterMax));
                    for (int i = 21; i < (1 + casterMax); i++)
                    {
                        bard.Spellbook.SpellsPerDay.Levels[i] = new SpellsLevelEntry();
                        bard.Spellbook.SpellsPerDay.Levels[i].Count = new int[7];
                        bard.Spellbook.SpellsPerDay.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 7; j++)
                        {
                            bard.Spellbook.SpellsPerDay.Levels[i].Count[j] =
                                bard.Spellbook.SpellsPerDay.Levels[i - 1].Count[j];
                        }
                        bard.Spellbook.SpellsPerDay.Levels[i].Count[6]++;
                        bard.Spellbook.SpellsPerDay.Levels[i].Count[(i - 21) % 6 + 1]++;

                        bard.Spellbook.SpellsKnown.Levels[i] = new SpellsLevelEntry();
                        bard.Spellbook.SpellsKnown.Levels[i].Count = new int[7];
                        bard.Spellbook.SpellsKnown.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 7; j++)
                        {
                            bard.Spellbook.SpellsKnown.Levels[i].Count[j] =
                                bard.Spellbook.SpellsKnown.Levels[i - 1].Count[j];
                        }
                    }
                }
            }

            string[] semilearnidlist = { "45a4607686d96a1498891b3286121780" /* magus */ };
            /* 0937bec61c0dabc468428f496580c721 alchemist can't stack - not an arcane caster */
            foreach (string learnid in semilearnidlist)
            {
                BlueprintCharacterClass magus = ResourcesLibrary.TryGetBlueprint<BlueprintCharacterClass>(learnid);
                if (magus == null)
                    logger.Error($"Can't find blueprint # {learnid} for learned half-caster.");
                //if (magus != null && magus.Spellbook.SpellsPerDay.Levels.Length < 41)
                if (magus != null && !tablePatched)
                {
                    logger.Warning($"Fixing Spells for {magus.Name}");
                    System.Array.Resize(ref magus.Spellbook.SpellsPerDay.Levels, (1 + casterMax));
                    for (int i = 21; i < (1 + casterMax); i++)
                    {
                        magus.Spellbook.SpellsPerDay.Levels[i] = new SpellsLevelEntry();
                        magus.Spellbook.SpellsPerDay.Levels[i].Count = new int[7];
                        magus.Spellbook.SpellsPerDay.Levels[i].Count[0] = 0;
                        for (int j = 1; j < 7; j++)
                        {
                            magus.Spellbook.SpellsPerDay.Levels[i].Count[j] =
                                magus.Spellbook.SpellsPerDay.Levels[i - 1].Count[j];
                        }
                        magus.Spellbook.SpellsPerDay.Levels[i].Count[6]++;
                        magus.Spellbook.SpellsPerDay.Levels[i].Count[(i - 21) % 6 + 1]++;
                    }
                }
            }
            tablePatched = true;
            return true;
        }

        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.Class.LevelUp.LevelUpController), "GetEffectiveLevel")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch1
        {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(UnitEntityData unit, int __result)
            {
                if (!tablePatched)
                    updateXPTable();
                unit = (unit ?? Game.Instance.Player.MainCharacter.Value);
                int? num = (unit != null) ? new int?(unit.Descriptor.Progression.CharacterLevel) : null;
                int i = (num == null) ? 1 : num.Value;
                int? num2 = (unit != null) ? new int?(unit.Descriptor.Progression.Experience) : null;
                int num3 = (num2 == null) ? 1 : num2.Value;
                while (i < ModSettings.maxLevel)
                {
                    if (Game.Instance.BlueprintRoot.Progression.XPTable.GetBonus(i + 1) > num3)
                    {
                        break;
                    }
                    i++;
                }
                __result = i;
                return false;
            }
        }
        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.Class.LevelUp.LevelUpController), "CanLevelUp")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch2
        {
            private static bool Prefix(UnitDescriptor unit, ref bool __result)
            {
                if (!tablePatched)
                    updateXPTable();
                __result = false;
                if (Game.Instance.Player.IsInCombat)
                {
                    return false;
                }
                if (unit.State.IsDead)
                {
                    return false;
                }
                int characterLevel = unit.Progression.CharacterLevel;
                if (characterLevel >= ModSettings.maxLevel)
                {
                    return false;
                }
                int bonus = Game.Instance.BlueprintRoot.Progression.XPTable.GetBonus(characterLevel + 1);
                __result = bonus <= unit.Progression.Experience;
                return false;
            }
        }

        [Harmony12.HarmonyPatch(typeof(Kingmaker.UI.ServiceWindow.CharacterScreen.CharSLevel), "FillData")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch3
        {
            private static bool Prefix()
            {
                if (!tablePatched)
                    updateXPTable();
                return true;
            }
        }

        [Harmony12.HarmonyPatch(typeof(Kingmaker.UI.ServiceWindow.CharSheetCommonLevel), "Initialize")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch4
        {
            private static bool Prefix()
            {
                if (!tablePatched)
                    updateXPTable();
                return true;
            }
        }

        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.UnitProgressionData), "AddClassLevel")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch5
        {
            private static bool Prefix()
            {
                if (!tablePatched)
                    updateXPTable();
                return true;
            }
        }

        /* Ugly hack to avoid the UI crashing when adding at level > 20 */
        [Harmony12.HarmonyPatch(typeof(Kingmaker.UI.ServiceWindow.CharacterScreen.CharSComponentFeatsBlock), "GenerateUIFeature")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch6
        {
            private static bool Prefix(ref int level)
            {
                if (level > 20)
                    level = 41 - level;
                return true;
            }
        }


        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.UnitProgressionData), "GainExperience")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch7
        {
            private static bool Prefix(MethodBase __originalMethod, UnitProgressionData __instance, ref int exp, bool log)
            {
                // logger.Log($"Trying to add {exp} XP to a current value of {__instance.Experience}, using {Game.Instance.Player.ExperienceRatePercent}%.");
                const int limit = 10000000;
                int temp = exp;
                if (temp <= limit)
                    return true;
                while (temp > limit)
                {
                    Object[] p = { limit, log };
                    __originalMethod.Invoke(__instance, p);
                    temp -= limit;
                    // logger.Log($" ... intermediate result is  {__instance.Experience}");
                }
                exp = temp;
                // logger.Log($" ... final result is  {__instance.Experience}");
                return true;
            }
        }

        private static void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            ModSettings.Save(modEntry);
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            modEnabled = enabled;
            ModSettings.maxLevel = enabled ? (ModSettings.enableLinearScale ? absoluteMax : normalMax) : 20;
            logger.Log($"CL29: limiting Character Level to {ModSettings.maxLevel}{(enabled ? (ModSettings.enableLinearScale ? " (linear scale)" : " (normal scale)") : " (-disabled-)")}");
            updateXPTable();
            return true;
        }

        private static void OnGui(UnityModManager.ModEntry modEntry)
        {
            try
            {
                UnityEngine.GUILayout.BeginHorizontal();
                if (UnityEngine.GUILayout.Button($"{(ModSettings.enableLinearScale ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>")} Enable a more linear XP scale (level max is {absoluteMax} with linear scale, {normalMax} without).", UnityEngine.GUILayout.ExpandWidth(false)))
                {
                    ModSettings.enableLinearScale = !ModSettings.enableLinearScale;
                    ModSettings.maxLevel = modEnabled ? (ModSettings.enableLinearScale ? absoluteMax : normalMax) : 20;
                    updateXPTable();
                }
                UnityEngine.GUILayout.EndHorizontal();
            }
            catch (Exception e)
            {
                modEntry.Logger.Error($"Error rendering GUI: {e}");
            }
        }

    }
}
