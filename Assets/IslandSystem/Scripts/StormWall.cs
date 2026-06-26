using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Builds a ring of dark storm clouds around the playable area, just beyond where the ocean ends.
    /// The radius adapts to the current map size: it reads the generated "Ocean" plane extent (or is
    /// told the half-extent directly by <see cref="ArchipelagoGenerator"/>) and places the wall at
    /// ocean-edge + <see cref="margin"/>. Rebuilt automatically at the end of every archipelago Generate,
    /// or manually via the context menu / inspector.
    ///
    /// Independent of the COZY/Plume cloud system on purpose: Plume's shader colours all its clouds from
    /// one global colour, so it can't be locally darkened. This wall uses its own dark unlit material and
    /// stays reliably stormy regardless of weather/time of day.
    /// </summary>
    [ExecuteAlways]
    public class StormWall : MonoBehaviour
    {
        [Header("Sizing (adapts to the map)")]
        [Tooltip("How far beyond the ocean edge the storm wall sits.")]
        public float margin = 300f;
        [Tooltip("Fallback radius used only if no Ocean can be found and no extent is supplied.")]
        public float fallbackRadius = 4100f;
        [Tooltip("Radius at which the puff sizes below look right. Puffs scale with radius/this so the " +
                 "wall stays equally dense on small and large maps.")]
        public float referenceRadius = 4100f;

        [Header("Wall shape")]
        [Min(8)] public int segments = 30;
        [Tooltip("Height of the cloud wall (sea level up to roughly this many units).")]
        public float wallHeight = 1500f;
        [Tooltip("Random height variation between segments.")]
        public float heightVariation = 300f;
        [Tooltip("Random radius wobble between segments, so the edge isn't a perfect circle.")]
        public float radiusJitter = 180f;
        [Tooltip("Tangential overlap factor (1 = segments just touch, >1 = overlap into a continuous wall).")]
        public float widthOverlap = 1.25f;
        [Tooltip("Radial thickness of the wall.")]
        public float depth = 650f;

        [Tooltip("Altitude of the bottom of the cloud band. Kept above the water so clouds float in the sky " +
                 "instead of erupting from the sea (water surface is around y=4).")]
        public float baseAltitude = 600f;

        [Header("Clouds")]
        public Material material;
        [Min(10)] public int particlesPerSegment = 260;
        public float minPuffSize = 500f;
        public float maxPuffSize = 900f;
        [Tooltip("Particle lifetime (seconds). Long = a stable wall that only slowly churns.")]
        public float puffLifetime = 180f;

        const string SegPrefix = "StormSeg_";
        const string DefaultMaterialPath = "Assets/StormWall/StormCloud.mat";

        /// <summary>Rebuild using the auto-detected ocean extent (or fallback). Safe to call from the editor.</summary>
        [ContextMenu("Rebuild Storm Wall")]
        public void Rebuild()
        {
            Rebuild(FindOceanHalfExtent());
        }

        /// <summary>Rebuild the wall at <paramref name="oceanHalfExtent"/> + margin. Called by the generator.</summary>
        public void Rebuild(float oceanHalfExtent)
        {
            float radius = (oceanHalfExtent > 1f ? oceanHalfExtent : fallbackRadius) + margin;

            Clear();
            ResolveMaterial();

            // Keep puffs a fixed (moderate) size and instead scale their COUNT with map size, so the wall
            // stays equally dense on big maps without giant puffs that would dip below the water.
            float densityScale = Mathf.Clamp(radius / Mathf.Max(1f, referenceRadius), 1f, 2f);
            int segCount = Mathf.RoundToInt(particlesPerSegment * densityScale);

            for (int i = 0; i < segments; i++)
            {
                float ang = Mathf.PI * 2f * i / segments;
                float rr = radius + Mathf.Sin(i * 1.7f) * radiusJitter;
                float h = wallHeight + Mathf.Sin(i * 0.9f) * heightVariation;
                // Box sits with its bottom at baseAltitude so clouds float above the sea, not out of it.
                Vector3 pos = new Vector3(Mathf.Cos(ang) * rr, baseAltitude + h * 0.5f, Mathf.Sin(ang) * rr);

                var go = new GameObject(SegPrefix + i);
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
                go.transform.LookAt(new Vector3(0f, pos.y, 0f)); // local +Z faces centre, X tangential

                float segArc = (Mathf.PI * 2f * rr) / segments;
                BuildEmitter(go, new Vector3(segArc * widthOverlap, h, depth), segCount);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif
        }

        [ContextMenu("Clear Storm Wall")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
        }

        void BuildEmitter(GameObject go, Vector3 boxScale, int maxParticles)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = puffLifetime;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize = new ParticleSystem.MinMaxCurve(minPuffSize, maxPuffSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.88f, 0.95f, 0.95f), Color.white);
            main.maxParticles = maxParticles;

            var em = ps.emission;
            // steady-state count = rate * lifetime; keep it >= cap so the wall stays full
            em.rateOverTime = Mathf.Max(1f, (maxParticles * 1.2f) / Mathf.Max(1f, puffLifetime));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxScale;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.alignment = ParticleSystemRenderSpace.View;
            rend.sortMode = ParticleSystemSortMode.Distance;
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                ps.Simulate(puffLifetime, true, true, false); // fill so the wall is visible in the editor
#endif
        }

        /// <summary>Finds the generated ocean plane and returns its half-extent in world units; 0 if none.</summary>
        public float FindOceanHalfExtent()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.gameObject.name == "Ocean")
                    return Mathf.Max(r.bounds.extents.x, r.bounds.extents.z);
            return 0f;
        }

        void ResolveMaterial()
        {
            if (material != null) return;
#if UNITY_EDITOR
            material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
#endif
            if (material == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
                material = new Material(sh) { name = "StormCloud (runtime)" };
                material.SetColor("_BaseColor", new Color(0.24f, 0.26f, 0.32f, 1f));
            }
        }
    }
}
