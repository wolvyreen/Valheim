# Wolvy's Valheim mods

BepInEx mods for [Valheim](https://www.valheimgame.com/) by **Wolvyreen**, made with Claude (Anthropic's AI).

Each mod has its own folder with its source code, tests, build tools, README and changelog.

| Mod | What it does | Download |
|---|---|---|
| [Wolvy's Cross Beams](WolvysCrossBeams/) | Build X and + crosses from beams of the same type: the second beam now snaps across the first instead of being refused. Real duplicates are still blocked. Client-side only. | Thunderstore · Nexus *(links coming)* |

More mods will be added here as they're released.

## Installing

Every mod needs BepInEx for Valheim ([BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)). Pick whichever way you already use:

- **r2modman / Thunderstore Mod Manager:** install the mod from Thunderstore. BepInEx is installed for you.
- **Vortex or Mod Organizer 2:** download the mod from Nexus Mods with "Mod Manager Download". Your mod manager needs Valheim's BepInEx set up first (Vortex's Valheim support handles this; in Mod Organizer 2 use its Valheim support plugin and install BepInExPack Valheim as a mod).
- **Manual:** download from Nexus Mods and extract the zip into your Valheim folder (the one with valheim.exe). The zip already has the `BepInEx/plugins/...` folders.

Each mod's page also has its own install notes.

## Building from source

Each mod's `tools/build.sh` builds it with Mono (`mcs`) against your own game's DLLs, and `tests/run-tests.sh` runs its tests. See the comments at the top of those scripts for what they need. There's also a `.csproj` for building with `dotnet` / Visual Studio (set `ValheimPath` and `BepInExPath` if your folders differ).

## Bugs and ideas

Open an [Issue](../../issues) and say which mod and version you're using. A copy of `BepInEx/LogOutput.log` helps a lot.

## Made with AI

These mods were made with Claude. Wolvyreen decides what each mod should do, finds the problems and tests everything in game. Claude reads the game code, and writes the mod code and documentation. The Thunderstore and Nexus pages are marked as AI-generated.

## Licence

[MIT](LICENSE): free to use, change and share, as long as the copyright notice stays with it.
