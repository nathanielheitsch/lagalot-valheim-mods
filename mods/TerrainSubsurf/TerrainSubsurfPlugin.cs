using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TerrainSubsurf;

[BepInPlugin(GUID, NAME, VERSION)]
public class TerrainSubsurfPlugin : BaseUnityPlugin
{
    public const string GUID = "lagalot.terrainsubsurf";
    public const string NAME = "TerrainSubsurf";
    public const string VERSION = "0.1.0";

    internal static new ManualLogSource Logger = null!;

    internal static ConfigEntry<bool> _enabled = null!;
    internal static ConfigEntry<int> _factor = null!;
    internal static ConfigEntry<string> _mode = null!;

    private void Awake()
    {
        Logger = base.Logger;
        _enabled = Config.Bind("General", "Enabled", true, "Master toggle for terrain render smoothing.");
        _factor = Config.Bind("General", "SubdivisionFactor", 4,
            new ConfigDescription("Render-mesh resolution multiplier. 1 = off (vanilla).",
                new AcceptableValueList<int>(1, 2, 4, 8)));
        _mode = Config.Bind("General", "SmoothingMode", "CatmullRom",
            new ConfigDescription("Height interpolation for new vertices.",
                new AcceptableValueList<string>("Linear", "CatmullRom")));

        Logger.LogInfo($"{NAME} {VERSION} | Enabled={_enabled.Value} Factor={_factor.Value} Mode={_mode.Value}");

        var harmony = new Harmony(GUID);
        harmony.PatchAll(typeof(Patches));
        Logger.LogInfo("Harmony patched Heightmap.RebuildRenderMesh");
    }
}

internal static class Patches
{
    [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
    [HarmonyPostfix]
    private static void Postfix(Heightmap __instance)
    {
        var cfg = TerrainSubsurfPlugin._enabled;
        try
        {
            if (!cfg.Value || _factorVal() <= 1) return;
            // Skip distant-LOD heightmaps: subdividing far terrain wastes GPU and can
            // fight the LOD swap. Near terrain is where sharp edges actually read.
            if (__instance.IsDistantLod) return;
            Subdivide(__instance, _factorVal(), _modeVal() == "CatmullRom");
        }
        catch (Exception e)
        {
            // Never break the vanilla render path.
            TerrainSubsurfPlugin.Logger?.LogWarning($"TerrainSubsurf postfix failed (vanilla mesh kept): {e}");
        }
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

        int n = w + 1;               // vanilla grid
        int fw = w * factor;         // fine grid edge (in vanilla steps)
        int fn = fw + 1;             // fine vertex count per side
        float half = (float)w * scale * 0.5f;

        var verts = new Vector3[fn * fn];
        var uvs = new Vector2[fn * fn];
        var colors = new Color32[fn * fn];

        // Vanilla color array from the just-built render mesh (corner-biome interpolation).
        var vanillaColors = renderMesh.colors32;
        var vanillaUvs = renderMesh.uv;

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

                // Colors: bilinear interp of the vanilla color array.
                colors[idx] = BilinearColor(vanillaColors, n, x, y, w);
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
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        mf.mesh = mesh;
        HeightmapAccess.SetRenderMesh(hm, mesh);
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

    private static Color32 BilinearColor(Color32[] c, int n, float x, float y, int w)
    {
        if (c == null || c.Length == 0) return new Color32(0, 0, 0, 0);
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w), y1 = Mathf.Min(y0 + 1, w);
        float tx = x - x0, ty = y - y0;
        var a = c[y0 * n + x0]; var b = c[y0 * n + x1];
        var d = c[y1 * n + x0]; var e = c[y1 * n + x1];
        return Color32.Lerp(Color32.Lerp(a, b, tx), Color32.Lerp(d, e, tx), ty);
    }
}
