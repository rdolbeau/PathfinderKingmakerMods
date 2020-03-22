using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class.LevelUp.Actions;
using Kingmaker.Blueprints.Classes.Selection;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityModManagerNet;

namespace CL29
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool enableLinearScale = false;
        public bool enableSuperPet = false;
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
            if (!tablePatched)
            {
                if (!updateAnimalCompanion())
                {
                    logger.Warning($"Failed to update AnimalCompanion");
                }
            }
            tablePatched = true;
            return true;
        }


        public static T GetBlueprint<T>(string value) where T : BlueprintScriptableObject
        {
            T res = Kingmaker.Cheats.Utilities.GetBlueprint<T>(value);
            if (res == null)
            {
                logger.Error($"Failed to load Blueprint of name {value}, whose type should be {typeof(T).ToString()}");
            }
            else
            {
                //Main.logger.Log($"Found Blueprint of name {value}, whose type is {typeof(T).ToString()}, with ID {res.GetInstanceID().ToString()}");
            }
            return res;
        }

        // {} [] <> ()
        // duplicated / expanded from Kingmaker.UnitLogic.FactLogic.AddPet.RankToLevel
        private static readonly int[] RankToLevel = new int[]
        {
            0,
            2,
            3,
            3,
            4,
            5,
            6,
            6,
            7,
            8,
            9,
            9,
            10,
            11,
            12,
            12,
            13,
            14,
            15,
            15,
            16,
            17, 18, 18, 19, 20, 21, 21, 22, 23, 24,
            24, 25, 26, 27, 27, 28, 29, 30, 30, 31
        };
        // {} [] <> ()
        private static bool updateAnimalCompanion()
        {
            const int extraLevel = 20;
            {
                BlueprintArchetype aca = GetBlueprint<BlueprintArchetype>("AnimalCompanionArchetype");
                if (aca == null)
                    return false;
                int acal = aca.AddFeatures.Length;
                logger.Log($"AnimalCompanionArchetype AddFeatures.Length: {acal}");
                // LevelEntry source = aca.AddFeatures[acal - 1] as LevelEntry;
                // if (source == null)
                // return false;
                System.Array.Resize(ref aca.AddFeatures, acal + extraLevel);
                for (int i = acal; i < acal + extraLevel; i++)
                {
                    LevelEntry nl = new LevelEntry();
                    nl.Level = aca.AddFeatures[i - 1].Level + 1;
                    nl.Features = new List<BlueprintFeatureBase>();
                    if (nl.Level % 2 == 0)
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionNaturalArmor");
                        if (bf != null)
                        {
                            bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionNaturalArmor");
                    }
                    else
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionStrengthDexterityConstitution");
                        if (bf != null)
                        {
                            bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionStrengthDexterityConstitution");
                    }
                    if (nl.Level % 3 == 0)
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionAttacksEnhancement");
                        if (bf != null)
                        {
                            bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionAttacksEnhancement");
                    }
                    aca.AddFeatures[i] = nl;
                }
                logger.Log($"AnimalCompanionArchetype NEW AddFeatures.Length: {aca.AddFeatures.Length}");
            }
            {
                BlueprintProgression acbp = GetBlueprint<BlueprintProgression>("AnimalCompanionBonusesProgression");
                if (acbp == null)
                    return false;
                int acbpl = acbp.LevelEntries.Length;
                logger.Log($"AnimalCompanionBonusesProgression LevelEntries.Length: {acbpl}");
                // LevelEntry source = aca.AddFeatures[acal - 1] as LevelEntry;
                // if (source == null)
                // return false;
                System.Array.Resize(ref acbp.LevelEntries, acbpl + extraLevel);
                for (int i = acbpl; i < acbpl + extraLevel; i++)
                {
                    LevelEntry nl = new LevelEntry();
                    nl.Level = acbp.LevelEntries[i - 1].Level + 1;
                    nl.Features = new List<BlueprintFeatureBase>();
                    if (nl.Level % 2 == 0)
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionNaturalArmor");
                        if (bf != null)
                        {
                            //bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionNaturalArmor");
                    }
                    else
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionStrengthDexterityConstitution");
                        if (bf != null)
                        {
                            //bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionStrengthDexterityConstitution");
                    }
                    if (nl.Level % 3 == 0)
                    {
                        BlueprintFeature bf = GetBlueprint<BlueprintFeature>("AnimalCompanionAttacksEnhancement");
                        if (bf != null)
                        {
                            //bf.Ranks++;
                            nl.Features.Add(bf);
                        }
                        else logger.Warning($"Can't find AnimalCompanionAttacksEnhancement");
                    }
                    acbp.LevelEntries[i] = nl;
                }
                logger.Log($"AnimalCompanionBonusesProgression NEW LevelEntries.Length: {acbp.LevelEntries.Length}");

            }


            BlueprintFeature acr = GetBlueprint<BlueprintFeature>("AnimalCompanionRank");
            if (acr == null)
                return false;
            acr.Ranks += extraLevel;
            logger.Log($"AnimalCompanionRank NEW Ranks: {acr.Ranks}");
            //            Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild.Name
            if (Game.Instance.BlueprintRoot.Progression.AnimalCompanion != null)
            {
                if (Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild != null)
                {
                    if (Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild.Name != null)
                    {
                        logger.Log($"Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild.Name is {Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild.Name}");
                    }
                    else
                    {
                        logger.Warning($"Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild.Name is null");
                    }
                }
                else
                {
                    logger.Warning($"Game.Instance.BlueprintRoot.Progression.AnimalCompanion.DefaultBuild is null");
                    List<String> lf = Harmony12.Traverse.Create(Game.Instance.BlueprintRoot.Progression.AnimalCompanion).Fields();
                    foreach (String f in lf)
                    {
                        Harmony12.Traverse fin = Harmony12.Traverse.Create(Game.Instance.BlueprintRoot.Progression.AnimalCompanion).Field(f);
                        logger.Log($"Object {Game.Instance.BlueprintRoot.Progression.AnimalCompanion.ToString()}: field \"{f}\" (value \"{fin.GetValue()}\")");
                    }
                }
            }
            else
            {
                logger.Warning($"Game.Instance.BlueprintRoot.Progression.AnimalCompanion is null");
            }
            //if (true)
            {
                String[] allpets = {
                "AnimalCompanionUnit",
                "AnimalCompanionUnitBear",
                "AnimalCompanionUnitBoar",
                "AnimalCompanionUnitCentipede",
                "AnimalCompanionUnitDog",
                "AnimalCompanionUnitElk",
                "AnimalCompanionUnitLeopard",
                "AnimalCompanionUnitMammoth",
                "AnimalCompanionUnitMonitor",
                "AnimalCompanionUnitSmilodon",
                "AnimalCompanionUnitWolf"};
                String[] extrafeats = {
                    "BlindFight",
                    "Improved Initiative", // yes, with a space...
                    "BlindFightImproved",
                    "Mobility",
                    "BlindFightGreater",
                    "LightningReflexes",
                    "BlindingCriticalFeature",
                    "Outflank",
                    "SiezeTheMoment" // yes, with that spelling...
                    };
                foreach (String thepet in allpets)
                {
                    BlueprintUnit p = GetBlueprint<BlueprintUnit>(thepet);
                    if (p == null)
                    {
                        logger.Warning($"Can't find {thepet}");
                        continue;
                    }
                    AddClassLevels acl = p.GetComponent<AddClassLevels>();
                    if (acl == null)
                    {
                        logger.Warning($"Can't find AddClassLevels for {thepet}");
                        continue;
                    }
                    int sl = acl.Selections.Length;
                    /* if (sl > 1)
                    {
                        logger.Warning($"Chopping acl.Selections...");
                        System.Array.Resize<SelectionEntry>(ref acl.Selections, 1);
                        sl = 1;
                    } */
                    for (int i = 0; i < sl; i++)
                    {
                        int fl = acl.Selections[i].Features.Length;

                        logger.Log($"{thepet} had {fl} features");
                        System.Array.Resize<BlueprintFeature>(ref acl.Selections[i].Features, fl + extrafeats.Length);
                        for (int j = 0; j < extrafeats.Length; j++)
                        {
                            BlueprintFeature bf = GetBlueprint<BlueprintFeature>(extrafeats[j]);
                            acl.Selections[i].Features[fl + j] = bf;
                            if (bf == null)
                                logger.Warning($"Couldn't find {extrafeats[j]} for {thepet}");
                        }
                        logger.Log($"{thepet} now has {acl.Selections[i].Features.Length} features");
                    }
                }
            }
            return true;
        }


        // {} [] <> ()
        [Harmony12.HarmonyPatch(typeof(LevelUpController), "GetEffectiveLevel")]
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
        [Harmony12.HarmonyPatch(typeof(LevelUpController), "CanLevelUp")]
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

        [Harmony12.HarmonyPatch(typeof(UnitProgressionData), "AddClassLevel")]
        // ReSharper disable once UnusedMember.Local
        private static class MoreCharacterLevelPatch5
        {
            private static bool Prefix(/* ref */ UnitProgressionData __instance)
            {
                if (!tablePatched)
                    updateXPTable();
                //logger.Log($"AddClassLevel called on ${__instance.Owner.ToString()}");
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
                do
                {
                    if (level > 20)
                        level = 41 - level;
                    if (level <= 0)
                        level = 1 - level;
                } while ((level > 20) || (level <= 0));
                return true;
            }
        }


        [Harmony12.HarmonyPatch(typeof(UnitProgressionData), "GainExperience")]
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

        /*
        [Harmony12.HarmonyPatch(typeof(Kingmaker.Designers.Mechanics.Facts.CompanionBoon), "Apply")]
        // ReSharper disable once UnusedMember.Local
        private static class AnimalCompanionPatch1
        {
            private static void Postfix(Kingmaker.Designers.Mechanics.Facts.CompanionBoon __instance)
            {
                List<String> lf = Harmony12.Traverse.Create(__instance).Fields();
                foreach (String f in lf)
                {
                    Harmony12.Traverse fin = Harmony12.Traverse.Create(__instance).Field(f);
                    logger.Log($"Object {__instance.ToString()}: field \"{f}\" (value \"{fin.GetValue()}\")");
                }

                UnitDescriptor owner = __instance.Owner;
                lf = Harmony12.Traverse.Create(owner).Fields();
                foreach (String f in lf)
                {
                    Harmony12.Traverse fin = Harmony12.Traverse.Create(owner).Field(f);
                    logger.Log($"Object {owner.ToString()}: field \"{f}\" (value \"{fin.GetValue()}\")");
                }
                logger.Log($"Object {owner.ToString()}: final rank is {owner.GetFact(__instance.RankFeature).GetRank()}"); ;
            }
        }
        */

        // {} [] <> ()
        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.FactLogic.AddPet), "GetPetLevel")]
        // ReSharper disable once UnusedMember.Local
        private static class AnimalCompanionPatch2
        {
            public static bool Prefix(ref Kingmaker.UnitLogic.FactLogic.AddPet __instance, ref int __result)
            {
                if (!ModSettings.enableSuperPet)
                    return true;
                if (!__instance.LevelRank)
                {
                    __result = 1;
                    return false;
                }
                int a = RankToLevel.Length;
                Fact fact = __instance.Owner.GetFact(__instance.LevelRank);
                int? num = (fact != null) ? new int?(fact.GetRank()) : null;
                int num2 = UnityEngine.Mathf.Min(a, (num == null) ? 0 : num.Value);
                __result = RankToLevel[num2];
                logger.Log($"AddPet.GetPetLevel: returning {__result}");
                return false;
            }
        }
        // {} [] <> ()
        [Harmony12.HarmonyPatch(typeof(Kingmaker.UnitLogic.FactLogic.AddPet), "TryLevelUpPet")]
        // ReSharper disable once UnusedMember.Local
        private static class AnimalCompanionPatch3
        {
            private static bool Prefix(ref Kingmaker.UnitLogic.FactLogic.AddPet __instance)
            {
                if (!ModSettings.enableSuperPet)
                    return true;
                if (__instance.SpawnedPet == null)
                {
                    return false;
                }
                AddClassLevels component = __instance.SpawnedPet.Blueprint.GetComponent<AddClassLevels>();
                if (!component)
                {
                    return false;
                }
                int characterLevel = __instance.SpawnedPet.Descriptor.Progression.CharacterLevel;
                //int petLevel = __instance.GetPetLevel(); // function is private, and we have duplicated it ...
                int petLevel = 0;
                AnimalCompanionPatch2.Prefix(ref __instance, ref petLevel);
                int num = petLevel - characterLevel;
                if (num > 0)
                {
                    component.LevelUp(__instance.SpawnedPet.Descriptor, num);
                }
                int a = RankToLevel.Length;
                Fact fact = __instance.Owner.GetFact(__instance.LevelRank);
                int? num2 = (fact != null) ? new int?(fact.GetRank()) : null;
                int num3 = UnityEngine.Mathf.Min(a, (num2 == null) ? 0 : num2.Value);
                if (num3 >= __instance.UpgradeLevel)
                {
                    __instance.SpawnedPet.Descriptor.Progression.Features.AddFeature(__instance.UpgradeFeature, null);
                }
                return false;
            }
        }

        
        // {} [] <> ()
        [Harmony12.HarmonyPatch(typeof(ProgressionData), "RebuildLevelEntries")]
        // ReSharper disable once UnusedMember.Local
        private static class AnimalCompanionPatch4
        {
            public static bool Prefix(ref ProgressionData __instance)
            {
                if (!ModSettings.enableSuperPet)
                    return true;
                if (__instance.Archetypes.Count <= 0)
                {
                    __instance.LevelEntries = __instance.Blueprint.LevelEntries;
                    return false;
                }
                List<LevelEntry> list = new List<LevelEntry>();
                for (int i = 1; i <= RankToLevel[RankToLevel.Length - 1]; i++)
                {
                    List<BlueprintFeatureBase> list2 = new List<BlueprintFeatureBase>(__instance.Blueprint.GetLevelEntry(i).Features);
                    foreach (BlueprintArchetype blueprintArchetype in __instance.Archetypes)
                    {
                        foreach (BlueprintFeatureBase item in blueprintArchetype.GetRemoveEntry(i).Features)
                        {
                            list2.Remove(item);
                        }
                        list2.AddRange(blueprintArchetype.GetAddEntry(i).Features);
                    }
                    if (list2.Count > 0)
                    {
                        list.Add(new LevelEntry
                        {
                            Features = list2,
                            Level = i
                        });
                    }
                }
                __instance.LevelEntries = list.ToArray();
                return false;
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
            try
            {
                UnityEngine.GUILayout.BeginHorizontal();
                if (UnityEngine.GUILayout.Button($"{(ModSettings.enableSuperPet ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>")} Enable scaling of pets", UnityEngine.GUILayout.ExpandWidth(false)))
                {
                    ModSettings.enableSuperPet = !ModSettings.enableSuperPet;
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
