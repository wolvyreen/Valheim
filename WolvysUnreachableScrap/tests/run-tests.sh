#!/bin/bash
# Wolvy's Unreachable Scrap tests: this mod only (no other Wolvy's mod is built, loaded or faked here).
# Usage (from the mod folder, after tools/build.sh): bash tests/run-tests.sh <BepInEx folder> <valheim_Data/Managed folder>
# Loads out/WolvysUnreachableScrap.dll and the game's own DLLs, then checks the mod's pure logic and that every game
# method / field the mod hooks still exists in this game version. Exit code = number of failed checks.
set -e
T=$(mktemp -d); CORE="$1/core"; MANAGED="$2"
cp out/WolvysUnreachableScrap.dll "$CORE"/BepInEx.dll "$CORE"/0Harmony.dll "$CORE"/Mono.Cecil*.dll "$CORE"/MonoMod.*.dll "$T"/
cp "$MANAGED"/*.dll "$T"/
cp tests/Test.cs "$T"/T.cs
(cd "$T" && mcs T.cs -r:BepInEx.dll -r:0Harmony.dll -r:assembly_valheim.dll -r:UnityEngine.CoreModule.dll -r:UnityEngine.PhysicsModule.dll -r:netstandard.dll -out:T.exe >/dev/null && mono T.exe)
