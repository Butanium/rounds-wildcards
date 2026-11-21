#!/bin/bash
# One-shot release: bump version, build, commit, push, install into the profile (if the game is closed), GitHub release.
#   bash release.sh 1.4.7 "What changed (one line, used for commit + release notes)"
#   NO_GH=1 bash release.sh 1.4.7 "..."   -> skip the GitHub release
set -e
cd "$(dirname "$0")"
VER="$1"; MSG="$2"
[ -n "$VER" ] && [ -n "$MSG" ] || { echo "usage: bash release.sh <version> \"<message>\""; exit 1; }

sed -i "s/public const string Version = \"[0-9.]*\";/public const string Version = \"$VER\";/" src/Plugin.cs
sed -i "s/\"version_number\": \"[0-9.]*\"/\"version_number\": \"$VER\"/" package/manifest.json
bash build.sh | tail -2

git add -A
git commit -q -m "$MSG" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -q

if tasklist //FI "IMAGENAME eq Rounds.exe" | grep -qi "Rounds.exe"; then
  echo "ROUNDS is running: not installing (DLL is locked). Run install.ps1 after closing the game."
else
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w install.ps1)"
fi

if [ -z "$NO_GH" ]; then
  gh release create "v$VER" "bin/WildCards-$VER.zip" --title "WildCards $VER" \
    --notes "$MSG

**Install:** r2modman → Settings → Import local mod → \`WildCards-$VER.zip\`. Everyone in the lobby needs the same version."
fi
echo "released $VER"
