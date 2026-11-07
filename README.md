# Wolvy's Valheim mods

BepInEx mods for [Valheim](https://www.valheimgame.com/) by **Wolvyreen**, made with Claude (Anthropic's AI).

Each mod has its own folder with its source code, tests, build tools, README and changelog.

| Mod | What it does | Download |
|---|---|---|
| [Wolvy's Cross Beams](WolvysCrossBeams/) | Build X and + crosses from beams of the same type: the second beam now snaps across the first instead of being refused. Real duplicates are still blocked. Client-side only. | Thunderstore · Nexus *(links coming)* |

More mods will be added here as they're released.

## Installing

Use a mod manager (r2modman or Thunderstore Mod Manager) and install from Thunderstore, or download from Nexus and follow the instructions on the mod's page. All mods need [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

## Building from source

Each mod's `tools/build.sh` builds it with Mono (`mcs`) against your own game's DLLs, and `tests/run-tests.sh` runs its tests. See the comments at the top of those scripts for what they need. There's also a `.csproj` for building with `dotnet` / Visual Studio (set `ValheimPath` and `BepInExPath` if your folders differ).

## Bugs and ideas

Open an [Issue](../../issues) and say which mod and version you're using. A copy of `BepInEx/LogOutput.log` helps a lot.

## Made with AI

These mods were made with Claude. Wolvyreen decides what each mod should do, finds the problems and tests everything in game. Claude reads the game code, and writes the mod code and documentation. The Thunderstore and Nexus pages are marked as AI-generated.

## Licence

[MIT](LICENSE): free to use, change and share, as long as the copyright notice stays with it.
