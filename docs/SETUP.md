# Development Environment Setup

Walkthrough of the setup described in the
[Valheim-Modding wiki: Setting Up Mod Development Environment](https://github.com/Valheim-Modding/Wiki/wiki/Setting-Up-Mod-Development-Environment).

## Situation

This repo targets **Situation 3: custom content** — meshes, textures, player
controls, and behavior modifications. That means we use **Jotunn** as the
content-injection library, on top of **BepInEx** (the mod loader) + **HarmonyX**
(patching).

## Toolchain

| Component | What | How |
| --- | --- | --- |
| .NET SDK | builds the mod `.dll` (targets .NET Framework 4.8) | `brew install --cask dotnet-sdk` |
| VS Code + C# Dev Kit | editor / IntelliSense | `code --install-extension ms-dotnettools.csdevkit` |
| `Microsoft.NETFramework.ReferenceAssemblies` | lets net48 build on macOS without the .NET Framework runtime | NuGet, auto-restored |
| BepInEx Pack (Valheim) | mod loader + HarmonyX, referenced by mods | `scripts/fetch-libs.sh` |
| Valheim game assemblies | `assembly_*.dll` from the game install | `scripts/fetch-libs.sh` |
| Jotunn | content-injection lib (items, pieces, creatures, configs) | NuGet PackageReference |

## Build target

Mods target `net48` (the .NET Framework 4.8 the game runs on). On macOS we
build the *library* with reference assemblies — the DLL never runs here; it
loads into the game (Windows, or the game under Proton/CrossOver).

## Reference DLLs (the `libs/` folder)

Three categories, all gitignored and fetched by `scripts/fetch-libs.sh`:

1. **BepInEx core** — `BepInEx/core/`: `BepInEx.dll`, `0Harmony.dll`, etc.
   Extracted from the denikson BepInExPack_Valheim thunderstore zip.
2. **Valheim game assemblies** — `valheim_Data/Managed/assembly_*.dll`
   (e.g. `assembly_valheim.dll`, `assembly_game.dll`). Copied from the local
   game install.
3. **Jotunn** — pulled automatically as a NuGet `PackageReference` (no manual
   copy).

See `../scripts/fetch-libs.sh` for the exact file list.

## View vanilla code (optional but recommended)

To inspect decompiled vanilla classes/methods, use a decompiler:

- [ILSpy](https://github.com/icsharpcode/ILSpy/releases) (cross-platform,
  ILSpyIC on macOS works for browsing)
- [dnSpy](https://github.com/dnSpyEx/dnSpy/releases) (Windows-oriented)

Open `assembly_valheim.dll` (in `libs/`) to browse vanilla code.

## View game assets

- In-game: [UnityExplorer for Valheim](https://thunderstore.io/c/valheim/p/ValheimModding/UnityExplorer/)
  (install as a mod) — fast for inspecting prefabs/values live.
- Offline / for editing: a **game rip** (Unity project export). See the
  [Valheim Unity Project Guide](https://github.com/Valheim-Modding/Wiki/wiki/Valheim-Unity-Project-Guide).
  This is a multi-hour step; do it only if you're authoring/editing assets.
