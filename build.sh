#!/bin/bash
# Builds WildCards.dll with the built-in .NET Framework C# 5 compiler, then makes the r2modman/Thunderstore zip.
#   bash build.sh                 -> full build + package
#   CARD=Portal bash build.sh     -> isolated compile check: core + old cards + ONLY src/Cards/New/Portal.cs
#                                    (output bin/test/Portal.dll, no packaging) so parallel card work doesn't collide.
cd "$(dirname "$0")"
ROOT="$(cygpath -w "$(pwd)")"
# Override with ROUNDS_MANAGED / BEPINEX_DIR (an r2modman profile's BepInEx folder with UnboundLib + MMHook installed).
M="${ROUNDS_MANAGED:-C:\\Program Files (x86)\\Steam\\steamapps\\common\\ROUNDS\\ROUNDS_Data\\Managed}"
B="${BEPINEX_DIR:-$(cygpath -w "$APPDATA")\\r2modmanPlus-local\\ROUNDS\\profiles\\WildCards 1v1\\BepInEx}"
mkdir -p bin/test
if [ -n "$CARD" ]; then OUT="$ROOT\\bin\\test\\$CARD.dll"; RSP=bin/test/$CARD.rsp; LOG=bin/test/$CARD.log
else OUT="$ROOT\\bin\\WildCards.dll"; RSP=bin/build.rsp; LOG=bin/build.log; fi

winpath() { cygpath -w "$(pwd)/$1"; }

{
  echo "-nologo -nostdlib -target:library -optimize+ -warn:1"
  echo "\"-out:$OUT\""
  for r in mscorlib System System.Core netstandard Assembly-CSharp Assembly-CSharp-firstpass UnityEngine UnityEngine.CoreModule UnityEngine.Physics2DModule UnityEngine.PhysicsModule UnityEngine.ParticleSystemModule UnityEngine.AudioModule UnityEngine.UI UnityEngine.UIModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule UnityEngine.ImageConversionModule Unity.TextMeshPro PhotonUnityNetworking PhotonRealtime Photon3Unity3D SonigonAudioEngine.Runtime; do
    echo "\"-r:$M\\$r.dll\""
  done
  echo "\"-r:$B\\core\\BepInEx.dll\""
  echo "\"-r:$B\\core\\0Harmony.dll\""
  echo "\"-r:$B\\plugins\\willis81808-UnboundLib\\UnboundLib.dll\""
  echo "\"-r:$B\\plugins\\willis81808-MMHook\\MMHOOK_Assembly-CSharp.dll\""
  for f in src/*.cs src/Cards/*.cs; do echo "\"$(winpath "$f")\""; done
  if [ -n "$CARD" ]; then
    echo "\"$(winpath "src/Cards/New/$CARD.cs")\""
  else
    for f in src/Cards/New/*.cs; do [ -f "$f" ] && echo "\"$(winpath "$f")\""; done
  fi
  for f in art/png/*.png; do [ -f "$f" ] || continue; b="$(basename "$f" .png)"; case "$b" in _*) continue;; esac; echo "\"-resource:$(winpath "$f"),WildCards.Art.$b.png\""; done
} > "$RSP"

MSYS_NO_PATHCONV=1 /c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -noconfig "@$(cygpath -w "$RSP")" > "$LOG" 2>&1
STATUS=$?
iconv -f cp850 -t utf-8 "$LOG" | grep -v "warning CS1701\|warning CS1702"
[ $STATUS -eq 0 ] || { echo "BUILD FAILED"; exit 1; }
echo "BUILD OK -> $OUT"
[ -n "$CARD" ] && exit 0

# Package for r2modman ("Import local mod") / Thunderstore layout.
rm -rf bin/pkg && mkdir -p bin/pkg
cp package/manifest.json package/README.md package/icon.png bin/pkg/
cp bin/WildCards.dll bin/pkg/
VER=$(grep -o '"version_number": *"[^"]*"' package/manifest.json | grep -o '[0-9.]*[0-9]')
ZIP="WildCards-$VER.zip"
rm -f bin/WildCards-*.zip
powershell.exe -NoProfile -Command "Compress-Archive -Path '$ROOT\\bin\\pkg\\*' -DestinationPath '$ROOT\\bin\\$ZIP' -Force" && echo "PACKAGED -> bin/$ZIP"
