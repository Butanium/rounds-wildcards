#!/bin/bash
# Usage: bash render.sh <name>   -> renders art/svg/<name>.svg to art/png/<name>.png (1100x865, transparent)
cd "$(dirname "$0")"
N="$1"
SVG="$(cygpath -m "$(pwd)/svg/$N.svg")"
PNG="$(cygpath -w "$(pwd)/png/$N.png")"
HTML="$(pwd)/png/_$N.html"
printf '<html><body style="margin:0;background:transparent"><img src="file:///%s" width="1100" height="865"></body></html>' "$SVG" > "$HTML"
rm -f "png/$N.png"
"/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" --headless=old --disable-gpu --hide-scrollbars --default-background-color=00000000 --window-size=1100,865 --screenshot="$PNG" "file:///$(cygpath -m "$HTML")" >/dev/null 2>&1
rm -f "$HTML"
[ -f "png/$N.png" ] && echo "RENDERED png/$N.png" || { echo "RENDER FAILED"; exit 1; }
