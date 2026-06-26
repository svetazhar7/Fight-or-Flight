#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    [System.Serializable]
    public struct TileMeshDesc
    {
        [SerializeField]
        private float m_size;
        public float size
        {
            get
            {
                return m_size;
            }
            set
            {
                m_size = Mathf.Max(1, value);
            }
        }

        [SerializeField]
        private int m_resolution;
        public int resolution
        {
            get
            {
                return m_resolution;
            }
            set
            {
                m_resolution = Mathf.Clamp(value, 2, 100);
                if (m_resolution % 2 == 1)
                {
                    m_resolution -= 1;
                }
            }
        }

        [SerializeField]
        private bool m_needNormals;
        public bool needNormals
        {
            get
            {
                return m_needNormals;
            }
            set
            {
                m_needNormals = value;
            }
        }

        [SerializeField]
        private bool m_needTangents;
        public bool needTangents
        {
            get
            {
                return m_needTangents;
            }
            set
            {
                m_needTangents = value;
            }
        }
    }
}
#endif