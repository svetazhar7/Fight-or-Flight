#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System;
using System.Linq;

namespace Pinwheel.Poseidon
{
    [ExecuteInEditMode]
    public class TileableWater : PlanarWaterBody
    {
        [SerializeField]
        protected PlaneMeshPattern m_meshPattern;
        public PlaneMeshPattern meshPattern
        {
            get
            {
                return m_meshPattern;
            }
            set
            {
                m_meshPattern = value;
            }
        }

        [SerializeField]
        protected TileMeshDesc m_tileMeshDesc;
        public TileMeshDesc tileMeshDesc
        {
            get
            {
                return m_tileMeshDesc;
            }
            set
            {
                m_tileMeshDesc = value;
            }
        }

        [SerializeField]
        protected Mesh m_sharedMesh;
        public Mesh sharedMesh
        {
            get
            {
                return m_sharedMesh;
            }
        }

        public const string TILES_ROOT_NAME = "~WaterTiles";
        [SerializeField]
        protected List<WaterTile> m_tiles = new List<WaterTile>();
        public IEnumerable<WaterTile> tiles => m_tiles;

        protected override void Reset()
        {
            base.Reset();

            m_material = null;
            m_tileMeshDesc = new TileMeshDesc()
            {
                size = 50,
                resolution = 100,
                needNormals = false,
                needTangents = false
            };

            GetOrAddTile(0, 0);
            GenerateMesh();
        }

        public Transform GetOrAddTilesRoot()
        {
            Transform root = transform.Find(TILES_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(TILES_ROOT_NAME).transform;
                root.SetParent(transform);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;

                root.gameObject.layer = LayerMask.NameToLayer("Water");
            }
            return root;
        }

        public WaterTile GetOrAddTile(int indexX, int indexY)
        {
            WaterTile tile = GetTile(indexX, indexY);
            if (tile == null)
            {
                Transform tileRoot = GetOrAddTilesRoot();
                GameObject tileGO = new GameObject($"~Tile ({indexX},{indexY})");
                tileGO.transform.parent = tileRoot;
                tileGO.transform.localPosition = Vector2.zero;
                tileGO.transform.localRotation = Quaternion.identity;
                tileGO.transform.localScale = Vector2.one;
                tileGO.layer = LayerMask.NameToLayer("Water");

                tileGO.AddComponent<MeshFilter>();
                tileGO.AddComponent<MeshRenderer>();
                tile = tileGO.AddComponent<WaterTile>();
                tile.index = new Vector2Int(indexX, indexY);

                m_tiles.Add(tile);
                NotifyShapeChanged();
            }

            return tile;
        }

        public bool DestroyTile(int indexX, int indexY)
        {
            WaterTile tile = GetTile(indexX, indexY);
            if (tile != null)
            {
                m_tiles.Remove(tile);
                DestroyImmediate(tile.gameObject);
                NotifyShapeChanged();
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool HasTile(int indexX, int indexY)
        {
            return m_tiles.Exists(t => t.index.x == indexX && t.index.y == indexY);
        }

        public WaterTile GetTile(int indexX, int indexY)
        {
            return m_tiles.Find(t => t.index.x == indexX && t.index.y == indexY);
        }

        public Vector2Int PointOSToTileIndex(Vector3 pointOS)
        {
            return new Vector2Int(Mathf.FloorToInt(pointOS.x / m_tileMeshDesc.size), Mathf.FloorToInt(pointOS.z / m_tileMeshDesc.size));
        }

        public Vector3 TileIndexToTileOriginOS(Vector2Int tileIndex)
        {
            return new Vector3(tileIndex.x * m_tileMeshDesc.size, 0, tileIndex.y * m_tileMeshDesc.size);
        }

        protected override void Update()
        {
            m_tiles.ForEach(t =>
            {
                if (t == null)
                    return;
                t.transform.localPosition = CalculateTilePositionOS(tileMeshDesc.size, t.index);
                t.transform.localRotation = Quaternion.identity;
                t.transform.localScale = new Vector3(tileMeshDesc.size, 1, tileMeshDesc.size);

                t.meshFilter.sharedMesh = m_sharedMesh;
                t.meshRenderer.sharedMaterial = m_material;
                t.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            });

            if (m_material != null)
            {
                float time = GetTimeParam();
                float sineTime = Mathf.Sin(time);
                m_material.SetFloat(PMat.TIME, time);
                m_material.SetFloat(PMat.SINE_TIME, sineTime);
            }
        }

        public void GenerateMesh()
        {
            ITileMeshGenerator meshGenerator = null;
            if (meshPattern == PlaneMeshPattern.Hexagon)
            {
                meshGenerator = new HexMeshGenerator();
            }
            else if (meshPattern == PlaneMeshPattern.Diamond)
            {
                meshGenerator = new DiamondMeshGenerator();
            }
            else if (meshPattern == PlaneMeshPattern.Quad)
            {
                meshGenerator = new QuadMeshGenerator();
            }
            else
            {
                meshGenerator = new HexMeshGenerator();
            }

            if (m_sharedMesh == null)
            {
                m_sharedMesh = new Mesh();
            }
            meshGenerator.Overwrite(m_sharedMesh, m_tileMeshDesc);
            m_sharedMesh.name = $"~{gameObject.name}_{meshPattern}_{m_tileMeshDesc.resolution}";
            m_sharedMesh.Optimize();
        }

        public static Vector3 CalculateTilePositionOS(float tileSize, Vector2Int tileIndex)
        {
            return new Vector3(tileSize * tileIndex.x, 0, tileSize * tileIndex.y);
        }

        public override void GetRenderers(List<MeshRenderer> container)
        {
            container.Clear();
            foreach (WaterTile t in m_tiles)
            {
                if (t.meshRenderer != null)
                {
                    container.Add(t.meshRenderer);
                }
            }
        }
    }
}

#endif