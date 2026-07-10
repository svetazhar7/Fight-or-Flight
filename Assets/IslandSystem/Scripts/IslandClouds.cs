using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Our own stylized cloud field — a COZY-independent replacement for Plume. Each CLOUD is a CLUSTER of many soft
    /// sphere billboards (IslandSystem/Clouds shader) heaped into a flattened blob — the puffiness comes from lots of
    /// overlapping soft spheres, not from a detailed texture. You fly through them. Every setting is on this
    /// component; it also feeds the shader the day/night sun colour + direction each frame (shared with the
    /// StormWall). Particles are placed manually (SetParticles) so we control the clustering.
    /// </summary>
    [ExecuteAlways]
    public class IslandClouds : MonoBehaviour
    {
        [Header("Layout")]
        public float cloudHeight = 950f;
        [Tooltip("Vertical scatter of cloud CENTRES around cloudHeight.")]
        public float cloudThickness = 260f;
        [Tooltip("Half-extent of the covered area (world units).")]
        public float areaRadius = 6000f;
        public int seed = 12345;

        [Header("Amount")]
        [Range(0f, 1f)]
        [Tooltip("Cloudiness → how many cloud clusters are placed (0 = clear, 1 = packed).")]
        public float coverage = 0.4f;
        [Tooltip("Number of cloud clusters at coverage = 1.")]
        [Min(1)] public int maxClouds = 55;

        [Header("Each cloud (cluster of puffs)")]
        [Tooltip("How many soft-sphere puffs make up one cloud. More = puffier/heavier.")]
        public Vector2Int puffsPerCloud = new Vector2Int(12, 26);
        [Tooltip("Horizontal radius of one cloud (world units).")]
        public float cloudSize = 480f;
        [Tooltip("Vertical squash of a cloud (cumulus are wider than tall). 1 = round, 0.3 = flat.")]
        [Range(0.15f, 1f)] public float cloudFlatten = 0.42f;
        [Tooltip("Size of each puff sphere within a cloud.")]
        public Vector2 puffSize = new Vector2(220f, 420f);
        [Range(0f, 1f)] public float opacity = 1f;

        [Header("Wind")]
        public float windSpeed = 8f;
        public float windAngle = 40f;

        [Header("Colour (day/night sun colour is applied on top)")]
        public Color litColor = new Color(1.0f, 0.98f, 0.90f, 1f);      // cream crown (cartoon)
        public Color shadowColor = new Color(0.62f, 0.74f, 0.95f, 1f);  // soft blue base (cartoon)
        public Color ambient = new Color(0.25f, 0.30f, 0.40f, 1f);
        [Range(0.2f, 4f)] public float crownContrast = 1.15f;
        [Range(0f, 1f)] public float sunWrap = 0.45f;
        [Tooltip("CARTOON shading: quantize the crown→base gradient into this many flat tone bands (1 = smooth).")]
        [Range(1, 4)] public int shadeBands = 3;
        [Tooltip("Silhouette edge softness — small = crisp drawn-style cutout, big = airy feather.")]
        [Range(0.02f, 0.4f)] public float edgeSoftness = 0.07f;

        [Header("Rendering")]
        public Material material;

        [Header("Sun occlusion (hide the sun behind clouds)")]
        [Tooltip("How strongly a cloud between the camera and the sun dims the sun disc + lens flare (0 = off).")]
        [Range(0f, 1f)] public float sunOcclusion = 0.92f;

        const string DefaultShader = "IslandSystem/Clouds";
        const string PuffTexPath = "Assets/IslandSystem/Shaders/CloudPuff.png";

        ParticleSystem _ps;
        ParticleSystemRenderer _rend;
        static Mesh _sphereMesh;
        readonly List<Vector4> _puffs = new List<Vector4>();   // world xyz + radius, for the sun-occlusion test
        float _sunBrightBase = -1f, _sunGlowBase = -1f, _flareBase = -1f;

        /// <summary>Built-in sphere mesh (grabbed once from a temp primitive).</summary>
        static Mesh SphereMesh()
        {
            if (_sphereMesh != null) return _sphereMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
            return _sphereMesh;
        }

        static readonly int ID_SunColor = Shader.PropertyToID("_CloudSunColor");
        static readonly int ID_SunDir   = Shader.PropertyToID("_CloudSunDir");
        static readonly int ID_Ambient  = Shader.PropertyToID("_CloudAmbient");

        void OnEnable() { Rebuild(); }
        void OnValidate() { if (isActiveAndEnabled && _ps != null) { ApplyMaterial(); Refill(); } }
        void Update() { DriveSunGlobals(); ApplySunOcclusion(); }

        void OnDisable()
        {
            // Restore the sun so the skybox material asset isn't left saved in a dimmed state.
            var skyMat = RenderSettings.skybox;
            if (skyMat != null && _sunBrightBase > 0f)
            {
                skyMat.SetFloat("_SunBrightness", _sunBrightBase);
                skyMat.SetFloat("_SunGlow", _sunGlowBase);
            }
            var sun = RenderSettings.sun;
            var lf = sun != null ? sun.GetComponent<UnityEngine.Rendering.LensFlareComponentSRP>() : null;
            if (lf != null && _flareBase > 0f) lf.intensity = _flareBase;
        }

        float _occSmooth;
        float _refillTime;
        Vector3 _windVec;

        /// <summary>Dims the skybox sun disc/glow + the lens flare when a cloud sits between the camera and the
        /// sun (ray-vs-puff-spheres on the CPU — the sun shouldn't blaze through a solid cloud).</summary>
        void ApplySunOcclusion()
        {
            var skyMat = RenderSettings.skybox;
            Light sun = RenderSettings.sun;
            var cam = Camera.main;                                  // scene cameras aren't always tagged MainCamera
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null || sun == null || skyMat == null || sunOcclusion <= 0.001f || _puffs.Count == 0) return;
            if (_sunBrightBase < 0f && skyMat.HasProperty("_SunBrightness"))
            {
                _sunBrightBase = skyMat.GetFloat("_SunBrightness");
                _sunGlowBase = skyMat.GetFloat("_SunGlow");
            }

            // Ray from the camera toward the sun vs every puff sphere. Puffs drift with the wind since Refill,
            // so shift the cached positions by the elapsed wind offset (velocity is uniform).
            Vector3 windOff = Application.isPlaying ? _windVec * (Time.time - _refillTime) : Vector3.zero;
            Vector3 o = cam.transform.position;
            Vector3 d = -sun.transform.forward;
            float occ = 0f;
            for (int i = 0; i < _puffs.Count; i++)
            {
                Vector3 c = new Vector3(_puffs[i].x, _puffs[i].y, _puffs[i].z) + windOff - o;
                float along = Vector3.Dot(c, d);
                if (along <= 0f) continue;
                float r = _puffs[i].w;
                float distSq = c.sqrMagnitude - along * along;
                if (distSq < r * r)
                {
                    occ += 0.5f * (1f - Mathf.Sqrt(Mathf.Max(0f, distSq)) / r);   // deeper through the puff = more cover
                    if (occ >= 1f) { occ = 1f; break; }
                }
            }
            float target = Mathf.Clamp01(occ) * sunOcclusion;
            _occSmooth = Mathf.MoveTowards(_occSmooth, target, Time.deltaTime * 2.5f);   // no popping

            if (_sunBrightBase > 0f)
            {
                skyMat.SetFloat("_SunBrightness", _sunBrightBase * (1f - _occSmooth));
                skyMat.SetFloat("_SunGlow", _sunGlowBase * (1f - _occSmooth));
            }
            var flare = sun.GetComponent<UnityEngine.Rendering.LensFlareComponentSRP>();
            if (flare != null)
            {
                if (_flareBase < 0f) _flareBase = flare.intensity;
                flare.intensity = _flareBase * (1f - _occSmooth);
            }
        }

        void DriveSunGlobals()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                float best = -1f;
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.intensity > best) { best = l.intensity; sun = l; }
            }
            // Clamp the sun factor to ~1: with a gameplay sun of intensity 2 the clouds went HDR×2 — a thin puff
            // veil then BRIGHTENS everything behind it (washed grey island, bloom speckles on the water glints).
            Color sc = sun != null ? sun.color * Mathf.Clamp(sun.intensity, 0.12f, 1.05f) : Color.white;
            Vector3 dir = sun != null ? -sun.transform.forward : Vector3.up;
            Shader.SetGlobalColor(ID_SunColor, sc);
            Shader.SetGlobalVector(ID_SunDir, new Vector4(dir.x, dir.y, dir.z, 0f));
            Shader.SetGlobalColor(ID_Ambient, ambient);
            // Wind drift since Refill → the vertex-lump noise pattern rides along with the drifting puffs.
            Vector3 wo = Application.isPlaying ? _windVec * (Time.time - _refillTime) : Vector3.zero;
            Shader.SetGlobalVector("_CloudWindOff", new Vector4(wo.x, wo.y, wo.z, 0f));
        }

        [ContextMenu("Rebuild Clouds")]
        public void Rebuild()
        {
            ResolveMaterial();
            transform.position = new Vector3(transform.position.x, cloudHeight, transform.position.z);

            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) _ps = gameObject.AddComponent<ParticleSystem>();
            _rend = GetComponent<ParticleSystemRenderer>();

            var main = _ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(16, maxClouds * Mathf.Max(1, puffsPerCloud.y));
            var em = _ps.emission; em.enabled = false;           // we place particles manually
            var sh = _ps.shape; sh.enabled = false;

            // MESH spheres, not billboards: the puffs sit still in the world (no turning with the player camera).
            _rend.renderMode = ParticleSystemRenderMode.Mesh;
            _rend.mesh = SphereMesh();
            _rend.alignment = ParticleSystemRenderSpace.World;
            _rend.sortMode = ParticleSystemSortMode.Distance;
            _rend.sharedMaterial = material;
            _rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rend.receiveShadows = false;

            ApplyMaterial();
            Refill();
        }

        void ApplyMaterial()
        {
            if (material == null) return;
#if UNITY_EDITOR
            // A texture reimport can null the reference on a runtime material → puffs render as solid QUADS
            // (tex.a defaults to 1). Re-link the puff texture whenever it goes missing.
            if (material.GetTexture("_MainTex") == null)
                material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexPath));
#endif
            // The lit/shadow gradient is BAKED into the particle colours (see Refill) so the cloud shades as one
            // mass; the material tint stays neutral here. (The StormWall material sets a dark _LitColor instead.)
            material.SetColor("_LitColor", Color.white);
            material.SetFloat("_GradientPower", crownContrast);
            material.SetFloat("_SunWrap", sunWrap);
            material.SetFloat("_Opacity", opacity);
            material.SetFloat("_SoftEdge", edgeSoftness);
        }

        /// <summary>Rebuild every cloud as a heaped cluster of soft-sphere puffs and push them to the particle system.</summary>
        [ContextMenu("Refill")]
        public void Refill()
        {
            if (_ps == null) return;
            var rng = new System.Random(seed);
            int clouds = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1, maxClouds, Mathf.Clamp01(coverage))));
            float rad = windAngle * Mathf.Deg2Rad;
            Vector3 wind = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * windSpeed;
            _windVec = wind; _refillTime = Time.time;   // for the drift-corrected sun-occlusion test
            Vector3 baseP = transform.position;

            _puffs.Clear();
            var list = new List<ParticleSystem.Particle>(clouds * puffsPerCloud.y);
            for (int ci = 0; ci < clouds; ci++)
            {
                float cx = (float)(rng.NextDouble() - 0.5) * 2f * areaRadius;
                float cz = (float)(rng.NextDouble() - 0.5) * 2f * areaRadius;
                float cy = cloudHeight + (float)(rng.NextDouble() - 0.5) * cloudThickness;
                Vector3 center = new Vector3(baseP.x + cx, cy, baseP.z + cz);
                float scl = cloudSize * Mathf.Lerp(0.6f, 1.35f, (float)rng.NextDouble());
                // Puff size AND count scale with this cloud's actual size, so big clouds stay tightly packed
                // (no crumbling edges) and small clouds don't get overcrowded.
                float sclRatio = scl / Mathf.Max(1f, cloudSize);
                int k = Mathf.Max(8, Mathf.RoundToInt(rng.Next(puffsPerCloud.x, puffsPerCloud.y + 1) * sclRatio * sclRatio));
                float halfSpan = Mathf.Max(1f, scl * cloudFlatten);

                // CUMULUS: main body heap + a crown of smaller bumps on TOP; the base is clamped flat.
                int crown = Mathf.Max(3, k / 2);
                for (int pi = 0; pi < k + crown; pi++)
                {
                    bool isCrown = pi >= k;
                    float rr = scl * Mathf.Pow((float)rng.NextDouble(), isCrown ? 0.8f : 0.65f);
                    if (isCrown) rr *= 0.75f;                                          // crown bumps sit inboard
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float ex = Mathf.Cos(ang) * rr, ez = Mathf.Sin(ang) * rr;
                    float t = rr / Mathf.Max(1f, scl);                                // 0 centre .. 1 edge
                    float domeY = (0.5f - t) * scl * cloudFlatten;                    // centre puffs sit higher (dome)
                    float ey = domeY + (float)(rng.NextDouble() - 0.5f) * scl * cloudFlatten * 0.6f;
                    if (isCrown) ey = Mathf.Abs(ey) + halfSpan * 0.25f;               // bumps ride ON TOP of the mass
                    ey = Mathf.Max(ey, -halfSpan * 0.35f);                            // FLAT-ish base (cumulus)

                    var p = new ParticleSystem.Particle();
                    p.position = center + new Vector3(ex, ey, ez);
                    float sizeFall = Mathf.Lerp(1f, 0.6f, t);                         // bigger in the middle
                    float size = Mathf.Lerp(puffSize.x, puffSize.y, (float)rng.NextDouble()) * sizeFall * sclRatio;
                    if (isCrown) size *= Mathf.Lerp(0.4f, 0.6f, (float)rng.NextDouble());   // small billow bumps
                    p.startSize = size;
                    p.rotation3D = new Vector3((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
                    // BAKE the shading: the puff's height within the WHOLE cloud drives a lit-crown → shadow-base
                    // gradient, so the merged mass shades as one cloud (COZY-style), not per-ball.
                    float hN = Mathf.Clamp01((ey / halfSpan) * 0.5f + 0.55f);
                    float shade = Mathf.Pow(hN, Mathf.Max(0.2f, crownContrast));
                    // CARTOON: snap the gradient into flat tone bands (cream crown / mid / blue base, like drawn clouds).
                    if (shadeBands > 1)
                        shade = Mathf.Clamp01(Mathf.Floor(shade * shadeBands) / (shadeBands - 1f));
                    var pc = Color.Lerp(shadowColor, litColor, shade);
                    pc.a = 1f;
                    p.startColor = pc;
                    p.startLifetime = 1e9f; p.remainingLifetime = 1e9f;
                    p.velocity = wind;
                    list.Add(p);
                    _puffs.Add(new Vector4(p.position.x, p.position.y, p.position.z, size * 0.5f));
                }
            }
            var arr = list.ToArray();
            var main = _ps.main; main.maxParticles = Mathf.Max(main.maxParticles, arr.Length);
            _ps.Clear();
            _ps.Play();                       // must be playing before SetParticles or they don't stick (edit mode)
            _ps.SetParticles(arr, arr.Length);
        }

        void ResolveMaterial()
        {
            if (material != null) return;
            var sh = Shader.Find(DefaultShader);
            if (sh == null) return;
            material = new Material(sh) { name = "IslandClouds (runtime)" };
#if UNITY_EDITOR
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexPath);
            if (t != null) material.SetTexture("_MainTex", t);
#endif
        }
    }
}
