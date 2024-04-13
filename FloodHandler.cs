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

    // Manager; handles generation of floodcells and cleanup
    public class FloodHandler : GameCondition
    {
        // Global flags
        static int MODE_FLOOD = 0;
        static int MODE_CLEAN = 1;
        static float cleanChance = 0.5f;
        static int cleanTicks = 3500;       
        static int floodTicks = 4000;       // Flood ticks is only the initial ticks until the flood starts
        static float basePuddleChance = 0.4f;
        static int waterChunk = 300;
        static int maxPuddles = 30;
        static int cycleType = 0;


        // Data structures
        Map map;
        WaterGrid waterGrid;
        HashSet<IntVec3> lowCells = new HashSet<IntVec3>();      // Active flood cells
        // HashSet<IntVec3> occupiedCells = new HashSet<IntVec3>(); // If cleaning
        HashSet<IntVec3> puddleCells = new HashSet<IntVec3>();          // Auxillary puddles
        HashSet<IntVec3> deferredCells = new HashSet<IntVec3>();

        // Variable flags
        int state = MODE_FLOOD;
        int ticksUntilNextEvent = floodTicks;
        int totalCyclesElasped = 0;
        float puddleChance = basePuddleChance;
        int ticksElasped = 0;
        int waterNewCellsCap = 1;
        int chunkPerTile = 3;


        // Wet soil module
        // A list of tiles of the map in random order
        // Value will be null if wetsoil module not enabled
        List<IntVec3> changeSoil = null;

        // Position within changeSoil
        int csPos = 0;

        // Wet soil definition
        public static TerrainDef WetSoilDef = DefDatabase<TerrainDef>.GetNamed("DF_WetSoil");

        /* Init */
        public override void Init()
        {
            this.map = SingleMap;
            waterGrid = map.GetComponent<WaterGrid>();

            // Add all water sources to the grid
            var sources = waterGrid.FindWaterSources();
            lowCells.UnionWith(sources);
            this.RegenerateWater();

            // Puddle chance dependent on water sources
            if (lowCells.Count() > 500)
            {
                puddleChance -= 0.2f;
            } 
            else if (lowCells.Count() < 100)
            {
                puddleChance += 0.25f;
            }

            // Skip cycles if rained already
            if (this.lowCells.Count() - sources.Count() > 100)
            {
                // Skip cycles
                this.totalCyclesElasped = 5;
            }

            // Wet soil module
            if (DFlooding.settings.WetSoil)
            {
                changeSoil = this.map.AllCells.InRandomOrder().ToList();
                waterGrid.GetOldTerra();
            }
        }

        /* Tick Stages */
        public override void GameConditionTick()
        {
            if (state == MODE_FLOOD)
            {
                DoFloodEffect();
            } else if (state == MODE_CLEAN)
            {
                DoFloodCleaningEffect();
            }
        }

        private void DoFloodEffect()
        {
            if (DFlooding.settings.FloodLevel < 0)
            {
                if (changeSoil != null && csPos < changeSoil.Count && ticksElasped++ % 7 == 0)
                {
                    ReloadIfNeeded();
                    MakeSoilWet();
                }
                return;
            }

            // Water rises after set number of ticks
            if (ticksElasped++ >= ticksUntilNextEvent)
            {
                ReloadIfNeeded();
                totalCyclesElasped++;

                // Verse.Log.Message("Lowcells count grow: iter " + totalCyclesElasped + " " + lowCells.Count + " puddles: " + puddleCells.Count);

                // Cycle strategy
                waterNewCellsCap = 1;
                chunkPerTile = 1;
                int[] floodSetting = DSettings.FloodLevelVals[DFlooding.settings.FloodLevel];
                if (totalCyclesElasped < floodSetting[0])
                {
                    ticksUntilNextEvent = 5000;
                    waterNewCellsCap = 3; // Make rain smoother
                    chunkPerTile = 5;
                    cycleType = 0;
                }
                else if (totalCyclesElasped < floodSetting[1])
                {
                    waterNewCellsCap = 2;
                    ticksUntilNextEvent = 5000;
                    cycleType = 1;
                } else if (totalCyclesElasped < floodSetting[2])
                {
                    ticksUntilNextEvent = 7000;
                    chunkPerTile = 2;
                    cycleType = 2;
                } else if (totalCyclesElasped < floodSetting[3])
                {
                    ticksUntilNextEvent = 7500;
                    cycleType = 3;
                } else {
                    chunkPerTile = 1;
                    ticksUntilNextEvent = 2500 * 24;
                    cycleType = 4;
                }

                // Begin loop for flood cells
                // Defer all actions
                deferredCells.UnionWith(lowCells);
                lowCells.Clear();

                // Do action for puddles
                PuddleAction();
            } else if (deferredCells.Count() > 0 && ticksElasped % 88 == 0)
            {
                ReloadIfNeeded();
                FloodAction();

            } else if (changeSoil != null && csPos < changeSoil.Count && ticksElasped % 7 == 0)
            {
                ReloadIfNeeded();
                MakeSoilWet();
            }
        }

        private void DoFloodCleaningEffect()
        {
            if (ticksElasped++ >= ticksUntilNextEvent)
            {
                ReloadIfNeeded();
                //Verse.Log.Message("Lowcells count shrink: " + lowCells.Count + " " + occupiedCells.Count());
                if (lowCells.Count == 0)
                {
                    // Ensure everything is cleaned up
                    this.End();
                    return;
                }

                // Do cleanup
                HashSet<IntVec3> newCells = new HashSet<IntVec3>();
                foreach (IntVec3 cell in lowCells)
                {
                    if (!Rand.Chance(cleanChance))
                    {
                        newCells.Add(cell);
                        continue;
                    }
                    
                    waterGrid.SetDepth(cell, 0);
                    newCells.UnionWith(GenAdjFast.AdjacentCellsCardinal(cell).Where((nc) => waterGrid.GetDepth(nc) > 0));
                }

                lowCells = newCells;
                ticksElasped = 0;
            } else if (changeSoil != null && csPos < changeSoil.Count && ticksElasped % 7 == 0)
            {
                ReloadIfNeeded();
                MakeSoilDry();
            }

        }

        private void FloodAction()
        {
            // Do deferred action
            int waterSpawn = 0;
            HashSet<IntVec3> newDeferred = new HashSet<IntVec3>();
            foreach (IntVec3 cell in deferredCells)
            {
                if (waterSpawn++ >= waterChunk)
                {
                    newDeferred.Add(cell);
                    continue;
                }

                // Cell is blocked now
                if (!waterGrid.CanSetDepth(cell, 0.01f))
                {
                    continue;
                }

                // Set
                if (totalCyclesElasped <= 1)
                {
                    waterGrid.SetDepth(cell, 1);
                }

                // Group addition strategy
                // Make more random after c > 2
                IEnumerable<IntVec3> neighbors = null;
                if (cycleType < 1)
                {
                    neighbors = GenAdjFast.AdjacentCellsCardinal(cell).Where((nc) => waterGrid.CanSetDepth(nc, 1.0f));
                }
                else
                {
                    neighbors = GenAdjFast.AdjacentCells8Way(cell).Where((nc) => waterGrid.CanSetDepth(nc, 1.0f));
                }

                if (neighbors.Count() == 0)
                {
                    continue;
                }

                int i2 = 0;
                foreach (IntVec3 neighbor in neighbors.InRandomOrder())
                {
                    if (i2 < waterNewCellsCap && waterGrid.SetDepth(neighbor, 1))
                    {
                        lowCells.Add(neighbor);
                        i2++;
                    }
                    else if (i2 >= waterNewCellsCap)
                    {
                        if (i2++ == chunkPerTile)
                        {
                            break;
                        }
                        waterGrid.SetDepth(neighbor, 1);
                    }
                }
            }
            deferredCells = newDeferred;
        }
        private void PuddleAction()
        {
            // Think about adding a new random puddle
            if (puddleCells.Count() < maxPuddles && Rand.Chance(puddleChance))
            {
                // Try 10 candidate selections
                for (int i = 0; i < 10; i++)
                {
                    int times = 3;
                    IntVec3 randCell = map.AllCells.RandomElement();
                    if (!randCell.Roofed(map) && waterGrid.SetDepth(randCell, 0.7f))
                    {
                        puddleCells.Add(randCell);
                        if (times-- <= 0)
                        {
                            break;
                        }
                    }
                }
            }

            // Puddles have a different behavior.
            // They advance infrequently and their behavior is less regular
            HashSet<IntVec3> newPuddles = new HashSet<IntVec3>();
            foreach (IntVec3 cell in puddleCells)
            {
                // Cell is blocked now
                if (!waterGrid.CanSetDepth(cell, 0.01f))
                {
                    continue;
                }

                if (waterGrid.GetDepth(cell) < 0.7f)
                {
                    if (!waterGrid.SetDepth(cell, 0.7f))
                    {
                        continue;
                    }
                }

                // Group addition strategy
                var neighbors = GenAdjFast.AdjacentCellsCardinal(cell).Where((nc) => waterGrid.CanSetDepthMaximum(nc, 0.7f));
                if (neighbors.Count() == 0)
                {
                    continue;
                }

                bool notAdded = true;
                for (int i = 0; i < 2; i++)
                {
                    IntVec3 neighbor = neighbors.RandomElement();
                    if (waterGrid.GetDepth(neighbor) < 0.7f && waterGrid.SetDepth(neighbor, 0.7f) && notAdded)
                    {
                        newPuddles.Add(neighbor);
                        notAdded = false;
                    }
                }
            }
            puddleCells = newPuddles;
            ticksElasped = 0;
        }

        /* Stage changing */
        public void SignalCleaning()
        {
            if (state == MODE_CLEAN)
            {
                return;
            }

            ReloadIfNeeded();

            state = MODE_CLEAN;
            ticksUntilNextEvent = cleanTicks;
            ticksElasped = 0;
            lowCells.Clear();
            csPos = 0;
            foreach (IntVec3 cell in map.AllCells){
                if (waterGrid.GetDepth(cell) > 0)
                {
                    // Priortize cells with only 2 or fewer neighbor cells
                    int freeNeighbors = GenAdjFast.AdjacentCellsCardinal(cell).Aggregate(0, (a, b) => a + (waterGrid.CanSetDepthMaximum(b, 0.1f) ? 1 : 0));
                    if (freeNeighbors >= 2)
                    {
                        lowCells.Add(cell);
                    }
                }
            }
            // Verse.Log.Message("Ending signal with " + occupiedCells.Count() + " " + lowCells.Count());
        }

        public void EndWithoutCleaning()
        {
            // Verse.Log.Message("Terminating");
            base.End();
        }
        
        public override void End()
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                waterGrid.SetDepth(cell, 0);
            }

            if (changeSoil != null)
            {
                csPos = 0;
                while (csPos < changeSoil.Count)
                {
                    MakeSoilDry();
                }
                changeSoil = null;
                waterGrid.GetOldTerra().Clear();
            }

            base.End();
        }

        // Look at potential water sources that were missed
        public void RegenerateWater()
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                if (waterGrid.GetDepth(cell) != 1)
                {
                    continue;
                }

                var freeNeighbors = GenAdjFast.AdjacentCellsCardinal(cell).FindAll((a) => waterGrid.CanSetDepth(a, 1));
                lowCells.UnionWith(freeNeighbors);
            }
        }


        /* Game reloading */
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref state, "state", 0, forceSave: true);
            Scribe_Values.Look(ref ticksUntilNextEvent, "ticksUntilNextEvent", 0, forceSave: true);
            Scribe_Values.Look(ref totalCyclesElasped, "totalCyclesElasped", 0, forceSave: true);
            Scribe_Values.Look(ref puddleChance, "puddleChance", 0, forceSave: true);
            Scribe_Values.Look(ref ticksElasped, "ticksElasped", 0, forceSave: true);

            Scribe_Collections.Look(ref lowCells, "lowCells", LookMode.Value);
            //Scribe_Collections.Look(ref occupiedCells, "occupiedCells", LookMode.Value);
            Scribe_Collections.Look(ref puddleCells, "puddleCells", LookMode.Value);
            Scribe_Collections.Look(ref deferredCells, "deferredCells", LookMode.Value);

            // Wet soil
            Scribe_Collections.Look(ref changeSoil, "changeSoil", LookMode.Value);
            Scribe_Values.Look(ref csPos, "csPos", 0, forceSave: true);
        }

        private void ReloadIfNeeded()
        {
            if (map != null)
            {
                return;
            }

            this.map = SingleMap;
            waterGrid = map.GetComponent<WaterGrid>();
        }


        /* Change Soil Module */
        private void MakeSoilWet()
        {
            int modCells = 0;
            int orig = csPos;
            for (; csPos < changeSoil.Count; csPos++)
            {
                if (modCells > 15 || csPos - orig >= 300)
                {
                    csPos--;
                    break;
                }


                // Get indices
                IntVec3 loc = changeSoil[csPos];
                int cellIdx = map.cellIndices.CellToIndex(loc);


                // Disallow tiles that have been modified
                var tileTop = map.terrainGrid.topGrid[cellIdx];
                var tileBot = map.terrainGrid.UnderTerrainAt(cellIdx);
                if (tileBot != null)
                {
                    continue;
                }

                // Check correct tile type
                if (!WaterGrid.canBeWet.Contains(tileTop.defName))
                {
                    continue;
                }


                // Check for building
                Building building = map.edificeGrid[cellIdx];
                if (building != null && building.def.category == ThingCategory.Building)
                {
                    continue;
                }

                
                // Check roof
                if (map.roofGrid.Roofed(loc))
                {
                    continue;
                }

                map.terrainGrid.SetTerrain(loc, WetSoilDef);
                modCells++;
            }
        }

        private void MakeSoilDry()
        {
            int modCells = 0;
            int orig = csPos;
            for (; csPos < changeSoil.Count; csPos++)
            {
                if (modCells > 15 || csPos - orig >= 300)
                {
                    csPos--;
                    break;
                }


                // Get indices
                IntVec3 loc = changeSoil[csPos];
                int cellIdx = map.cellIndices.CellToIndex(loc);
                var oldTerrain = waterGrid.GetOldTerra().GetValueSafe(loc);
                if (map.terrainGrid.topGrid[cellIdx] == WetSoilDef)
                {
                    map.terrainGrid.SetTerrain(loc, oldTerrain);
                } else if (map.terrainGrid.UnderTerrainAt(cellIdx) == WetSoilDef)
                {
                    map.terrainGrid.SetUnderTerrain(loc, oldTerrain);
                }
                modCells++;
            }
        }

    }
}



