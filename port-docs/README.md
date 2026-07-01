# Emerge → Godot 4.7 Port Documentation

Analysis of the existing Unity codebase, written to re-explain how the game works
and to plan the Godot 4.7 port (with a move to **LDTK** for level editing).

Read in order:

1. **[00-overview.md](00-overview.md)** — what the game is, the big subsystems,
   your-code vs third-party.
2. **[01-architecture.md](01-architecture.md)** — base classes, managers, the
   `Body` model, the all-important load/init order, the grid, events, the tick
   scheduler.
3. **[02-gameplay-systems.md](02-gameplay-systems.md)** — lasers, triggers/signals,
   machines/devices/items, carry-and-rotate, players/input, Ink narrative,
   indicators.
4. **[03-levels-and-data.md](03-levels-and-data.md)** — how a level is stored, the
   data I **extracted**, and the **LDTK migration plan**.
5. **[04-godot-port-plan.md](04-godot-port-plan.md)** — Unity→Godot concept map,
   decisions, build order, watch-outs.

## Tooling produced by this analysis

- **`../tools/extract_levels.py`** — parses the shipped `grid_data.json` /
  `round_0.json` into clean, engine-agnostic level data. Run: `python
  tools/extract_levels.py` from the repo root.
- **`../tools/level_export/`** — its output: per-scenario `*.map.txt` (ASCII
  preview), `*.grid.json` (clean integer tile grid), `*.bodies.json` (decoded body
  placements). This is the intended input to the LDTK pipeline.

## TL;DR

A 2.5D, grid-based, top-down **laser-routing puzzle game with an Ink-driven
narrative layer**. Everything on the field is a `Body` on a tile `Grid`, wired
together by a `TriggerHandler` signal bus and orchestrated by an
`EmergeSceneManager` + managers, on a 0.2s tick. Levels = a Unity scene + a baked
tile-grid JSON (+ a legacy body JSON). The tile grids extract cleanly today; see
doc 03 for the LDTK plan and doc 04 for the port mapping.
