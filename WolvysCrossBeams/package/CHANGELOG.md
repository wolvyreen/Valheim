# Changelog

## 1.0.2 (2026-11-06)
- Hardening after a second pass through the game code: the new piece's snap points are now taken from where the game itself has them (world positions, moved by the snap), so a scaled piece can't throw the comparison off; the duplicate test only does that work when a same-named piece is actually in the same spot (it runs every frame while building).

## 1.0.1 (2026-11-06)
- A piece now only counts as a duplicate when its snap points land on the existing piece's snap points (instead of comparing angles). Closes a gap in 1.0.0, where a symmetric duplicate (a beam turned 180, a pole turned any amount, a square floor turned 90) could be placed inside an existing one.

## 1.0.0 (2026-11-06)
- First version. A piece that crosses another piece of the same type at its centre now snaps, as long as it is turned more than 10 degrees from it. Exact duplicates (same spot, same angle) are still refused.
