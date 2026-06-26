#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Poseidon;

namespace Pinwheel.Poseidon
{
    public class QuadMeshGenerator : ITileMeshGenerator
    {
        private Vector2[,] grid;

        public void Overwrite(Mesh mesh, TileMeshDesc desc)
        {
            Init(desc.resolution);
            GenerateGrid();
            UpdateMesh(mesh, desc);
        }

        private void Init(int meshResolution)
        {
            int length = meshResolution + 1;
            grid = new Vector2[length, length];
        }

        private void GenerateGrid()
        {
            int length = grid.GetLength(0);
            int width = grid.GetLength(1);

            Vector2 p = Vector2.zero;
            for (int z = 0; z < length; ++z)
            {
                for (int x = 0; x < width; ++x)
                {
                    p.Set(
                        Mathf.InverseLerp(0, width - 1, x),
                        Mathf.InverseLerp(0, length - 1, z));
                    grid[z, x] = p;
                }
            }
        }

        private void UpdateMesh(Mesh mesh, TileMeshDesc meshDesc)
        {
            //vertices
            int length = grid.GetLength(0);
            int width = grid.GetLength(1);
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector4> uvs0 = new List<Vector4>(); //contain neighbor vertex position, for normal re-construction
            List<Color> colors = new List<Color>(); //contain neighbor vertex position, for normal re-construction

            Vector4 bl = Vector4.zero;
            Vector4 tl = Vector4.zero;
            Vector4 tr = Vector4.zero;
            Vector4 br = Vector4.zero;

            for (int z = 0; z < length - 1; ++z)
            {
                for (int x = 0; x < width - 1; ++x)
                {
                    int lastIndex = vertices.Count;
                    triangles.Add(lastIndex + 0);
                    triangles.Add(lastIndex + 1);
                    triangles.Add(lastIndex + 2);
                    triangles.Add(lastIndex + 3);
                    triangles.Add(lastIndex + 4);
                    triangles.Add(lastIndex + 5);

                    bl.Set(Mathf.InverseLerp(0, width - 1, x), 0, Mathf.InverseLerp(0, length - 1, z), 0);
                    tl.Set(Mathf.InverseLerp(0, width - 1, x), 0, Mathf.InverseLerp(0, length - 1, z + 1), 0);
                    tr.Set(Mathf.InverseLerp(0, width - 1, x + 1), 0, Mathf.InverseLerp(0, length - 1, z + 1), 0);
                    br.Set(Mathf.InverseLerp(0, width - 1, x + 1), 0, Mathf.InverseLerp(0, length - 1, z), 0);

                    vertices.Add(bl); uvs0.Add(tl); colors.Add(tr);
                    vertices.Add(tl); uvs0.Add(tr); colors.Add(bl);
                    vertices.Add(tr); uvs0.Add(bl); colors.Add(tl);

                    vertices.Add(bl); uvs0.Add(tr); colors.Add(br);
                    vertices.Add(tr); uvs0.Add(br); colors.Add(bl);
                    vertices.Add(br); uvs0.Add(bl); colors.Add(tr);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs0);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.size = new Vector3(bounds.size.x, 0.01f, bounds.size.z);
            mesh.bounds = bounds;

            if (meshDesc.needNormals)
            {
                mesh.RecalculateNormals();
            }
            else
            {
                mesh.normals = null;
            }

            if (meshDesc.needTangents)
            {
                mesh.RecalculateTangents();
            }
            else
            {
                mesh.tangents = null;
            }
        }
    }
}

#endif