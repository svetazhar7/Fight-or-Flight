using UnityEngine;

/// <summary>
/// Carves shallow lakes and narrow rivers into a normalized heightmap and builds
/// a matching water surface mesh. Lakes are small rounded basins; rivers start
/// high in the mountains and follow the terrain downhill as thin, shallow
/// channels. Everything is intentionally small/shallow so the world stays
/// flyable and readable from the air.
/// </summary>
public static class TerrainWaterBuilder
{
    [System.Serializable]
    public struct Settings
    {
        public bool lakesEnabled;
        public int lakeCount;
        public float lakeRadius;        // as fraction of the map (0..1)
        public float lakeDepth;         // normalized height units
        public float lakeMaxTerrain;    // only spawn lakes where terrain is below this (0..1)

        public bool riversEnabled;
        public int riverCount;
        public float riverDepth;        // normalized height units
        public int riverWidth;          // channel half-width in heightmap cells
        public float riverSourceMinHeight; // rivers start above this normalized height
        public float riverMeander;      // 0 = straight downhill, 1 = wanders a lot
        public int riverMaxSteps;

        public Color waterShallow;
        public Color waterDeep;
    }

    public class Result
    {
        public float[,] surface; // normalized water surface height, or NoWater where dry
        public bool any;
        public const float NoWater = -1f;
    }

    /// <summary>
    /// Mutates <paramref name="heights"/> in place (carving basins/channels) and
    /// returns the per-cell water surface field. <paramref name="flow"/> is a
    /// smoothed copy of the terrain (the base shape before fine ruggedness) used
    /// for lake placement and river descent, so rivers follow the large-scale
    /// downhill instead of getting trapped in tiny noise pits.
    /// </summary>
    public static Result Carve(float[,] heights, float[,] flow, int res, Settings s, System.Random rng)
    {
        var result = new Result();
        var surface = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                surface[z, x] = Result.NoWater;
        result.surface = surface;

        if (s.lakesEnabled && s.lakeCount > 0)
            CarveLakes(heights, flow, surface, res, s, rng, result);

        if (s.riversEnabled && s.riverCount > 0)
            CarveRivers(heights, flow, surface, res, s, rng, result);

        return result;
    }

    private static void CarveLakes(float[,] heights, float[,] original, float[,] surface,
        int res, Settings s, System.Random rng, Result result)
    {
        int radiusCells = Mathf.Max(2, Mathf.RoundToInt(s.lakeRadius * res));
        int placed = 0, attempts = 0, maxAttempts = s.lakeCount * 30;

        while (placed < s.lakeCount && attempts < maxAttempts)
        {
            attempts++;
            int cx = rng.Next(radiusCells, res - radiusCells);
            int cz = rng.Next(radiusCells, res - radiusCells);

            float centerH = original[cz, cx];
            if (centerH > s.lakeMaxTerrain) continue; // keep lakes in the lowlands

            // Vary each lake's size a little.
            int r = Mathf.Max(2, Mathf.RoundToInt(radiusCells * Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble())));
            float surf = centerH; // flat lake surface at the centre height

            for (int dz = -r; dz <= r; dz++)
            {
                int z = cz + dz;
                if (z < 0 || z >= res) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= res) continue;

                    float dist = Mathf.Sqrt(dx * dx + dz * dz) / r;
                    if (dist >= 1f) continue;

                    // Smooth bowl: deepest at the centre, zero at the rim.
                    float bowl = s.lakeDepth * Mathf.SmoothStep(1f, 0f, dist);
                    if (bowl <= 0f) continue;

                    float bed = surf - bowl;
                    if (heights[z, x] > bed) heights[z, x] = bed;

                    // Mark water where the surface sits above the (carved) bed.
                    if (surf > heights[z, x] + 1e-5f)
                    {
                        if (surface[z, x] < surf) surface[z, x] = surf;
                        result.any = true;
                    }
                }
            }
            placed++;
        }
    }

    private static void CarveRivers(float[,] heights, float[,] original, float[,] surface,
        int res, Settings s, System.Random rng, Result result)
    {
        int placed = 0, attempts = 0, maxAttempts = s.riverCount * 40;

        while (placed < s.riverCount && attempts < maxAttempts)
        {
            attempts++;

            // Find a high source: sample a handful of cells, take the highest.
            int sx = 0, sz = 0;
            float bestH = -1f;
            for (int k = 0; k < 12; k++)
            {
                int rx = rng.Next(res);
                int rz = rng.Next(res);
                if (original[rz, rx] > bestH) { bestH = original[rz, rx]; sx = rx; sz = rz; }
            }
            if (bestH < s.riverSourceMinHeight) continue;

            if (TraceRiver(heights, original, surface, res, s, rng, sx, sz))
            {
                result.any = true;
                placed++;
            }
        }
    }

    // Follows the terrain downhill from (sx,sz), carving a thin shallow channel.
    // Returns true if the river travelled a meaningful distance.
    private static bool TraceRiver(float[,] heights, float[,] original, float[,] surface,
        int res, Settings s, System.Random rng, int sx, int sz)
    {
        int x = sx, z = sz;
        int steps = 0;
        int carved = 0;
        int width = Mathf.Max(1, s.riverWidth);

        // 8-neighbour offsets.
        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] oz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        int lastDir = -1;
        // Momentum budget: how many cells the river may push "uphill" through a
        // minor barrier before it's considered to have settled into a basin.
        int climbBudget = 24;

        while (steps < s.riverMaxSteps)
        {
            steps++;

            CarveChannelCell(heights, original, surface, res, x, z, width, s.riverDepth);
            carved++;

            // Pick the lowest neighbour (with optional meander) to descend into.
            float here = original[z, x];
            int bestI = -1;
            float bestNeighbourH = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                int nx = x + ox[i];
                int nz = z + oz[i];
                if (nx < 0 || nx >= res || nz < 0 || nz >= res) return carved > 4;

                float nh = original[nz, nx];
                // Lower score = preferred. Real downhill (big height drops on
                // mountains) dominates; on near-flat plains the strong direction
                // bias keeps the river running straight across instead of
                // puddling in place, while the meander term adds gentle wander.
                float score = nh;
                score += (float)(rng.NextDouble() - 0.5) * s.riverMeander * 0.006f;
                if (i == lastDir) score -= 0.02f;
                if (score < bestNeighbourH) { bestNeighbourH = score; bestI = i; }
            }

            if (bestI < 0) return carved > 4;

            int stepX = x + ox[bestI];
            int stepZ = z + oz[bestI];

            // If the best move is uphill we're climbing out of a pit; spend budget.
            if (original[stepZ, stepX] >= here)
            {
                climbBudget--;
                if (climbBudget <= 0) return carved > 4; // settled in a basin -> river ends
            }
            else
            {
                climbBudget = Mathf.Min(24, climbBudget + 1);
            }

            lastDir = bestI;
            x = stepX;
            z = stepZ;
        }
        return carved > 4;
    }

    private static void CarveChannelCell(float[,] heights, float[,] original, float[,] surface,
        int res, int cx, int cz, int width, float depth)
    {
        float centerOrig = original[cz, cx];
        for (int dz = -width; dz <= width; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= res) continue;
            for (int dx = -width; dx <= width; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= res) continue;

                float dist = Mathf.Sqrt(dx * dx + dz * dz) / (width + 0.5f);
                if (dist >= 1f) continue;

                float d = depth * (1f - dist); // V-shaped channel
                float bed = centerOrig - d;
                if (heights[z, x] > bed) heights[z, x] = bed;

                // Thin ribbon of water sitting in the channel, just below the rim.
                float surf = centerOrig - depth * 0.35f;
                if (surf > heights[z, x] + 1e-5f && surface[z, x] < surf)
                {
                    surface[z, x] = surf;
                }
            }
        }
    }

    /// <summary>
    /// Builds a water surface mesh GameObject (child of <paramref name="parent"/>)
    /// from the water field. The mesh is generated at a coarse resolution to stay
    /// cheap; lakes show as solid water, while thin rivers are mainly conveyed by
    /// the blue tint baked into the terrain colormap.
    /// </summary>
    public static GameObject BuildWaterMesh(Result water, int res, Vector3 terrainSize,
        Settings s, int meshResolution, Transform parent)
    {
        if (water == null || !water.any) return null;

        int m = Mathf.Clamp(meshResolution, 32, 512);
        float step = (float)(res - 1) / (m - 1);

        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        int[,] index = new int[m, m];
        bool[,] isWet = new bool[m, m];

        for (int gz = 0; gz < m; gz++)
        {
            for (int gx = 0; gx < m; gx++)
            {
                int sxi = Mathf.Clamp(Mathf.RoundToInt(gx * step), 0, res - 1);
                int szi = Mathf.Clamp(Mathf.RoundToInt(gz * step), 0, res - 1);
                float surf = water.surface[szi, sxi];

                index[gz, gx] = -1;
                if (surf <= Result.NoWater + 0.5f) continue;

                float wx = (float)gx / (m - 1) * terrainSize.x;
                float wz = (float)gz / (m - 1) * terrainSize.z;
                float wy = surf * terrainSize.y;

                index[gz, gx] = verts.Count;
                isWet[gz, gx] = true;
                verts.Add(new Vector3(wx, wy, wz));
                uvs.Add(new Vector2((float)gx / (m - 1), (float)gz / (m - 1)));
            }
        }

        if (verts.Count == 0) return null;

        for (int gz = 0; gz < m - 1; gz++)
        {
            for (int gx = 0; gx < m - 1; gx++)
            {
                // Emit a quad only where all four corners hold water (clean lake edges).
                if (!isWet[gz, gx] || !isWet[gz, gx + 1] || !isWet[gz + 1, gx] || !isWet[gz + 1, gx + 1])
                    continue;

                int a = index[gz, gx];
                int b = index[gz, gx + 1];
                int c = index[gz + 1, gx];
                int d = index[gz + 1, gx + 1];

                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        if (tris.Count == 0) return null;

        var mesh = new Mesh { name = "WaterMesh" };
        mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Water");
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = MakeWaterMaterial(s.waterDeep);
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
