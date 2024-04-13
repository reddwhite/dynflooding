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
    // Rendering of floodwater

    [StaticConstructorOnStartup]
    public class SectionLayer_Flood : SectionLayer
    {
        private static readonly Texture2D waterTexture;
        private static readonly Material waterMaterial;
        private float[] adjValuesTmp = new float[9];
        private static readonly List<float> opacityListTmp = new List<float>();
        public static readonly List<List<int>> vertexWeights = new List<List<int>> {
        new List<int> { 0, 1, 2, 8 },
        new List<int> { 2, 8 },
        new List<int> { 2, 3, 4, 8 },
        new List<int> { 4, 8 },
        new List<int> { 4, 5, 6, 8 },
        new List<int> { 6, 8 },
        new List<int> { 6, 7, 0, 8 },
        new List<int> { 0, 8 },
        new List<int> { 8 } };

        static SectionLayer_Flood()
        {
            waterTexture = ContentFinder<Texture2D>.Get("swablu/Water");
            waterMaterial = new Material(MatBases.Snow);

            waterMaterial.SetTexture("_MainTex", waterTexture);
            waterMaterial.SetTexture("_MacroTex", waterTexture);
            waterMaterial.SetTexture("_AlphaAddTex ", waterTexture);
            waterMaterial.SetTexture("_PollutedTex", waterTexture);
        }

        public SectionLayer_Flood(Section section) : base(section)
        {
            base.relevantChangeTypes = MapMeshFlagDefOf.Snow;
        }


        // Modified decompiled snow regeneration code
        // Maybe optimize this later
        public override void Regenerate()
        {
            LayerSubMesh subMesh = GetSubMesh(waterMaterial);
            WaterGrid waterGrid = base.Map.GetComponent<WaterGrid>();
            if (subMesh.mesh.vertexCount == 0)
            {
                SectionLayerGeometryMaker_Solid.MakeBaseGeometry(section, subMesh, AltitudeLayer.Terrain);
            }
            subMesh.Clear(MeshParts.Colors);
            float[] depthGridDirect_Unsafe = waterGrid.depthGrid;
            CellRect cellRect = section.CellRect;
            bool flag = false;
            CellIndices cellIndices = base.Map.cellIndices;
            for (int i = cellRect.minX; i <= cellRect.maxX; i++)
            {
                for (int j = cellRect.minZ; j <= cellRect.maxZ; j++)
                {
                    opacityListTmp.Clear();
                    float num = depthGridDirect_Unsafe[cellIndices.CellToIndex(i, j)];
                    for (int k = 0; k < 9; k++)
                    {
                        IntVec3 c = new IntVec3(i, 0, j) + GenAdj.AdjacentCellsAndInsideForUV[k];
                        adjValuesTmp[k] = (c.InBounds(base.Map) ? depthGridDirect_Unsafe[cellIndices.CellToIndex(c)] : num);
                    }
                    for (int l = 0; l < 9; l++)
                    {
                        float num2 = 0f;
                        for (int m = 0; m < vertexWeights[l].Count; m++)
                        {
                            num2 += adjValuesTmp[vertexWeights[l][m]];
                        }
                        num2 /= (float)vertexWeights[l].Count;
                        if (num2 > 0.01f)
                        {
                            flag = true;
                        }
                        opacityListTmp.Add(num2);
                    }

                    for (int num3 = 0; num3 < 9; num3++)
                    {
                        float num6 = opacityListTmp[num3];
                        subMesh.colors.Add(new Color32(0, byte.MaxValue, byte.MaxValue, Convert.ToByte(Mathf.Clamp(num6 * 255f, 0, 180))));
                    }
                }
            }
            if (flag)
            {
                subMesh.disabled = false;
                subMesh.FinalizeMesh(MeshParts.Colors);
            }
            else
            {
                subMesh.disabled = true;
            }
        }
    }
}
