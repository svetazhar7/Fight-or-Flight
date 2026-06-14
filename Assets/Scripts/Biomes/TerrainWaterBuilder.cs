using UnityEngine;

/// <summary>
/// Builds the world's water surface. Water is a single flat ocean plane at sea
/// level: the toon water shader reads the depth buffer, so terrain rising above
/// sea level naturally occludes the plane (z-test) while terrain below it shows
/// as depth-tinted water with shoreline foam where the surface meets the seabed.
/// (Carved lakes and rivers were removed - the ocean covers all lowland water.)
/// </summary>
public static class TerrainWaterBuilder
{
    [System.Serializable]
    public struct Settings
    {
        public bool oceansEnabled;
        public float seaLevel;          // normalized height (0..1); terrain below this floods

        public Color waterShallow;
        public Color waterDeep;

        [Tooltip("Water surface material (toon water). When empty, a plain transparent URP/Lit material is built as a fallback.")]
        public Material material;
    }

    /// <summary>
    /// Builds the ocean: one flat water plane spanning the whole map at sea level.
    /// No heightmap carving is needed - the toon water shader reads the depth
    /// buffer, so terrain rising above sea level naturally occludes the plane
    /// (z-test) while terrain below it shows as depth-tinted water with shoreline
    /// foam wherever the surface meets the seabed. A modest grid (not a single
    /// quad) keeps it ready for future vertex waves and avoids huge triangles.
    /// </summary>
    public static GameObject BuildOceanMesh(float seaLevelNormalized, Vector3 terrainSize,
        Settings s, int subdivisions, Transform parent)
    {
        int n = Mathf.Clamp(subdivisions, 1, 200);
        float y = Mathf.Clamp01(seaLevelNormalized) * terrainSize.y;

        int side = n + 1;
        var verts = new Vector3[side * side];
        var uvs = new Vector2[side * side];
        for (int gz = 0; gz <= n; gz++)
        {
            float tz = (float)gz / n;
            for (int gx = 0; gx <= n; gx++)
            {
                float tx = (float)gx / n;
                int i = gz * side + gx;
                verts[i] = new Vector3(tx * terrainSize.x, y, tz * terrainSize.z);
                uvs[i] = new Vector2(tx, tz);
            }
        }

        var tris = new int[n * n * 6];
        int t = 0;
        for (int gz = 0; gz < n; gz++)
        {
            for (int gx = 0; gx < n; gx++)
            {
                int a = gz * side + gx;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        var mesh = new Mesh { name = "OceanMesh" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Ocean");
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = s.material != null ? s.material : MakeWaterMaterial(s.waterDeep);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private static Material MakeWaterMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = "WaterMaterial" };

        Color c = color; c.a = 0.72f;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.55f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.55f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

        // Transparent surface (URP Lit).
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }
}
