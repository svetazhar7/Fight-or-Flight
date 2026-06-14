using UnityEngine;

/// <summary>
/// One static low-poly mesh of the entire map, textured with the same global
/// colormap as the real terrain and sunk a few meters below it. It is always
/// visible, so beyond the chunk streaming radius the player sees a continuous
/// world all the way to the horizon instead of a cut-off edge; nearby, the
/// full-detail chunks render on top of it. ~33k triangles in a single draw
/// call - effectively free on the GPU.
/// </summary>
public static class FarTerrainRenderer
{
    public static GameObject Build(WorldData world, Transform parent, int resolution, float sinkMeters)
    {
        int n = Mathf.Clamp(resolution, 33, 257);
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];

        for (int z = 0; z < n; z++)
        {
            float nz = (float)z / (n - 1);
            for (int x = 0; x < n; x++)
            {
                float nx = (float)x / (n - 1);
                int i = z * n + x;
                float h = world.SampleHeight01(nx, nz) * world.worldSize.y - sinkMeters;
                verts[i] = new Vector3(nx * world.worldSize.x, h, nz * world.worldSize.z);
                uvs[i] = new Vector2(nx, nz);
            }
        }

        var tris = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int z = 0; z < n - 1; z++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x;
                int b = a + 1;
                int c = a + n;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        var mesh = new Mesh { name = "FarTerrain" };
        mesh.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "FarTerrainMat" };
        mat.SetTexture("_BaseMap", world.colormap);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", Color.black);

        var go = new GameObject("FarTerrain");
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
