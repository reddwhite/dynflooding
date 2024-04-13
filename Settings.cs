using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Verse.Noise;
using static UnityEngine.GraphicsBuffer;
using HarmonyLib;
using UnityEngine;
using Verse.AI;
using RimWorld.Planet;
using System.Collections;
using Verse.Sound;
using System.Security.Cryptography;
using System.Reflection.Emit;
using System.Net.NetworkInformation;
using static UnityEngine.TouchScreenKeyboard;
using System.Runtime.Remoting.Messaging;


namespace Flooding
{
    public class DFlooding : Mod
    {
        public static DSettings settings;

        public DFlooding(ModContentPack content) : base(content)
        {
            settings = GetSettings<DSettings>();
        }

        public override string SettingsCategory() => "Dynamic Flooding";
        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.FillRect(inRect);
        }
    }

    public class DSettings : ModSettings
    {

        public static int[][] FloodLevelVals =
        {
            new int[]{0, 2, 4, 5},
            new int[]{2, 2, 8, 15},
            new int[]{2, 4, 12, 25},
            new int[]{2, 10, 40, 500}
        };

        public int FloodLevel = FloodLevel_Default;
        public const int FloodLevel_Default = 1;


        public static int[][] PuddleLevelVals =
        {
            new int[]{ },
            new int[]{ },
            new int[]{ },
            new int[]{ }
        };

        public int PuddleLevel = PuddleLevel_Default;
        public const int PuddleLevel_Default = 2;


        static int[][] CleanSpeedVals =
        {
            new int[]{ },
            new int[]{ },
            new int[]{ },
            new int[]{ }
        };

        public int CleanSpeed = CleanSpeed_Default;
        public const int CleanSpeed_Default = 1;

        public bool WetSoil = WetSoil_Default;
        public const bool WetSoil_Default = false;

        public bool DisableManMadeTileFlood = DisableManMadeTileFlood_Default;
        public const bool DisableManMadeTileFlood_Default = false;

        public bool DisableBridgeTileFlood = DisableBridgeTileFlood_Default;
        public const bool DisableBridgeTileFlood_Default = false;

        public bool PlantEffects = PlantEffects_Default;
        public const bool PlantEffects_Default = true;


        public void FillRect(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);


            list.Label("Flooding", -1f, "Controls how much flooding can happen");
            if (list.RadioButton("Off", FloodLevel == -1, 8f, (string)null))
            {
                FloodLevel = -1;
            }
            if (list.RadioButton("Low", FloodLevel == 0, 8f, (string)null)){
                FloodLevel = 0;
            }
            if (list.RadioButton("Medium", FloodLevel == 1, 8f, (string)null)){
                FloodLevel = 1;
            }
            if (list.RadioButton("High", FloodLevel == 2, 8f, (string)null)){
                FloodLevel = 2;
            }
            if (list.RadioButton("Extreme", FloodLevel == 3, 8f, (string)null)){
                FloodLevel = 3;
            }

            list.Gap(16f);


            list.Label("Puddle Level (to be added)", -1f, "Controls how much puddles spawn");
            /*
            if (list.RadioButton("Off", FloodLevel == 1, 8f, (string)null)){
                FloodLevel = 0;
            }
            if (list.RadioButton("Low", FloodLevel == 2, 8f, (string)null)){
                FloodLevel = 1;
            }
            if (list.RadioButton("Medium", FloodLevel == 3, 8f, (string)null)){
                FloodLevel = 2;
            }
            if (list.RadioButton("High", FloodLevel == 4, 8f, (string)null)){
                FloodLevel = 3;
            }
            */

            list.Label("Water Dry Speed (to be added)", -1f, "Controls how fast water dries after rain");
            /*
            if (list.RadioButton("Quicker", FloodLevel == 1, 8f, (string)null)){
                FloodLevel = 0;
            }
            if (list.RadioButton("Normal", FloodLevel == 2, 8f, (string)null)){
                FloodLevel = 1;
            }
            if (list.RadioButton("Slower", FloodLevel == 3, 8f, (string)null)){
                FloodLevel = 2;
            }
            list.Gap(16f);
            */


            list.Gap(16f);
            list.CheckboxLabeled("Enable wet soil after rain (may cause lag)", ref WetSoil, "");
            list.Gap();
            list.CheckboxLabeled("Disable flooding on man-made tiles", ref DisableManMadeTileFlood, "");
            list.Gap();
            list.CheckboxLabeled("Disable flooding on bridges", ref DisableBridgeTileFlood, "");
            list.Gap(16f);
            list.CheckboxLabeled("Plant effects", ref PlantEffects, "");
            list.Gap(16f);
            if (list.ButtonText("Reset Configuration"))
            {
                Reset();
            }
            if (list.ButtonText("Clear modded water"))
            {
                Find.Maps.ForEach((map) =>
                {
                    // End conditions
                    GameConditionDef cnd = DefDatabase<GameConditionDef>.GetNamed("swabluFloodHandler");
                    GameCondition active = map.GameConditionManager.GetActiveCondition(cnd);
                    if (active != null)
                    {
                        active.End();
                    }
                    map.GetComponent<WaterGrid>().ForceClear();
                });
            }

            list.End();
        }

        public void Reset()
        {
            FloodLevel = FloodLevel_Default;
            PuddleLevel = PuddleLevel_Default;
            CleanSpeed = CleanSpeed_Default;
            WetSoil = WetSoil_Default;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref FloodLevel, nameof(FloodLevel), FloodLevel_Default);
            Scribe_Values.Look(ref PuddleLevel, nameof(PuddleLevel), PuddleLevel_Default);
            Scribe_Values.Look(ref CleanSpeed, nameof(CleanSpeed), CleanSpeed_Default);
            Scribe_Values.Look(ref WetSoil, nameof(WetSoil), WetSoil_Default);
            Scribe_Values.Look(ref DisableManMadeTileFlood, nameof(DisableManMadeTileFlood), DisableManMadeTileFlood_Default);
            Scribe_Values.Look(ref DisableBridgeTileFlood, nameof(DisableBridgeTileFlood), DisableBridgeTileFlood_Default);
            Scribe_Values.Look(ref PlantEffects, nameof(PlantEffects), PlantEffects_Default);

        }
    }
}
