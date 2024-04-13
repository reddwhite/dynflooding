using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse.AI;
using Verse;

namespace Flooding
{
    // Patch weather events

    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {

        static GameConditionDef cnddef = DefDatabase<GameConditionDef>.GetNamed("swabluFloodHandler");

        static HarmonyPatches()
        {
            Harmony inst = new Harmony("rimworld.floodingmod");
            inst.Patch(AccessTools.Method(typeof(WeatherManager), nameof(WeatherManager.TransitionTo)), postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Raincatcher)));
            //inst.Patch(AccessTools.DeclaredPropertyGetter(typeof(GameCondition), nameof(GameCondition.HiddenByOtherCondition)), postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(ConditionIsActiveFaker)));
            inst.Patch(AccessTools.Method(typeof(GameCondition), nameof(GameCondition.HiddenByOtherCondition)), postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(ConditionIsActiveFaker)));
            inst.Patch(AccessTools.Method(typeof(FireUtility), nameof(FireUtility.ChanceToStartFireIn)), postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(ChanceToStartFireIn)));
            inst.Patch(AccessTools.Method(typeof(FireUtility), nameof(FireUtility.CanEverAttachFire)), postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CanEverAttachFire)));
            inst.PatchAll();
        }

        // Hide condition
        public static void ConditionIsActiveFaker(GameCondition __instance, Map map, ref bool __result)
        {
            if (__instance.def.defName.Equals("swabluFloodHandler"))
            {
                __result = true;
            }
        }

        // Catches weather transitions
        static void Raincatcher(ref WeatherManager __instance, WeatherDef newWeather)
        {

            // Check for a previous instance of rain handler
            var rainInstance = (FloodHandler)__instance.map.GameConditionManager.GetActiveCondition(cnddef);

            // Possible weathers to trigger rain water handler
            string[] weathers = { "FoggyRain", "Rain", "RainyThunderstorm" };
            if (weathers.Contains(newWeather.defName))
            {

                // Create a new game condition to manage flooding
                var cnd = GameConditionMaker.MakeConditionPermanent(cnddef);
                __instance.map.GameConditionManager.RegisterCondition(cnd);
                var newRainHandler = (FloodHandler)__instance.map.GameConditionManager.GetActiveCondition(cnddef);


                if (rainInstance != null)
                {
                    rainInstance.EndWithoutCleaning();
                }
                return;

            }


            // Non-water weather triggered, signal water clearing
            if (rainInstance != null)
            {
                // Do clear flood transition
                rainInstance.SignalCleaning();
            }
        }


        // No fire in floodwater
        public static void ChanceToStartFireIn(IntVec3 c, Map map, SimpleCurve flammabilityChanceCurve, ref float __result)
        {
            if (__result == 0)
            {
                return;
            }

            if (map.GetComponent<WaterGrid>().GetDepth(c) > 0)
            {
                __result = 0;
            }
        }


        // No fire in floodwater
        public static void CanEverAttachFire(this Thing t, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (t != null && t.Spawned)
            {
                if (t.Map.GetComponent<WaterGrid>().GetDepth(t.Position) > 0)
                {
                    __result = false;
                }
            }
        }
    }


    // Verbatim Sandstorms code to draw floodwater label
    [HarmonyPatch(typeof(CellInspectorDrawer), "DrawMapInspector")]
    public static class Patch_CellInspectorDrawer
    {
        [HarmonyPostfix]
        public static void AddSandCategory()
        {
            IntVec3 cell = UI.MouseCell();
            Map map = Find.CurrentMap;
            SnowCategory sandDepthAtCell = map.GetComponent<WaterGrid>().GetDepthAsCategory(cell);
            if (sandDepthAtCell == SnowCategory.None)
            {
                return;
            }
            Reroute_DrawRow("Floodwaters ".Translate(), SnowUtility.GetDescription(sandDepthAtCell).CapitalizeFirst());
        }

        static Action<string, string> drawRowDelegate;
        /// <summary>
        /// Private static method. Reflection will call the original <see cref="CellInspectorDrawer.DrawRow"/>
        /// </summary>
        private static void Reroute_DrawRow(string label, string info)
        {
            if (drawRowDelegate == default)
            {
                MethodInfo drawRowMI = AccessTools.Method(typeof(CellInspectorDrawer), "DrawRow");
                drawRowDelegate = Delegate.CreateDelegate(typeof(Action<string, string>), drawRowMI) as Action<string, string>;
            }

            drawRowDelegate(label, info);
        }
    }

    public static class WaterMovement
    {
        public static int MovementTicksAddOn(SnowCategory category)
        {
            // TODO: unfortunately can't get this working
            return SnowUtility.MovementTicksAddOn(category);
        }
    }


    // Verbatim Sandstorms code to add cost to path grid
    [HarmonyPatch(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt))]
    public static class Patch_PathGrid
    {

        private static readonly MethodInfo movementTicksAddOnMI = AccessTools.Method(typeof(SnowUtility), nameof(SnowUtility.MovementTicksAddOn));
       // private static readonly MethodInfo movementTicksAddOnMI = AccessTools.Method(typeof(WaterMovement), nameof(WaterMovement.MovementTicksAddOn));
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ConsiderFallenSand(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codeInstructions = instructions.ToList();

            int anchorIndex = codeInstructions.FindIndex(ci => ci.Calls(movementTicksAddOnMI));
            anchorIndex += 2;

            codeInstructions.InsertRange(anchorIndex, InjectedInstructions());

            return codeInstructions.AsEnumerable();
        }

        private static IEnumerable<CodeInstruction> InjectedInstructions()
        {
            yield return new CodeInstruction(OpCodes.Ldloc_S, 4);   // num2 (movementCost)
            yield return new CodeInstruction(OpCodes.Ldarg_0);  // this
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PathGrid), "map"));    //.map
            yield return new CodeInstruction(OpCodes.Ldarg_1);  // c (gridCell)
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_PathGrid), nameof(OverwriteWithSandMovementIfPossible)));    //Patch_PathGrid.OverwriteWithSandMovementIfPossible
            yield return new CodeInstruction(OpCodes.Stloc_S, 4); // num2 (movementCost)
        }

        private static int OverwriteWithSandMovementIfPossible(int originalMovement, Map map, IntVec3 cell)
        {
            SnowCategory sandDephtCategory = map.GetComponent<WaterGrid>().GetDepthAsCategory(cell);
            //int sandMovement = SnowUtility.MovementTicksAddOn(sandDephtCategory);
            int sandMovement = WaterMovement.MovementTicksAddOn(sandDephtCategory);
            return Math.Max(originalMovement, sandMovement);
        }
    }


    // Remove water on construction finish
    [HarmonyPatch(typeof(JobDriver_ConstructFinishFrame), "MakeNewToils")]
    public static class Patch_JobDriver_ConstructFinishFrame
    {
        [HarmonyPostfix]
        public static IEnumerable<Toil> ClearSand(IEnumerable<Toil> __result, JobDriver_ConstructFinishFrame __instance)
        {

            Toil buildToil = __result.Last();
            foreach (Toil item in __result.Except(buildToil))
            {
                yield return item;
            }
            BuildableDef entityDefToBuild = __instance.job?.targetA.Thing?.def?.entityDefToBuild;
            if (entityDefToBuild != null)
            {
                buildToil.AddFinishAction(delegate
                {
                    __instance.pawn.Map.GetComponent<WaterGrid>().ClearWater(__instance.job.targetA.Cell);
                });
            }
            yield return buildToil;
        }
    }

    // Remove water on construction finish
    [HarmonyPatch(typeof(JobDriver_AffectFloor), "MakeNewToils")]
    public static class Patch_JobDriver_AffectFloor
    {
        [HarmonyPostfix]
        public static IEnumerable<Toil> ClearSand(IEnumerable<Toil> __result, JobDriver_AffectFloor __instance, bool ___clearSnow)
        {
            Toil workToil = __result.Last();
            foreach (Toil item in __result.Except(workToil))
            {
                yield return item;
            }
            if (___clearSnow)
            {
                workToil.AddFinishAction(delegate
                {
                    __instance.pawn.Map.GetComponent<WaterGrid>().ClearWater(__instance.job.targetA.Cell);
                });
            }
            yield return workToil;
        }
    }


    // Verbatim Sandstorms code to draw floodwater label
    [HarmonyPatch(typeof(MouseoverReadout), "MouseoverReadoutOnGUI")]
    public static class Patch_MouseoverReadout
    {
        static Patch_MouseoverReadout()
        {
            FieldInfo botLeftFI = AccessTools.Field(typeof(MouseoverReadout), "BotLeft");
            BotLeft = (Vector2)botLeftFI.GetValue(null);
        }

        private static Vector2 BotLeft;
        private static readonly FieldInfo snowGridFI = AccessTools.Field(typeof(Map), nameof(Map.snowGrid));
        private static readonly MethodInfo displaySandMI = AccessTools.Method(typeof(Patch_MouseoverReadout), nameof(Patch_MouseoverReadout.DisplayWater));
        const float yOffsetPerEntry = 19f;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> AddWaterDetails(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codeInstructions = instructions.ToList();

            int anchorIndex = codeInstructions.FindIndex(ci => ci.LoadsField(snowGridFI));
            anchorIndex += 3;

            codeInstructions.InsertRange(anchorIndex, InjectedInstructions());

            return codeInstructions.AsEnumerable();
        }

        private static IEnumerable<CodeInstruction> InjectedInstructions()
        {
            yield return new CodeInstruction(OpCodes.Ldloca_S, 1);
            yield return new CodeInstruction(OpCodes.Call, displaySandMI);
        }

        private static void DisplayWater(ref float curYOffset)
        {
            IntVec3 cell = UI.MouseCell();
            SnowCategory waterDepthCategory = Find.CurrentMap.GetComponent<WaterGrid>().GetDepthAsCategory(cell);

            if (waterDepthCategory == SnowCategory.None)
            {
                return;
            }
            float rectY = UI.screenHeight - BotLeft.y - curYOffset;
            Rect rect = new Rect(BotLeft.x, rectY, 999f, 999f);
            string walkSpeedString = GenPath.SpeedPercentString(WaterMovement.MovementTicksAddOn(waterDepthCategory));
            string label = $"{"Floodwaters"} ({"WalkSpeed".Translate(walkSpeedString)})";
            Widgets.Label(rect, label);
            curYOffset += yOffsetPerEntry;
        }
    }
}
