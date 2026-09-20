# TerrainSubsurf

Render-only smoothing of Valheim's terrain so the ground has fewer sharp edges. The actual terrain collider is untouched — player walking, building placement, and physics are exactly vanilla. This mod only subdivides the **render mesh** and interpolates heights with a spline for a smoother, more realistic surface.

Pairs with any terrain texture/shader mod: this mod touches **geometry only**, never the material or shader, so whatever texture pack you install renders on the smoother surface unchanged.

## How it works

Valheim's terrain is a custom `Heightmap` MonoBehaviour with two separate meshes: a collision mesh (→ `MeshCollider`) and a render mesh (→ `MeshFilter`). A Harmony postfix on `Heightmap.RebuildRenderMesh()` rebuilds the render mesh at higher resolution (× the configured factor), sampling the height array with Catmull-Rom (curved, realistic) or linear interpolation. Because the postfix fires after every `Poke()`→`Regenerate()` (digging, hoeing, building), the smoother surface re-applies automatically whenever the terrain changes.

## Config (BepInEx config file)

| Key | Default | Values | Meaning |
| --- | --- | --- | --- |
| `Enabled` | `true` | bool | Master toggle. |
| `SubdivisionFactor` | `2` | 1 / 2 / 4 / 8 | Render-mesh resolution multiplier. 1 = off (vanilla). 4/8 are heavy GPU/memory; 2 is the default sweet spot. |
| `SmoothingMode` | `CatmullRom` | Linear / CatmullRom | Height interpolation for new vertices. Catmull-Rom curves the surface; Linear makes smaller flat facets. |
| `CullDistance` | `0` | float (world units) | Only smooth patches within this distance of the player. 0 = smooth ALL loaded patches (recommended — the whole world renders better). Set >0 for a perf trade-off (far patches stay vanilla; a proximity tick re-smooths them as you approach). |
| `FlatEpsilon` | `0.05` | float (world units) | Skip smoothing patches whose height range (max-min) is below this — flat terrain stays at vanilla cost. |
| `RebuildAllTerrain` | `F6` | keybind | Press in-game to clear the cache and rebuild all near terrain immediately (debug). |

## Build

```bash
dotnet build mods/TerrainSubsurf
# → mods/TerrainSubsurf/bin/Debug/TerrainSubsurf.dll
```

Copy `TerrainSubsurf.dll` into the game's `BepInEx/plugins/`.

## Scope / non-goals

- v1: near terrain only. Distant-LOD heightmaps are skipped (subdividing far terrain wastes GPU and fights the LOD swap).
- Rocks and water are separate systems — not touched.
- Client-side only. No server install required; safe in multiplayer (purely cosmetic).

## Known limitations

- **Zone-border normals:** each patch recalculates its own normals, so edge normals won't match the neighbor patch's — may show as a subtle lighting seam along zone borders. Not yet verified in-game.
- **Steep-edge float/sink:** since the collider stays on the original mesh, characters may appear to float or sink a hair on steep edges where the smoothed surface diverges from the collider. This is the expected trade-off of render-only smoothing.
- **Performance at ×8:** ~65k verts / 130k tris per near patch, ~20–40 near patches → ~2.6M tris. Rebuilds only happen on terrain load/change (not per frame), so CPU is fine, but GPU/memory at ×8 is real. Use ×4.
- **Not yet tested in-game.** Build proves compilation; live load pending.
