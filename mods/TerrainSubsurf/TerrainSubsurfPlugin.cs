using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
// ConfigurationManagerAttributes lives in the global namespace (BepInEx
// ConfigurationManager convention, referenced by Jotunn). Used unqualified.
using UnityEngine;

namespace TerrainSubsurf;

[BepInPlugin(GUID, NAME, VERSION)]
[BepInDependency("com.jotunn.jotunn")] // loads after Jotunn so ConfigManager panel is ready
public class TerrainSubsurfPlugin : BaseUnityPlugin
{
    public const string GUID = "lagalot.terrainsubsurf";
    public const string NAME = "TerrainSubsurf";
    public const string VERSION = "0.1.0";

    internal static new ManualLogSource Logger = null!;

    internal static ConfigEntry<bool> _enabled = null!;
    internal static ConfigEntry<int> _factor = null!;
    internal static ConfigEntry<string> _mode = null!;
    internal static ConfigEntry<float> _cullDistance = null!;
    internal static ConfigEntry<float> _flatEpsilon = null!;
    internal static ConfigEntry<float> _cliffBlendBand = null!;

    internal static ConfigEntry<bool> _cliffFaceUV = null!;
    internal static ConfigEntry<float> _cliffTileSize = null!;
    internal static ConfigEntry<float> _cliffThreshold = null!;

    private void Awake()
    {
        Logger = base.Logger;
        // Jotunn ConfigManager reads ConfigurationManagerAttributes tags to render the
        // in-game panel (open with F1 by default). Our postfix reads these live, so a
        // slider/toggle change re-subdivides on the next terrain rebuild.
        _enabled = Config.Bind("General", "Enabled", true,
            new ConfigDescription("Master toggle for terrain render smoothing.",
                null, new ConfigurationManagerAttributes { Order = 5 }));
        _factor = Config.Bind("General", "SubdivisionFactor", 2,
            new ConfigDescription("Render-mesh resolution multiplier. 1 = off (vanilla). 4/8 are heavy; 2 is the default sweet spot.",
                new AcceptableValueList<int>(1, 2, 4, 8),
                new ConfigurationManagerAttributes { Order = 4 }));
        _mode = Config.Bind("General", "SmoothingMode", "CatmullRom",
            new ConfigDescription("Height interpolation for new vertices. CatmullRom curves the surface; Linear makes smaller flat facets.",
                new AcceptableValueList<string>("Linear", "CatmullRom"),
                new ConfigurationManagerAttributes { Order = 3 }));
        _cullDistance = Config.Bind("General", "CullDistance", 0f,
            new ConfigDescription("Only smooth patches within this distance of the player (world units). 0 = smooth ALL loaded patches (recommended — the whole world renders better). Set >0 to skip far patches for perf.",
                null, new ConfigurationManagerAttributes { Order = 2 }));
        _flatEpsilon = Config.Bind("General", "FlatEpsilon", 0.05f,
            new ConfigDescription("Skip smoothing patches whose height range (max-min) is below this — flat terrain stays at vanilla cost.",
                null, new ConfigurationManagerAttributes { Order = 1 }));
        _cliffFaceUV = Config.Bind("General", "CliffFaceUV", true,
            new ConfigDescription("Remap UVs on steep terrain so textures tile down cliff faces instead of stretching.",
                null, new ConfigurationManagerAttributes { Order = 0 }));
        _cliffTileSize = Config.Bind("General", "CliffFaceTileSize", 2f,
            new ConfigDescription("World units per texture tile on cliff faces (smaller = more repeats).",
                null, new ConfigurationManagerAttributes { Order = -1 }));
        _cliffThreshold = Config.Bind("General", "CliffFaceThreshold", 0.3f,
            new ConfigDescription("Steepness below which a face is treated as a cliff (|normal.y|; 0.3 ≈ 72°, lower = only sheer walls).",
                null, new ConfigurationManagerAttributes { Order = -2 }));
        _cliffBlendBand = Config.Bind("General", "CliffFaceBlendBand", 0.2f,
            new ConfigDescription("Width of the blend zone between cliff and plan-view UV, in |normal.y| units. 0 = hard snap (old behavior, smudges). 0.2 = smooth crossfade.",
                null, new ConfigurationManagerAttributes { Order = -3 }));

        Logger.LogInfo($"{NAME} {VERSION} | Enabled={_enabled.Value} Factor={_factor.Value} Mode={_mode.Value}");

        var harmony = new Harmony(GUID);
        harmony.PatchAll(typeof(Patches));
        Logger.LogInfo("Harmony patched Heightmap.RebuildRenderMesh");

        // Live config: invalidate the cache on ANY setting change so already-smoothed
        // patches re-subdivide with the new factor/mode/cull distance. Clearing the cache
        // alone suffices — the proximity tick (0.5s) re-smooths near patches, and vanilla
        // rebuilds handle any terrain touched later. For an instant visible update we
        // also poke all near heightmaps to rebuild.
        Config.SettingChanged += (_, _) =>
        {
            Patches.InvalidateCache();
            Patches.RebuildAllNearHeightmaps();
            Logger.LogInfo($"Config changed → cache cleared, factor={_factor.Value} mode={_mode.Value} cull={_cullDistance.Value} flat={_flatEpsilon.Value}");
        };
    }

    // ponytail: throttle to ~0.5s — the proximity scan is O(loaded patches) with a
    // 2-float distance check each, so per-frame would be fine, but there's no reason
    // to pay it 60x/sec. New patches smooth within half a second of walking near.
    private const float ProximityInterval = 0.5f;
    private float _nextProximityCheck;

    /// <summary>
    /// Proximity smoothing: patches culled by CullDistance at load never see another
    /// vanilla RebuildRenderMesh when you just walk near them (vanilla only rebuilds
    /// on terrain change / zone load), so they'd stay sharp forever without a dig.
    /// This tick subdivides any near, not-yet-subdivided patch — sharp terrain that
    /// generated far away smooths as you approach it.
    /// </summary>
    private void Update()
    {
        if (Time.time < _nextProximityCheck) return;
        _nextProximityCheck = Time.time + ProximityInterval;
        try
        {
            Patches.SmoothNearbyPatches();
        }
        catch (Exception e)
        {
            Logger?.LogWarning($"TerrainSubsurf proximity pass failed: {e}");
        }
    }
}

internal static class Patches
{
    /// <summary>Height-range threshold below which a patch is considered flat.</summary>
    private const int kHeightSampleStride = 4;

    /// <summary>
    /// Per-Heightmap cache of the last-subdivided state. ConditionalWeakTable so the
    /// key does not keep Heightmaps alive (they are destroyed on zone unload).
    /// m_heights is mutated in place, so we hash CONTENTS, not reference identity.
    /// </summary>
    private sealed class CacheEntry
    {
        public int Hash;
        public int Width;
        public float Scale;
    }

    private static System.Runtime.CompilerServices.ConditionalWeakTable<Heightmap, CacheEntry> _cache = new();

    internal static void InvalidateCache() => _cache = new();  // ponytail: CWT has no Clear on net48; swap the whole table

    /// <summary>Trigger a vanilla rebuild on every near (non-LOD) heightmap so the
    /// postfix re-subdivides them with the current config. Snapshot to avoid mid-pass
    /// mutation of s_heightmaps.</summary>
    internal static void RebuildAllNearHeightmaps()
    {
        var snap = new List<Heightmap>(Heightmap.GetAllHeightmaps());
        foreach (var hm in snap)
        {
            if (hm == null || hm.IsDistantLod) continue;
            try { hm.Poke(); }
            catch { /* ignore — a destroyed patch during a settings flip is fine */ }
        }
    }

    [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
    [HarmonyPostfix]
    private static void Postfix(Heightmap __instance)
    {
        try
        {
            if (!TerrainSubsurfPlugin._enabled.Value || _factorVal() <= 1) return;
            // Skip distant-LOD heightmaps: subdividing far terrain wastes GPU and can
            // fight the LOD swap. Near terrain is where sharp edges actually read.
            if (__instance.IsDistantLod) return;
            if (IsCulledByDistance(__instance)) return;
            Subdivide(__instance, _factorVal(), _modeVal() == "CatmullRom");
        }
        catch (Exception e)
        {
            // Never break the vanilla render path.
            TerrainSubsurfPlugin.Logger?.LogWarning($"TerrainSubsurf postfix failed (vanilla mesh kept): {e}");
        }
    }

    /// <summary>True if the patch is farther than CullDistance from the player (xz only).</summary>
    private static bool IsCulledByDistance(Heightmap hm)
    {
        float cull = TerrainSubsurfPlugin._cullDistance.Value;
        if (cull <= 0f) return false; // 0 disables culling
        // Player.m_localPlayer is public static (decompiled line 158). If null (menu,
        // or no player yet), do NOT cull — safer to subdivide.
        if (Player.m_localPlayer == null) return false;
        Vector3 p = Player.m_localPlayer.transform.position;
        Vector3 c = hm.transform.position;
        float dx = p.x - c.x, dz = p.z - c.z;
        return dx * dx + dz * dz > cull * cull;
    }

    /// <summary>True if we have already subdivided this patch (mesh + cache set).</summary>
    internal static bool HasBeenSubdivided(Heightmap hm) => _cache.TryGetValue(hm, out _);

    /// <summary>
    /// Throttled proximity pass (called from the plugin's Update). Subdivides loaded
    /// near patches we haven't done yet — the ones that were culled by distance at
    /// load time and never got a vanilla rebuild when the player walked near.
    /// </summary>
    internal static void SmoothNearbyPatches()
    {
        if (!TerrainSubsurfPlugin._enabled.Value || _factorVal() <= 1) return;
        if (TerrainSubsurfPlugin._cullDistance.Value <= 0f) return; // culling off: postfix already does everything at load
        if (Player.m_localPlayer == null) return;

        // Snapshot s_heightmaps: vanilla Add/Removes it from Awake/OnDestroy/LOD swaps
        // (all main thread, but can happen mid-pass via zone load in the same frame).
        // Reused list — grows once to the max patch count, no per-tick allocation.
        _snapshot.Clear();
        _snapshot.AddRange(Heightmap.GetAllHeightmaps());

        for (int i = 0; i < _snapshot.Count; i++)
        {
            Heightmap hm = _snapshot[i];
            if (hm == null || hm.IsDistantLod) continue; // defensive: list should already exclude LOD
            if (IsCulledByDistance(hm)) continue;        // still far — leave it vanilla for now
            if (HasBeenSubdivided(hm)) continue;         // already smoothed; skip even the hash pass
            try
            {
                // Not in cache -> this patch was culled at load (or newly loaded within
                // range). Subdivide directly; no vanilla rebuild needed.
                Subdivide(hm, _factorVal(), _modeVal() == "CatmullRom");
            }
            catch (Exception e)
            {
                // One bad patch must not stop the rest of the pass.
                TerrainSubsurfPlugin.Logger?.LogWarning($"TerrainSubsurf proximity subdivide failed (patch skipped): {e}");
            }
        }
    }

    // Reused snapshot buffer for SmoothNearbyPatches (no per-tick allocation beyond
    // occasional growth of the internal array).
    private static readonly List<Heightmap> _snapshot = new List<Heightmap>(64);

    /// <summary>Cheap rolling hash of the height grid (every 4th sample).</summary>
    private static int HashHeights(List<float> h)
    {
        int hash = 17;
        for (int i = 0; i < h.Count; i += kHeightSampleStride)
            hash = hash * 397 ^ h[i].GetHashCode();
        return hash;
    }

    private static int _factorVal() => TerrainSubsurfPlugin._factor.Value;

    private static string _modeVal() => TerrainSubsurfPlugin._mode.Value;

    private static void Subdivide(Heightmap hm, int factor, bool catmull)
    {
        int w = HeightmapAccess.GetWidth(hm);
        float scale = HeightmapAccess.GetScale(hm);
        List<float> heights = HeightmapAccess.GetHeights(hm);
        Mesh renderMesh = HeightmapAccess.GetRenderMesh(hm);
        MeshFilter mf = HeightmapAccess.GetMeshFilter(hm);
        if (heights == null || renderMesh == null || mf == null) return;

        // Guard 1: vanilla copies render-mesh vertices from m_collisionMesh, so if
        // the collision mesh was never built (e.g. menu heightmaps before a world
        // loads), the vanilla render mesh is EMPTY. Vanilla renders nothing there;
        // if we synthesize a mesh from m_heights anyway we inject geometry where
        // vanilla had none -> garbage/degenerate mesh -> black screen. Never do that.
        var vanillaVerts = new List<Vector3>();
        renderMesh.GetVertices(vanillaVerts);
        if (vanillaVerts.Count == 0) return; // nothing rendered vanilla-side; keep it that way

        int n = w + 1;               // vanilla grid
        // Guard 2: heights must be a full (w+1)^2 grid, and w/scale must be sane.
        if (w <= 0 || scale <= 0f || heights.Count < n * n) return;
        // Guard 3: reject NaN/Inf height data (uninitialized samples would poison
        // every interpolated vertex and the GPU silently drops the mesh).
        bool bad = false;
        for (int c = 0; c < heights.Count; c++) { float v = heights[c]; if (float.IsNaN(v) || float.IsInfinity(v)) { bad = true; break; } }
        if (bad) return;

        // OPTIMIZATION 1 — skip-if-flat: one O(n) pass. Flat terrain (max-min < eps)
        // gets no visual benefit from smoothing; leave the vanilla mesh untouched.
        float max = float.MinValue, min = float.MaxValue;
        for (int c = 0; c < heights.Count; c++) { float v = heights[c]; if (v > max) max = v; if (v < min) min = v; }
        if (max - min < TerrainSubsurfPlugin._flatEpsilon.Value) return;

        // OPTIMIZATION 3 — skip-if-unchanged: heights are hashed in place; if the
        // content hash + width + scale match what we already subdivided, the mesh we
        // set last time is still valid. Catches redundant rebuilds when a neighbouring
        // patch is modified and this one re-renders unchanged.
        int hash = HashHeights(heights);
        if (_cache.TryGetValue(hm, out var prev) && prev.Hash == hash && prev.Width == w && prev.Scale == scale) return;

        int fw = w * factor;         // fine grid edge (in vanilla steps)
        int fn = fw + 1;             // fine vertex count per side
        float half = (float)w * scale * 0.5f;

        var verts = new Vector3[fn * fn];
        var uvs = new Vector2[fn * fn];
        var colors = new Color32[fn * fn];

        float step = 1f / factor;    // fine step in vanilla-vertex units

        for (int i = 0; i < fn; i++)
        {
            float y = i * step;      // vanilla vertex-space y (0..w)
            for (int j = 0; j < fn; j++)
            {
                float x = j * step;  // vanilla vertex-space x (0..w)
                int idx = i * fn + j;

                float h = catmull ? SampleCatmullRom(heights, n, x, y, w)
                                  : SampleLinear(heights, n, x, y, w);

                // Match CalcVertex: (-w*s/2 + x*s, height, -w*s/2 + y*s), local space.
                verts[idx] = new Vector3(-half + x * scale, h, -half + y * scale);

                // UV: same 0..1 patch mapping as vanilla (j/w, i/w) — no texture stretch.
                uvs[idx] = new Vector2(x / w, y / w);

                // OPTIMIZATION 5 — colors straight from vanilla's private
                // GetBiomeColor(ix, iy) (same SmoothStep lerp of m_cornerBiomes incl.
                // AltBiome overrides). t = col/(w*factor) maps the fine vertex to the
                // same 0..1 range vanilla uses; then vanilla's own smoothstep.
                float tX = (float)j / fw, tY = (float)i / fw;
                float ix = tX * tX * (3f - 2f * tX);
                float iy = tY * tY * (3f - 2f * tY);
                colors[idx] = HeightmapAccess.GetBiomeColor(hm, ix, iy);
            }
        }

        var indices = new int[fw * fw * 6];
        int t = 0;
        for (int i = 0; i < fw; i++)
        {
            for (int j = 0; j < fw; j++)
            {
                int v0 = i * fn + j;
                int v1 = v0 + 1;
                int v2 = v0 + fn;
                int v3 = v2 + 1;
                indices[t++] = v0; indices[t++] = v2; indices[t++] = v1;
                indices[t++] = v1; indices[t++] = v2; indices[t++] = v3;
            }
        }

        var mesh = new Mesh { name = "___Heightmap m_renderMesh (subsurf)" };
        // Guard 4: factor=8 gives ~66k verts, over the UInt16 index limit (65535).
        // Vanilla defaults to UInt16; silently overflowing produces garbage indices.
        // UInt32 is safe and cheap at this scale.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        // ponytail (cliff-face UV): normals must exist BEFORE we decide the UVs, so
        // build verts+indices, RecalculateNormals, read them back, remap the steep
        // verts' UVs to world-space tiling, then set colors/UVs. Vanilla plan-view UV
        // is kept on flat/slope verts; only |n.y| < threshold verts get wall tiling.
        mesh.SetVertices(verts);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();

        bool cliff = TerrainSubsurfPlugin._cliffFaceUV.Value;
        float tile = TerrainSubsurfPlugin._cliffTileSize.Value;
        float steepLimit = TerrainSubsurfPlugin._cliffThreshold.Value;
        float band = TerrainSubsurfPlugin._cliffBlendBand.Value;
        if (cliff && tile > 0f)
        {
            Vector3[] normals = mesh.normals;
            Vector3 origin = hm.transform.position;
            float low = Mathf.Max(0f, steepLimit - band * 0.5f);
            float high = steepLimit + band * 0.5f;
            for (int i = 0; i < fn; i++)
            {
                for (int j = 0; j < fn; j++)
                {
                    int idx = i * fn + j;
                    float ny = Mathf.Abs(normals[idx].y);
                    // ponytail (UV seam fix): per-vertex hard snap left triangles
                    // straddling the threshold with verts in DIFFERENT UV spaces, so the
                    // GPU's linear UV interpolation sampled distant texels -> grey/green
                    // smudge around every tiled area. Blend the two UV spaces smoothly
                    // instead: w=0 (steep) -> pure wall UV, w=1 (flat) -> pure plan UV,
                    // SmoothStep in between. Grass-vs-stone is normal-based in the shader,
                    // so both spaces sample the SAME texture layer — the lerp is a clean
                    // crossfade of tiling position, not a texture swap.
                    float wgt = Mathf.SmoothStep(low, high, ny);
                    if (wgt >= 1f) continue; // fully plan-view; the array already holds plan UV
                    // Verified vs decompile: CalcVertex returns LOCAL space and
                    // m_meshFilter = GetComponent<MeshFilter>() on the same GameObject,
                    // so world = hm.transform.position + localVert.
                    float v = (origin.y + verts[idx].y) / tile;
                    float u = Mathf.Abs(normals[idx].x) > Mathf.Abs(normals[idx].z)
                        ? (origin.z + verts[idx].z) / tile   // wall faces ±X, along-wall = Z
                        : (origin.x + verts[idx].x) / tile;  // wall faces ±Z, along-wall = X
                    uvs[idx] = Vector2.Lerp(new Vector2(u, v), uvs[idx], wgt);
                }
            }
        }

        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        mf.mesh = mesh;
        HeightmapAccess.SetRenderMesh(hm, mesh);

        // ponytail: ConditionalWeakTable has no AddOrUpdate on net48 — remove+add.
        _cache.Remove(hm);
        _cache.Add(hm, new CacheEntry { Hash = hash, Width = w, Scale = scale });
    }

    private static float SampleLinear(List<float> h, int n, float x, float y, int w)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w), y1 = Mathf.Min(y0 + 1, w);
        float tx = x - x0, ty = y - y0;
        float a = h[y0 * n + x0], b = h[y0 * n + x1];
        float c = h[y1 * n + x0], d = h[y1 * n + x1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static float SampleCatmullRom(List<float> h, int n, float x, float y, int w)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float tx = x - x0, ty = y - y0;
        // ponytail: upgrade path — weights could be precomputed per tx/ty bucket.
        float[] wx = CatmullWeights(tx);
        float[] wy = CatmullWeights(ty);
        float sum = 0f;
        for (int m = -1; m <= 2; m++)
        {
            int sy = Mathf.Clamp(y0 + m, 0, w);
            float rowSum = 0f;
            for (int k = -1; k <= 2; k++)
            {
                int sx = Mathf.Clamp(x0 + k, 0, w);
                rowSum += wx[k + 1] * h[sy * n + sx];
            }
            sum += wy[m + 1] * rowSum;
        }
        return sum;
    }

    private static float[] CatmullWeights(float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return new float[]
        {
            -0.5f * t3 + t2 - 0.5f * t,
             1.5f * t3 - 2.5f * t2 + 1f,
            -1.5f * t3 + 2f * t2 + 0.5f * t,
             0.5f * t3 - 0.5f * t2
        };
    }

}
