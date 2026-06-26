#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Poseidon;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class AreaWater : PlanarWaterBody
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
        protected AreaMeshDesc m_meshDesc;
        public AreaMeshDesc meshDesc
        {
            get
            {
                return m_meshDesc;
            }
            set
            {
                m_meshDesc = value;
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

        [SerializeField]
        protected MeshFilter m_meshFilter;
        public MeshFilter meshFilter
        {
            get
            {
                if (m_meshFilter == null)
                {
                    m_meshFilter = GetComponent<MeshFilter>();
                }
                return m_meshFilter;
            }
        }

        [SerializeField]
        protected MeshRenderer m_meshRenderer;
        public MeshRenderer meshRenderer
        {
            get
            {
                if (m_meshRenderer == null)
                {
                    m_meshRenderer = GetComponent<MeshRenderer>();
                }
                return m_meshRenderer;
            }
        }

        [SerializeField]
        protected List<Vector3> m_anchors = new List<Vector3>();
        public List<Vector3> anchors
        {
            get
            {
                return m_anchors;
            }
        }

        protected override void Reset()
        {
            base.Reset();

            m_material = null;
            m_meshDesc = new AreaMeshDesc()
            {
                resolution = 20,
                needNormals = false,
                needTangents = false
            };
            m_anchors = new List<Vector3>()
            {
                new Vector3(Mathf.Cos(0*Mathf.Deg2Rad)  , 0, Mathf.Sin(0*Mathf.Deg2Rad))*10f,
                new Vector3(Mathf.Cos(60*Mathf.Deg2Rad) , 0, Mathf.Sin(60*Mathf.Deg2Rad))*10f,
                new Vector3(Mathf.Cos(120*Mathf.Deg2Rad), 0, Mathf.Sin(120*Mathf.Deg2Rad))*10f,
                new Vector3(Mathf.Cos(180*Mathf.Deg2Rad), 0, Mathf.Sin(180*Mathf.Deg2Rad))*10f,
                new Vector3(Mathf.Cos(240*Mathf.Deg2Rad), 0, Mathf.Sin(240*Mathf.Deg2Rad))*10f,
                new Vector3(Mathf.Cos(300*Mathf.Deg2Rad), 0, Mathf.Sin(300*Mathf.Deg2Rad))*10f,
            };

            GenerateMesh();
        }

        protected override void Update()
        {
            if (m_material != null)
            {
                float time = GetTimeParam();
                float sineTime = Mathf.Sin(time);
                m_material.SetFloat(PMat.TIME, time);
                m_material.SetFloat(PMat.SINE_TIME, sineTime);
            }

            meshFilter.sharedMesh = m_sharedMesh;
            meshRenderer.sharedMaterial = m_material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void GenerateMesh()
        {
            if (m_anchors == null || m_anchors.Count < 3)
            {
                Debug.LogWarning("Area water mesh anchors must have at least 3 points. Skip creating mesh.");
                return;
            }

            if (m_sharedMesh == null)
            {
                m_sharedMesh = new Mesh();
            }
            AreaMeshGenerator meshGen = new AreaMeshGenerator();
            meshGen.Overwrite(m_sharedMesh, m_meshDesc, m_anchors);
            m_sharedMesh.name = $"~{gameObject.name}_{meshPattern}_{m_meshDesc.resolution}";
            m_sharedMesh.Optimize();
        }

        public override void GetRenderers(List<MeshRenderer> container)
        {
            container.Clear();
            if (meshRenderer != null)
            {
                container.Add(meshRenderer);
            }
        }
    }
}

#endif