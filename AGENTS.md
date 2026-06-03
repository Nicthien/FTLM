# Project Notes

## Overview
- `FTLM` is a **brick-breaker / Breakout-style game** (jeu de casse-brique).
- Built with the **Godot 4.6 engine (.NET/Mono)** in **C#**, driven via the `godot-mcp` MCP server (see `.mcp.json`; `GODOT_PATH` points to the console build of Godot 4.6.3).
- Main scene: `res://MainMenu.tscn`; the gameplay scene is `res://Jeu.tscn` (root node `Jeu`) — the playfield: bounding walls, a ball, and a paddle (`Bar`).
- C# project: `FTLM.csproj`, targeting `net8.0` for desktop and `net9.0` for Android.
- Physics engine is configured as Jolt Physics in `project.godot`.

## Structure
- `Scripts/` contains C# scripts. Current gameplay script: `BalleScript.cs`, attached to the `Balle` `RigidBody3D`.
- `Physics_Material/` contains reusable Godot physics materials. `Mur.tres` is frictionless and fully bouncy.
- `Jeu.tscn` defines the casse-brique playfield: bounding walls, the ball (`Balle`), the paddle (`Bar`, a `StaticBody3D`, currently hidden and not yet wired to input), camera, and directional light. The breakable bricks are not implemented yet.

## Conventions
- Keep files UTF-8; `.editorconfig` declares `charset = utf-8`.
- Follow existing Godot C# style: public partial classes extending Godot node types, with `_Ready` and `_Process` overrides as needed.
- Prefer editing scene/resource files through Godot when possible, but small textual changes are acceptable when the structure is clear.
- Preserve user-created scene node names, especially the French labels such as `Mur_Gauche`, `Mur_Droit`, `Mur_Haut`, `Mur_Bas`, and `Balle`.

## Notes For Future Changes
- The ball currently gets its initial impulse in `Scripts/BalleScript.cs`.
- The `Balle` node locks linear movement on Z, disables gravity, and uses a bouncy physics material.
- The repository is not currently initialized as a git repository.
