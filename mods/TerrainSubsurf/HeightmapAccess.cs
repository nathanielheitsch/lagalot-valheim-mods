using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TerrainSubsurf;

/// <summary>
/// Cached reflection access to Heightmap internals (all private in assembly_valheim).
/// Height index convention (decompiled): m_heights[y * (m_width + 1) + x],
/// where x is the local X axis step and y the local Z axis step.
/// </summary>
internal static class HeightmapAccess
{
    private static FieldInfo? _heights;
    private static FieldInfo? _width;
    private static FieldInfo? _scale;
    private static FieldInfo? _meshFilter;
    private static FieldInfo? _renderMesh;
    private static MethodInfo? _rebuildRenderMesh;

    private static FieldInfo F(System.Reflection.FieldInfo? fi, string name)
    {
        if (fi != null) return fi;
        return typeof(Heightmap).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
               ?? throw new System.MissingMemberException($"Heightmap.{name} not found");
    }

    private static FieldInfo Heights => _heights ??= F(null, "m_heights");
    private static FieldInfo Width => _width ??= F(null, "m_width");
    private static FieldInfo Scale => _scale ??= F(null, "m_scale");
    private static FieldInfo MeshFilterF => _meshFilter ??= F(null, "m_meshFilter");
    private static FieldInfo RenderMeshF => _renderMesh ??= F(null, "m_renderMesh");

    public static System.Reflection.MethodInfo RebuildRenderMeshMethod =>
        _rebuildRenderMesh ??= typeof(Heightmap).GetMethod("RebuildRenderMesh",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new System.MissingMemberException("Heightmap.RebuildRenderMesh not found");

    public static List<float> GetHeights(Heightmap hm) => (List<float>)Heights.GetValue(hm)!;
    public static int GetWidth(Heightmap hm) => (int)Width.GetValue(hm)!;
    public static float GetScale(Heightmap hm) => (float)Scale.GetValue(hm)!;
    public static MeshFilter GetMeshFilter(Heightmap hm) => (MeshFilter)MeshFilterF.GetValue(hm)!;
    public static Mesh GetRenderMesh(Heightmap hm) => (Mesh)RenderMeshF.GetValue(hm)!;
    public static void SetRenderMesh(Heightmap hm, Mesh mesh) => RenderMeshF.SetValue(hm, mesh);

    private static MethodInfo? _biomeColor;

    /// <summary>
    /// Vanilla's private instance GetBiomeColor(float ix, float iy). Encapsulates the
    /// m_cornerBiomes lerp AND AltBiome terrain-texture overrides, so calling it keeps
    /// our colors byte-for-byte identical to vanilla (no formula drift).
    /// </summary>
    public static Color32 GetBiomeColor(Heightmap hm, float ix, float iy)
    {
        _biomeColor ??= typeof(Heightmap).GetMethod("GetBiomeColor",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(float), typeof(float) }, null)
            ?? throw new System.MissingMemberException("Heightmap.GetBiomeColor(float,float) not found");
        object boxed = hm;
        return (Color32)(_biomeColor.Invoke(boxed, new object[] { ix, iy }) ?? default(Color32));
    }
}
