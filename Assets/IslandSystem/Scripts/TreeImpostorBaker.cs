using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IslandSystem
{
    /// <summary>
    /// Bakes a HEMI-OCTAHEDRAL impostor atlas for a tree prefab: the prefab is rendered from a grid of view
    /// directions covering the upper hemisphere (yaw × elevation) into one atlas texture. At runtime the impostor
    /// shader picks the atlas cell whose baked direction matches the camera→tree direction, so a single 2-triangle
    /// billboard shows the tree from the correct angle — including from ABOVE (aerial), which a flat billboard can't.
    /// Baking uses URP's on-demand <see cref="RenderPipeline.SubmitRenderRequest"/> into a small per-cell RT that is
    /// copied into the atlas, so it works without touching the live scene cameras. Editor/runtime callable.
    /// </summary>
    public static class TreeImpostorBaker
    {
        public struct Result
        {
            public Texture2D atlas;      // baked hemi-oct atlas (grid×grid cells)
            public int grid;
            public Vector3 boundsSize;   // world-space size of the tree at scale 1 (for quad sizing)
            public Vector3 centerLocal;  // bounds centre relative to the prefab pivot (for quad placement)
        }

        /// <summary>Hemi-octahedral decode — MUST match the shader's HemiOctaDecode.</summary>
        static Vector3 HemiOctaDecode(Vector2 e)
        {
            e = e * 2f - Vector2.one;
            Vector2 t = new Vector2(e.x + e.y, e.x - e.y) * 0.5f;
            float y = 1f - Mathf.Abs(t.x) - Mathf.Abs(t.y);
            return new Vector3(t.x, y, t.y).normalized;
        }

        public static Result Bake(GameObject prefab, int grid = 8, int cellSize = 128, int bakeLayer = 31)
        {
            // Isolate a temp instance high above the world on a dedicated layer so the bake camera sees ONLY it.
            var tmp = Object.Instantiate(prefab);
            var basePos = new Vector3(0f, 100000f, 0f);
            tmp.transform.SetPositionAndRotation(basePos, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;   // bake at unit scale; the atlas is scale-independent
            SetLayerRecursive(tmp, bakeLayer);

            Bounds b = ComputeWorldBounds(tmp);
            Vector3 center = b.center;
            float radius = b.extents.magnitude + 0.01f;
            float orthoSize = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z)) * 1.04f;

            int atlasRes = grid * cellSize;
            var cellRT = new RenderTexture(cellSize, cellSize, 24, RenderTextureFormat.ARGB32) { name = "~impostorCell" };
            cellRT.Create();
            var atlasRT = new RenderTexture(atlasRes, atlasRes, 0, RenderTextureFormat.ARGB32);
            atlasRT.Create();
            var prevActive = RenderTexture.active;
            RenderTexture.active = atlasRT; GL.Clear(true, true, new Color(0, 0, 0, 0)); RenderTexture.active = prevActive;

            var camGO = new GameObject("~ImpostorBakeCam");
            var cam = camGO.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.aspect = 1f;
            cam.cullingMask = 1 << bakeLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 4f + 20f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var extra = camGO.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = false;
            extra.renderShadows = false;
            extra.requiresColorOption = CameraOverrideOption.Off;
            extra.requiresDepthOption = CameraOverrideOption.Off;

            float camDist = radius * 2f + 1f;
            for (int j = 0; j < grid; j++)
                for (int i = 0; i < grid; i++)
                {
                    Vector2 uv = new Vector2((i + 0.5f) / grid, (j + 0.5f) / grid);
                    Vector3 dir = HemiOctaDecode(uv);
                    Vector3 up = Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up;
                    cam.transform.position = center + dir * camDist;
                    cam.transform.rotation = Quaternion.LookRotation(center - cam.transform.position, up);

                    var req = new RenderPipeline.StandardRequest { destination = cellRT };
                    if (RenderPipeline.SupportsRenderRequest(cam, req))
                        RenderPipeline.SubmitRenderRequest(cam, req);
                    Graphics.CopyTexture(cellRT, 0, 0, 0, 0, cellSize, cellSize, atlasRT, 0, 0, i * cellSize, j * cellSize);
                }

            // Read the atlas RT into a CPU Texture2D so it survives as a plain asset-free texture (mip + clamp).
            var tex = new Texture2D(atlasRes, atlasRes, TextureFormat.RGBA32, true);
            RenderTexture.active = atlasRT;
            tex.ReadPixels(new Rect(0, 0, atlasRes, atlasRes), 0, 0, false);
            tex.Apply(true, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            RenderTexture.active = prevActive;

            Object.DestroyImmediate(camGO);
            cellRT.Release(); Object.DestroyImmediate(cellRT);
            atlasRT.Release(); Object.DestroyImmediate(atlasRT);
            if (Application.isPlaying) Object.Destroy(tmp); else Object.DestroyImmediate(tmp);

            return new Result { atlas = tex, grid = grid, boundsSize = b.size, centerLocal = center - basePos };
        }

        static Bounds ComputeWorldBounds(GameObject go)
        {
            Bounds b = default; bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(go.transform.position, Vector3.one);
            return b;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
