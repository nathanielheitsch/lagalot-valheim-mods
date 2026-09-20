# LagalotTemplate

Copyable starter mod. Rename the folder, `AssemblyName`/`RootNamespace`/`GUID`/`NAME` in the `.csproj` + `Plugin.cs`, and you have a new mod.

## Build

```bash
dotnet build mods/LagalotTemplate
```

Output: `mods/LagalotTemplate/bin/Debug/LagalotTemplate.dll` → drop into the game's `BepInEx/plugins/`.

## Jotunn entry points (for custom content)

- `ItemManager` — custom items, pieces, recipes
- `PieceManager` — custom build pieces
- `PrefabManager` — custom prefabs / creatures
- `ZoneManager` — custom locations, vegvisirs
- `Localization` — custom strings / translations
- `ConfigManager` — in-game config UI (also syncs to clients)

## Player controls / behaviors

These aren't Jotunn content — they're Harmony patches on `Player` (and related) methods in `assembly_valheim.dll`. Browse the vanilla code with a decompiler (ILSpy/dnSpy) to find the method to patch.
