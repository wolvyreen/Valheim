# Wolvy's Valheim mods

These are the Valheim mods I've made. Each one fixes or adds something I wanted while playing. They're BepInEx mods, and I make them with the help of Claude (Anthropic's AI).

Every mod has its own folder here with the source code, tests, build scripts, README and changelog.

| Mod | What it does | Download |
|---|---|---|
| [Wolvy's Cross Beams](WolvysCrossBeams/) | Lets you snap two angled beams into an X. Vanilla refuses the second one; with this it clicks into place. Real duplicates are still blocked. | Thunderstore · Nexus *(links coming soon)* |

More will show up here as I release them.

## Installing

All my mods need BepInEx for Valheim ([BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)). Use whatever you already use:

- **r2modman or Thunderstore Mod Manager:** install from Thunderstore. BepInEx gets set up for you.
- **Vortex or Mod Organizer 2:** grab the mod from Nexus with "Mod Manager Download". Your mod manager needs BepInEx for Valheim set up first. Vortex's Valheim support does this for you. In Mod Organizer 2, use its Valheim support plugin and install BepInExPack Valheim as a mod.
- **By hand:** download from Nexus and extract the zip into your Valheim folder (the one with valheim.exe). The folders inside are already set up.

Each mod's page has its own install notes too.

## Building from source

Each mod has a `tools/build.sh` that builds it with Mono (`mcs`) against your own game's DLLs, and a `tests/run-tests.sh` for its tests. The comments at the top of each script say what they need. There's also a `.csproj` if you'd rather build with `dotnet` or Visual Studio (set `ValheimPath` and `BepInExPath` if your folders are somewhere else).

## Found a bug or got an idea?

Open an [Issue](../../issues) and tell me which mod and version you're on. Your `BepInEx/LogOutput.log` helps a lot.

## How these are made

I make these with Claude. I come up with the idea, spot the problems and test everything in game. Claude reads through the game code and writes the mod code and most of the docs. On Thunderstore and Nexus they're marked as AI-generated.

## Licence

[MIT](LICENSE). Use them, change them and share them, just keep the copyright notice with them.
