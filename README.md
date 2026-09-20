# lagalot-valheim-mods

Monorepo for [Valheim](https://www.valheimgame.com/) mods by lagalot. Custom content — meshes, textures, player controls, and behavior tweaks — built on [Jotunn](https://github.com/Valheim-Modding/Jotunn) + [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

Each mod lives in its own folder under `mods/` and produces one `.dll` for the game's `BepInEx/plugins/` directory.

## Prerequisites (macOS, one-time)

```bash
# 1. .NET SDK (arm64) — installer needs sudo, run in a real terminal:
brew install --cask dotnet-sdk
dotnet --version          # confirm

# 2. VS Code C# tooling (already installed):
code --install-extension ms-dotnettools.csdevkit   # pulls ms-dotnettools.csharp too

# 3. Valheim installed via Steam (provides the vanilla game assemblies).
#    Default Mac path: ~/Library/Application Support/Steam/steamapps/common/Valheim
```

## First-time setup

```bash
# Fetch BepInEx pack + copy the game's assembly_*.dll into libs/
./scripts/fetch-libs.sh
```

The script auto-detects the Valheim install. To override, create `.valheim-path`:

```bash
echo "/path/to/Valheim" > .valheim-path
```

## Build a mod

```bash
dotnet build mods/<ModName>
# or build everything:
dotnet build
```

Built DLLs land in `mods/<ModName>/bin/Debug/net48/`. Copy to the game's `BepInEx/plugins/` (or use the deploy script — TODO once first mod exists).

## Layout

```
mods/          # one folder per mod, each a .csproj that outputs a .dll
libs/          # gitignored: BepInEx core + Valheim game assemblies (fetched, not committed)
scripts/       # fetch-libs.sh, deploy helpers
docs/          # setup notes, references
Directory.Build.props  # shared MSBuild props across all mods
```

See `docs/SETUP.md` for the full environment setup walkthrough.
