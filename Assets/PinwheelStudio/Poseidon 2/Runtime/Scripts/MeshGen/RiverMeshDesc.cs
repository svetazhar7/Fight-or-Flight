#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    [System.Serializable]
    public struct RiverMeshDesc
    {
        [SerializeField]
        private float m_width;
        public float width
        {
            get
            {
                return m_width;
            }
            set
            {
                m_width = Mathf.Max(1, value);
            }
        }

        [SerializeField]
        private float m_vertexDistance;
        public float vertexDistance
        {
            get
            {
                return m_vertexDistance;
            }
            set
            {
                m_vertexDistance = Mathf.Max(0.2f, value);
            }
        }

        [SerializeField]
        private float m_segmentLength;
        public float segmentLength
        {
            get
            {
                return m_segmentLength;
            }
            set
            {
                m_segmentLength = Mathf.Max(10, value);
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