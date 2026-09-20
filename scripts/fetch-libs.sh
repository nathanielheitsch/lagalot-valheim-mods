#!/usr/bin/env bash
# Fetch BepInEx Pack + copy Valheim game assemblies into libs/.
# Run once after Valheim is installed. Re-run anytime to refresh.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

LIBS="$REPO_ROOT/libs"
BEPINEX_VER="5.4.2350"
BEPINEX_URL="https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/${BEPINEX_VER}/"

# --- locate Valheim install ---
VALHEIM=""
if [[ -f "$REPO_ROOT/.valheim-path" ]]; then
  VALHEIM="$(cat "$REPO_ROOT/.valheim-path" | tr -d '[:space:]')"
fi
if [[ -z "$VALHEIM" ]]; then
  for c in \
    "$HOME/Library/Application Support/Steam/steamapps/common/Valheim" \
    "$HOME/.steam/steam/steamapps/common/Valheim" \
    "$HOME/Library/Application Support/Steam/steamapps/common/valheim"; do
    [[ -d "$c" ]] && VALHEIM="$c" && break
  done
fi

if [[ -z "$VALHEIM" || ! -d "$VALHEIM" ]]; then
  echo "ERROR: could not find Valheim install."
  echo "Create .valheim-path with the path, e.g.:"
  echo "  echo \"\$HOME/Library/Application Support/Steam/steamapps/common/Valheim\" > .valheim-path"
  exit 1
fi
echo "Valheim install: $VALHEIM"

# Game assemblies live inside the app bundle on macOS.
MANAGED=""
for m in \
  "$VALHEIM/valheim_Data/Managed" \
  "$VALHEIM/valheim.app/Contents/Resources/Data/Managed" \
  "$VALHEIM/valheim.app/Contents/Resources/valheim_Data/Managed" \
  "$VALHEIM/valheim.app/Contents/Data/Managed"; do
  [[ -d "$m" ]] && MANAGED="$m" && break
done
if [[ -z "$MANAGED" ]]; then
  echo "ERROR: could not find valheim_Data/Managed under $VALHEIM"
  echo "Searched valheim_Data/Managed and valheim.app/Contents/.../Managed."
  exit 1
fi
echo "Managed assemblies: $MANAGED"

mkdir -p "$LIBS/bepinex" "$LIBS/game"

# --- BepInEx core ---
if [[ ! -f "$LIBS/bepinex/BepInEx.dll" ]]; then
  echo "Fetching BepInEx Pack_Valheim $BEPINEX_VER ..."
  TMP="$(mktemp -d)"
  curl -fL -o "$TMP/bep.zip" "$BEPINEX_URL"
  unzip -q "$TMP/bep.zip" -d "$TMP/out"
  # The pack extracts to BepInExPack_Valheim/BepInEx/core/*.dll.
  cp "$TMP/out"/BepInExPack_Valheim/BepInEx/core/*.dll "$LIBS/bepinex/"
  rm -rf "$TMP"
  echo "BepInEx core -> $LIBS/bepinex ($(ls "$LIBS/bepinex" | wc -l | tr -d ' ') dlls)"
else
  echo "BepInEx core already present in libs/bepinex (skipping)."
fi

# --- Valheim game assemblies (assembly_*.dll) ---
echo "Copying assembly_*.dll from game ..."
copied=0
for f in "$MANAGED"/assembly_*.dll; do
  [[ -f "$f" ]] || continue
  cp "$f" "$LIBS/game/"
  copied=$((copied+1))
done
# A few non-assembly_ game DLLs mods commonly need:
for extra in UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.AssetBundleModule.dll; do
  [[ -f "$MANAGED/$extra" ]] && cp "$MANAGED/$extra" "$LIBS/game/" && copied=$((copied+1))
done
echo "Game assemblies -> $LIBS/game ($copied copied)"

echo
echo "Done. libs/ now has:"
echo "  bepinex/  $(ls "$LIBS/bepinex" | wc -l | tr -d ' ') dlls"
echo "  game/     $(ls "$LIBS/game" | wc -l | tr -d ' ') dlls"
echo
echo "Next: dotnet build mods/<ModName>"
