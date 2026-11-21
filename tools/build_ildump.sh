#!/bin/bash
# Builds dev/ILDump.exe: prints the IL of game methods (read the real game code without a decompiler).
#   dev/ILDump.exe "C:\Program Files (x86)\Steam\steamapps\common\ROUNDS\ROUNDS_Data\Managed" PlayerJump:Jump Gravity:FixedUpdate
cd "$(dirname "$0")/.."
mkdir -p dev
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -nologo -out:"$(cygpath -w dev/ILDump.exe)" "$(cygpath -w tools/ildump/ILDump.cs)" && echo "built dev/ILDump.exe"
