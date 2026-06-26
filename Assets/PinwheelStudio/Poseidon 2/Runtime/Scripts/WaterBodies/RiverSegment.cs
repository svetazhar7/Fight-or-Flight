#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RiverSegment : MonoBehaviour
    {
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

        private void OnDestroy()
        {
            if (meshFilter.sharedMesh != null)
            {
                Object.DestroyImmediate(meshFilter.sharedMesh);
            }
        }
    }
}

#endif