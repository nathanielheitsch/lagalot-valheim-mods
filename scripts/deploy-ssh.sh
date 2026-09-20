#!/usr/bin/env bash
# Deploy built mod DLL(s) to the Valheim game install over SSH.
# Usage: ./scripts/deploy-ssh.sh [ssh-host] [mod-name...]
# Defaults: ssh-host=rhino, all built mod DLLs.
set -euo pipefail

HOST="${1:-rhino}"
shift || true

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Collect DLLs to deploy (default: every mods/*/bin/Debug/*.dll).
DLLS=()
if [[ $# -gt 0 ]]; then
  for m in "$@"; do
    DLLS+=("mods/$m/bin/Debug/$m.dll")
  done
else
  for f in mods/*/bin/Debug/*.dll; do
    [[ -f "$f" ]] && DLLS+=("$f")
  done
fi

if [[ ${#DLLS[@]} -eq 0 ]]; then
  echo "No built DLLs found. Run 'dotnet build mods/<Name>' first."
  exit 1
fi

echo "Deploying ${#DLLS[@]} DLL(s) to $HOST:"
for d in "${DLLS[@]}"; do echo "  $d"; done

# Upload to the SSH user's home, then move into the game's plugins folder.
# Valheim install path resolved on the remote (edit if different).
REMOTE_PLUGINS="D:\\SteamLibrary\\steamapps\\common\\Valheim\\BepInEx\\plugins"

for d in "${DLLS[@]}"; do
  name="$(basename "$d")"
  echo "→ $name"
  scp -o BatchMode=yes "$d" "$HOST:$name"
done

# Move them on the remote via a small PowerShell block.
ps='$ErrorActionPreference="Stop"; $dst="'"$REMOTE_PLUGINS"'"; foreach ($n in @('"$(printf "'%s' " "${DLLS[@]##*/}")"')) { $src="$env:USERPROFILE\$n"; Copy-Item $src (Join-Path $dst $n) -Force; Write-Output ("installed: " + $n) }'
ssh -o BatchMode=yes "$HOST" "powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ps\"" 2>&1 | grep -vE "WARNING|session may|server may"

echo "Done. Launch the game to test."
