using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Flooding
{
    // Record water level
    public class WaterGrid : MapComponent
    {
        // Depth grid
        public float[] depthGrid;

        // Water sources (note: can be recalculated on request, so not a huge issue)
        public HashSet<IntVec3> waterSources = null;

        // Terrain for water resources
        private static string[] validWaterSources = { "WaterDeep", "WaterOceanDeep", "WaterMovingChestDeep", "WaterShallow", "WaterOceanShallow", "WaterMovingShallow" };

        // Special terrain allowed water flow
        private static string[] specialAllow = { "Marsh" };
        // private static string[] specialForbid = { };


        public WaterGrid(Map map) : base(map)
        {
            depthGrid = new float[map.cellIndices.NumGridCells];
        }

        public HashSet<IntVec3> FindWaterSources()
        {
            waterSources = new HashSet<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.Roofed(map))
                {
                    continue;
                }

                var freeNeighbors = GenAdjFast.AdjacentCellsCardinal(cell).Where((nc) => this.CanSetDepth(nc, 0.9f));
                if (freeNeighbors.Count() == 0)
                {
                    continue;
                }

                TerrainDef type = map.terrainGrid.TerrainAt(cell);
                if (type == null)
                {
                    continue;
                }

                foreach (string str in validWaterSources)
                {
                    if (str.Equals(type.defName))
                    {
                        waterSources.UnionWith(freeNeighbors);
                        break;
                    }
                }
            }
            return waterSources;
        }

        public bool SetDepth(IntVec3 cell, float amt)
        {
            if (!CanSetDepth(cell, amt))
            {
                return false;
            }
            int cellIndex = map.cellIndices.CellToIndex(cell);
            depthGrid[cellIndex] = Mathf.Clamp(amt, 0f, 1f);

            // Maybe think about optimizing this;
            this.MakeMeshDirty(cell);
            if (amt >= 0.5)
            {
                this.InudateEffect(cell, amt);
            }

            return true;
        }

        static DamageInfo defaultDamage = new DamageInfo(DamageDefOf.Deterioration, 35f);
        // static DamageInfo plantDamage = new DamageInfo(DamageDefOf.Rotting, 30f);
        static DamageInfo plantDamageBig = new DamageInfo(DamageDefOf.Rotting, 50f);
        private void InudateEffect(IntVec3 cell, float amt)
        {
            Thing thing = map.thingGrid.ThingAt(cell, ThingCategory.Plant);
            if (thing != null)
            {
                Plant plant = (Plant)thing;
                if (amt == 1 && DFlooding.settings.PlantEffects)
                {
                    if (plant.Growth < 0.3 && plant.MaxHitPoints < 100 && !plant.def.defName.ToLower().Contains("tree"))
                    {
                        plant.TakeDamage(plantDamageBig);
                    }
                    else if (plant.sown)
                    {
                        plant.Growth += 0.25f;
                    }
                }
                else if (DFlooding.settings.PlantEffects)
                {
                    plant.Growth += 0.05f;
                }

            }

            if (amt < 1)
            {
                return;
            }

            thing = map.thingGrid.ThingAt(cell, ThingCategory.Item);
            // why tf do chunks have this flag as true?
            if (thing != null && thing.def.deteriorateFromEnvironmentalEffects && thing.GetStatValue(StatDefOf.DeteriorationRate) > 0)
            {
                thing.TakeDamage(defaultDamage);
                return;
            }

            thing = map.thingGrid.ThingAt(cell, ThingCategory.Filth);
            if (thing != null)
            {
                thing.Destroy();
            }

            thing = map.thingGrid.ThingAt(cell, ThingCategory.Attachment);
            if (thing != null && thing.def.defName.Equals("Fire"))
            {
                thing.Destroy();
            }
        }

        public float GetDepth(IntVec3 cell, float OutOfBounds = -1)
        {
            if (!cell.InBounds(map))
            {
                return OutOfBounds;
            }
            int cellIndex = map.cellIndices.CellToIndex(cell);
            return depthGrid[cellIndex];
        }

        public bool CanSetDepth(IntVec3 cell, float desired)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            int cellIndex = map.cellIndices.CellToIndex(cell);

            if (depthGrid[cellIndex] == desired)
            {
                return false;
            }

            if (desired != 0 && !CanCoexistWithWater(cellIndex))
            {
                return false;
            }

            return true;
        }

        public void ClearWater(IntVec3 cell)
        {
            int cellIndex = map.cellIndices.CellToIndex(cell);
            depthGrid[cellIndex] = 0;
            this.MakeMeshDirty(cell);
        }

        public void ForceClear()
        {
            foreach(IntVec3 cell in map.AllCells)
            {
                ClearWater(cell);
                if (map.terrainGrid.TerrainAt(cell).Equals(FloodHandler.WetSoilDef))
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                }
            }
        }

        // Can set depth, but has a maximum parameter instead of desired
        // return false if current depth at max
        public bool CanSetDepthMaximum(IntVec3 cell, float max)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            int cellIndex = map.cellIndices.CellToIndex(cell);

            if (depthGrid[cellIndex] > max)
            {
                return false;
            }

            if (!CanCoexistWithWater(cellIndex))
            {
                return false;
            }

            return true;
        }


        public bool CanCoexistWithWater(int cellIndex)
        {
            Building building = map.edificeGrid[cellIndex];
            if (building != null && !SnowGrid.CanCoexistWithSnow(building.def))
            {
                return false;
            }

            if (building != null && (
                building.def == ThingDefOf.Sandbags || 
                building.def == ThingDefOf.Barricade))
            {
                return false;
            }


            TerrainDef terrainDef = map.terrainGrid.TerrainAt(cellIndex);
            if (terrainDef == null)
            {
                return false;
            }

            if (!terrainDef.holdSnow)
            {
                foreach (string str in specialAllow)
                {
                    if (terrainDef.defName.Equals(str))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (DFlooding.settings.DisableManMadeTileFlood && terrainDef.IsFloor)
            {
                return false;
            }

            if (DFlooding.settings.DisableBridgeTileFlood && terrainDef.defName.ToLower().Contains("bridge"))
            {
                return false;
            }

            return true;
        }


        // Get depth text for UI
        public SnowCategory GetDepthAsCategory(IntVec3 cell)
        {
            return SnowUtility.GetSnowCategory(GetDepth(cell));
        }

        // Update graphic
        public void MakeMeshDirty(IntVec3 c)
        {
            // map.mapDrawer.MapMeshDirty(c, MapMeshFlag.Terrain, regenAdjacentCells: true, regenAdjacentSections: false);
            map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Snow, regenAdjacentCells: false, regenAdjacentSections: false);

            if (Rand.Chance(0.5f))
            {
                map.pathing.RecalculatePerceivedPathCostAt(c);
            }
            // map.mapDrawer.MapMeshDirty(c, MapMeshFlag.Things, regenAdjacentCells: true, regenAdjacentSections: false);
        }


        // ExposeData code from sandstorms
        private static ushort SandFloatToShort(float depth)
        {
            depth = Mathf.Clamp(depth, 0f, 1f);
            depth *= 65535f;
            return (ushort)Mathf.RoundToInt(depth);
        }
        private static float SandShortToFloat(ushort depth)
        {
            return (float)depth / 65535f;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            MapExposeUtility.ExposeUshort(this.map, (IntVec3 c) => SandFloatToShort(this.GetDepth(c)), delegate (IntVec3 c, ushort val)
            {
                this.depthGrid[this.map.cellIndices.CellToIndex(c)] = SandShortToFloat(val);
            }, "depthGrid");
            Scribe_Collections.Look(ref oldTerra, "oldTerra", LookMode.Value);
        }


        // For wet soil
        Dictionary<IntVec3, TerrainDef> oldTerra = new Dictionary<IntVec3, TerrainDef>();
        public static string[] canBeWet = { "Soil", "SoilRich", "Gravel", "MossyTerrain" };

        public Dictionary<IntVec3, TerrainDef> GetOldTerra()
        {
            if (oldTerra.Count() == 0)
            {
                foreach (IntVec3 cell in map.AllCells)
                {
                    int cellIdx = map.cellIndices.CellToIndex(cell);
                    var tileTop = map.terrainGrid.topGrid[cellIdx];
                    var tileBot = map.terrainGrid.UnderTerrainAt(cellIdx);
                    if (tileBot != null && canBeWet.Contains(tileBot.defName))
                    {
                        oldTerra.Add(cell, tileBot);
                    } else if (canBeWet.Contains(tileTop.defName))
                    {
                        oldTerra.Add(cell, tileTop);
                    }
                }
            }
            return oldTerra;
        }
    }


}
