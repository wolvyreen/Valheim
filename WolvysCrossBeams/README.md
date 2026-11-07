# Wolvy's Cross Beams

**By Wolvyreen(With_ClaudeAI)**

Build an **X** or a **+** cross out of two beams of the same type, and the second beam **snaps** across the first, the way you'd expect. In vanilla Valheim the game quietly refuses that snap, whatever kind of beam you use.

Works with every beam and pole type (wood, core wood, darkwood, iron-wood and so on), diagonal 26° and 45° beams, and any other piece you want to cross with a copy of itself.

---

## The problem

You place one beam, then try to snap a second identical beam across it, for example its lower end onto the top of a support post. The second beam just won't snap. It floats loose in front of the first one, however carefully you aim and whichever snap point you pick with Q/E.

A lot of players run into this with diagonal braces, X-shaped wall bracing, crossed roof beams and + shaped floor beams.

## Why it happens

Before the game snaps a piece, it checks for an **accidental duplicate**. This stops a fast double-click from putting a second piece hidden inside the first one.

The check is very simple. If a piece **with the same name** already has its **centre within 5 cm** of where the new piece would go, the snap is thrown away. It doesn't look at the angle. The game does have an option to also compare angles, but it's switched off for beams.

In a symmetric cross, both beams have their centre at the crossing point. So the second beam's centre lands right on the first one's, and the game treats a beam turned 90° as a duplicate.

## What this mod changes

The duplicate check gets smarter, not weaker. A piece of the same type in the same spot only counts as a duplicate when **its snap points land on the existing piece's snap points**.

- **A crossing beam** has its ends somewhere else, so it **snaps**. ✔
- **The same beam clicked twice** is refused, as in vanilla. ✖
- **The same beam turned 180°** looks and sits exactly the same, so it's **still refused**. ✖
- **A pole turned any amount** is still refused. ✖
- **A square floor turned 90°** is still refused. ✖

So you get your crosses, and you still can't accidentally stack a hidden copy inside a piece.

## What it doesn't change

The mod only changes that one decision: whether the ghost is allowed to jump to the snap point. Everything else is vanilla:

- valid / invalid (green / red) placement, support and stability, build costs, workbench range, wards and no-build zones
- the pieces themselves: what you build is ordinary vanilla pieces at ordinary positions

## Good to know

- **Client-side only.** Install it on your own game. Servers and other players don't need it and see your crosses normally.
- **Safe to remove.** Everything you built stays exactly as it is. You just lose the extra snapping.
- **Safe across game updates.** If a Valheim update renames or changes the game code the mod relies on, it writes an error to the BepInEx log and switches itself off, so you're back to vanilla snapping instead of broken placement. If its check ever hits an error mid-game, it falls back to vanilla's check.
- **One small extra allowance:** a square floor turned 45° in the same spot as another one now snaps (vanilla refuses it). It's clearly visible because the corners stick out, so it's not a hidden copy.

## Settings

The config file is `BepInEx/config/wolvy.valheim.crossbeams.cfg` (created the first time you start the game with the mod), and the setting can also be changed with Configuration Manager.

| Setting | Default | What it does |
|---|---|---|
| `[General] Enabled` | `true` | Turns the fix off without removing the mod (back to vanilla snapping). |

## Installation

**r2modman / Thunderstore Mod Manager:** click Install. BepInExPack Valheim is installed for you if you don't have it.

**Vortex or Mod Organizer 2 (from Nexus Mods):** click "Mod Manager Download" on the Nexus page, then enable the mod. Your mod manager needs BepInEx for Valheim set up first (Vortex's Valheim support handles this; in Mod Organizer 2 use its Valheim support plugin and install BepInExPack Valheim as a mod).

**Manual:** install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), then put `WolvysCrossBeams.dll` in `BepInEx/plugins/` (the Nexus download already has the right folders: extract it into your Valheim folder, the one with valheim.exe).

## Compatibility

- It patches one game method, `Player.IsOverlappingOtherPiece`, with a Harmony prefix.
- Mods that add snap points (Extra Snap Points Made Easy, Snapheim and similar) should work alongside it. They change *where* pieces can snap, and this mod changes *whether* a crossing snap is allowed.
- Mods that add their own extra rules to the same check with a postfix (for example ItemDrawers Remake, Elite Building Pieces) still run after this mod, so their rules keep working.
- Mods that replace the same check completely might conflict. Please report it if you find one.

## Source code

The full source, tests and build tools are on GitHub: https://github.com/wolvyreen/Valheim/tree/main/WolvysCrossBeams (MIT licence). Bug reports are welcome there as Issues.

## About this mod

This mod was made with AI. Wolvyreen directed it, found the problem and tested it in game. Claude (Anthropic's AI) read the game code to find the cause, and wrote the mod's code and this description.

## Changelog

### 1.0.2
- Hardening: snap points are compared where the game itself has them, and the check does less work per frame.

### 1.0.1
- Duplicates are recognised by their snap points, so turned-but-identical copies (beam turned 180°, pole, square floor turned 90°) are refused again.

### 1.0.0
- First version.
