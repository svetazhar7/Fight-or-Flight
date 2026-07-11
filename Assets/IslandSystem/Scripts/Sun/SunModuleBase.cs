using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Snapshot of the sun state for one apply pass, computed once by <see cref="SunSystem"/> and handed to every
    /// module. Modules never talk to each other — they read this context and write their own outputs, which is what
    /// keeps the disc / halo / rays / shadows / environment blocks independently toggleable.
    /// </summary>
    public struct SunContext
    {
        public Light light;
        public Material skyboxMaterial;
        public Vector3 toSun;            // world-space direction TO the sun (normalized)
        public float elevationDeg;       // sun elevation above the horizon in degrees (negative = below)
        public float elevation01;        // 0 at the horizon, 1 at 60° and above
        public float horizonFactor;      // 1 right at the horizon, 0 once the sun is 25°+ up — drives all sunset boosts
        public Color sunColor;           // final graded sun colour (temperature + elevation gradient applied)
        public float sunIntensity;       // final light intensity after the elevation curve
        public float rayVisibility;      // 0..1 from SunSystem's elevation curve — god-ray master fade
        public float haloBoost;          // 0..1 halo/atmosphere boost from SunSystem's elevation curve
        public float atmosphericDensity; // global thickness dial, scales fog/haze/scatter responses
    }

    /// <summary>
    /// Base class for every sun module. [ExecuteAlways] children of the SunSystem object; each one owns a single
    /// visual concern. Disable a module component to switch that concern off — nothing else changes.
    /// </summary>
    [ExecuteAlways]
    public abstract class SunModuleBase : MonoBehaviour
    {
        /// <summary>Apply order (lower runs first). Environment must run before Shadow's ambient lift.</summary>
        public virtual int Order => 0;

        public abstract void Apply(in SunContext ctx);

        protected void RequestApply()
        {
            var system = GetComponentInParent<SunSystem>();
            if (system != null && system.isActiveAndEnabled) system.ApplyAll();
        }

        protected virtual void OnValidate() { if (isActiveAndEnabled) RequestApply(); }
        protected virtual void OnEnable() { RequestApply(); }
        protected virtual void OnDisable() { RequestApply(); }
    }

    /// <summary>
    /// Base for interchangeable light-ray implementations (screen-space god rays today; volumetric fog, atmospheric
    /// scattering, procedural shafts etc. can be added as further subclasses without touching the rest).
    /// </summary>
    public abstract class SunRayModeBase : SunModuleBase { }

    public static class SunColorUtil
    {
        /// <summary>Approximate blackbody colour for a Kelvin temperature (1500..20000), normalized so max channel = 1.</summary>
        public static Color KelvinToColor(float kelvin)
        {
            float t = Mathf.Clamp(kelvin, 1500f, 20000f) / 100f;
            float r, g, b;
            if (t <= 66f)
            {
                r = 1f;
                g = Mathf.Clamp01((99.4708025861f * Mathf.Log(t) - 161.1195681661f) / 255f);
                b = t <= 19f ? 0f : Mathf.Clamp01((138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f) / 255f);
            }
            else
            {
                r = Mathf.Clamp01(329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f) / 255f);
                g = Mathf.Clamp01(288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f) / 255f);
                b = 1f;
            }
            float m = Mathf.Max(r, Mathf.Max(g, b));
            return m > 0f ? new Color(r / m, g / m, b / m, 1f) : Color.white;
        }

        public static Color AdjustSaturation(Color c, float saturation)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * saturation), v);
        }

        /// <summary>Warm/cool shift: t in -1..1, positive = warmer. Cheap artist dial, not physical.</summary>
        public static Color ShiftTemperature(Color c, float t)
        {
            if (Mathf.Approximately(t, 0f)) return c;
            Color warm = new Color(1f, 0.82f, 0.62f), cool = new Color(0.72f, 0.85f, 1f);
            Color target = t > 0f ? warm : cool;
            return Color.Lerp(c, c * target, Mathf.Abs(t));
        }
    }
}
