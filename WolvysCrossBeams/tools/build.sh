#!/bin/bash
# Build Wolvy's Cross Beams in the Linux workspace (Mono mcs) and add the .pdb ScriptEngine needs.
# Usage (from this folder): tools/build.sh <BepInEx folder with core/> <Valheim valheim_Data/Managed folder>
#   Compiled against the game's UnityEngine.dll, UnityEngine.CoreModule.dll and netstandard.dll.
#   Valheim itself is reached by reflection, so assembly_valheim.dll is not needed.
# Output: out/WolvysCrossBeams.dll + out/WolvysCrossBeams.pdb  (this DLL is the one to deploy AND to zip)
set -e
CORE="$1/core"; MANAGED="$2"
rm -rf build out; mkdir -p build out
UNITY="-r:$MANAGED/UnityEngine.dll -r:$MANAGED/UnityEngine.CoreModule.dll -r:$MANAGED/netstandard.dll"
mcs -target:library -langversion:7.2 -optimize+ -debug- -out:build/WolvysCrossBeams.dll \
  $UNITY -r:"$CORE/BepInEx.dll" -r:"$CORE/0Harmony.dll" -r:System.Core.dll src/*.cs
# ScriptEngine (r11.2) refuses a DLL without a matching portable .pdb: rewrite the DLL with Cecil and write one
mcs -r:"$CORE/Mono.Cecil.dll" tools/AddPdb.cs -out:build/AddPdb.exe
cp "$CORE"/Mono.Cecil*.dll build/
mono build/AddPdb.exe build/WolvysCrossBeams.dll out/WolvysCrossBeams.dll "$MANAGED" "$CORE"
python3 tools/pdbcheck.py out/WolvysCrossBeams.dll out/WolvysCrossBeams.pdb
md5sum out/WolvysCrossBeams.dll out/WolvysCrossBeams.pdb
