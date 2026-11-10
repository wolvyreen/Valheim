#!/bin/bash
# Build Wolvy's Unreachable Scrap in the Linux workspace (Mono mcs) and add the .pdb ScriptEngine needs.
# Usage (from this folder): tools/build.sh <BepInEx folder with core/> <Valheim valheim_Data/Managed folder>
#   Compiled against assembly_valheim.dll, assembly_utils.dll, UnityEngine(.CoreModule/.PhysicsModule).dll, SoftReferenceableAssets.dll (ZNetView needs it), the game's netstandard.dll and,
#   for the mod hub window, UnityEngine.UI, UIModule, Unity.TextMeshPro, InputLegacyModule, ImageConversionModule, TextRenderingModule.
# Output: out/WolvysUnreachableScrap.dll + out/WolvysUnreachableScrap.pdb  (this DLL is the one to deploy AND to zip)
set -e
CORE="$1/core"; MANAGED="$2"
rm -rf build out; mkdir -p build out
REFS="-r:$MANAGED/assembly_valheim.dll -r:$MANAGED/assembly_utils.dll -r:$MANAGED/UnityEngine.dll -r:$MANAGED/UnityEngine.CoreModule.dll -r:$MANAGED/UnityEngine.PhysicsModule.dll -r:$MANAGED/netstandard.dll -r:$MANAGED/SoftReferenceableAssets.dll"
# the mod hub window (src/ModHubUi) also needs Unity UI, TextMeshPro, input and image loading
REFS="$REFS -r:$MANAGED/UnityEngine.UI.dll -r:$MANAGED/UnityEngine.UIModule.dll -r:$MANAGED/Unity.TextMeshPro.dll -r:$MANAGED/UnityEngine.InputLegacyModule.dll -r:$MANAGED/UnityEngine.ImageConversionModule.dll -r:$MANAGED/UnityEngine.TextRenderingModule.dll -r:$MANAGED/UnityEngine.IMGUIModule.dll"
mcs -target:library -langversion:7.2 -optimize+ -debug- -out:build/WolvysUnreachableScrap.dll \
  $REFS -r:"$CORE/BepInEx.dll" -r:"$CORE/0Harmony.dll" -r:System.Core.dll src/*.cs src/ModHubUi/*.cs
# ScriptEngine (r11.2) refuses a DLL without a matching portable .pdb: rewrite the DLL with Cecil and write one
mcs -r:"$CORE/Mono.Cecil.dll" tools/AddPdb.cs -out:build/AddPdb.exe
cp "$CORE"/Mono.Cecil*.dll build/
mono build/AddPdb.exe build/WolvysUnreachableScrap.dll out/WolvysUnreachableScrap.dll "$MANAGED" "$CORE"
python3 tools/pdbcheck.py out/WolvysUnreachableScrap.dll out/WolvysUnreachableScrap.pdb
md5sum out/WolvysUnreachableScrap.dll out/WolvysUnreachableScrap.pdb
