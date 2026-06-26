#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    [System.Serializable]
    public class ShaderDesc
    {
        [System.Flags]
        public enum WaterFeature
        {
            MeshNoise = 1 << 1,
            Ripples = 1 << 2,
            SineWave = 1 << 3,
            WaveMask = 1 << 4,

            NormalMaps = 1 << 5,
            LightAbsorption = 1 << 6,
            Fresnel = 1 << 7,
            Foam = 1 << 8,
            FoamHQ = 1 << 9,
            FoamCrest = 1 << 10,
            FoamSlope = 1 << 11,
            Reflection = 1 << 12,
            Refraction = 1 << 13,
            Caustic = 1 << 14
        }

        public enum VisualStyle
        {
            LowPoly,
            Stylized
        }

        public enum MeshType
        {
            Flat, River
        }

        public enum Priority
        {
            VisualQuality, Balanced, Performance
        }

        [SerializeField]
        protected string m_name;
        public string name => m_name;

        public void SetNameFromFeatures(string prefix)
        {
            string code =
                prefix + "_" +
                (visualStyle == VisualStyle.LowPoly ? "LP" : "S") + "_" +
                (meshType == MeshType.Flat ? "F" : "R") + "_" +
                (priority == Priority.VisualQuality ? "V" : priority == Priority.Balanced ? "B" : "P");
            m_name = code;
        }

        [SerializeField]
        protected Shader m_shader;
        public Shader shader => m_shader;

        [SerializeField]
        protected VisualStyle m_visualStyle;
        public VisualStyle visualStyle => m_visualStyle;

        [SerializeField]
        protected MeshType m_meshType;
        public MeshType meshType => m_meshType;

        [SerializeField]
        protected Priority m_priority;
        public Priority priority => m_priority;

        [SerializeField]
        protected WaterFeature m_features;
        public WaterFeature features => m_features;

        [SerializeField]
        protected string m_specialFeatures;
        public string specialFeatures => m_specialFeatures;
    }
}

#endif