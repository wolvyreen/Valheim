#!/bin/bash
# Usage (from the mod folder, after tools/build.sh): sh tests/run-tests.sh <BepInEx folder> <Managed folder>
# Runs the duplicate rule (Plugin.IsDuplicate) from the built DLL against beams, poles and floors.
set -e
T=$(mktemp -d); CORE="$1/core"
cp out/WolvysCrossBeams.dll "$CORE"/BepInEx.dll "$CORE"/0Harmony.dll "$2"/netstandard.dll "$2"/UnityEngine.dll "$2"/UnityEngine.CoreModule.dll "$T"/
cp tests/Test.cs "$T"/T.cs
(cd "$T" && mcs T.cs -r:UnityEngine.CoreModule.dll -r:netstandard.dll -out:T.exe >/dev/null && mono T.exe)
