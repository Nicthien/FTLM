# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FTLM is a **brick-breaker / Breakout-style game** (jeu de casse-brique) built with the **Godot 4.6.3 (.NET/Mono)** engine, written in **C#**. It is a playable game: a paddle moved with the arrow keys, a ball, multiple **levels** of destructible (multi-hit) bricks, falling **bonus/malus capsules**, **multi-ball**, **score/lives/best-score**, **sound effects + particles**, and a **menu / pause** flow. The Godot engine is driven through the **`godot-mcp` MCP server** (see below). See [AGENTS.md](AGENTS.md) for additional notes maintained alongside this file.

## Toolchain & Commands

The `godot` executable is **not on PATH**. Use the full path:
`C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe` (the `_console` variant for CLI/headless work).

- **Build C#:** `dotnet build` (also wired as the VS Code `build` task, run automatically before debug launches).
- **Run the game:** `& "C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe" --path .`
- **Run the auto-test:** append `-- --test` to the run command. It self-drives a full play-through and prints `[TEST] …` to the console, then quits. `godot-mcp run_project` cannot pass CLI args, so use the console exe directly for this. (A non-fatal "resources still in use at exit" warning is expected — the test force-quits while particles/timers are alive.)
- **Open the editor:** append `--editor` to the run command.
- **Re-import assets** (after adding files like the `Audio/` WAVs): `--headless --import`.
- **Debug (VS Code):** the `Launch` / `Launch Editor` configs in [.vscode/launch.json](.vscode/launch.json) build first, then attach the `coreclr` debugger. They resolve the Godot binary from the `godot-dotnet-tools.executablePath` setting.

### Driving Godot via MCP
The `godot-mcp` server is configured in [.mcp.json](.mcp.json) (with `GODOT_PATH` set to the console build). Prefer its tools for engine operations — `run_project` (returns debug output/errors), `get_debug_output`, scene/node editing, `launch_editor`. This is the most reliable way to run the project and read runtime errors. Requires a Claude Code reload after `.mcp.json` changes.

## Architecture

- **Entry point:** `run/main_scene = res://node_3d.tscn` ([project.godot](project.godot)). The C# assembly name is also `FTLM`.
- **Main scene** ([node_3d.tscn](node_3d.tscn)): four bounding walls (`StaticBody3D`: `Mur_Gauche`, `Mur_Droit`, `Mur_Haut` — note `Mur_Bas` was replaced by an `Area3D` **`ZoneMort`** below the paddle that detects lost balls), the `Bar` paddle (`AnimatableBody3D`), empty container nodes **`Briques`**, **`Balles`**, **`Capsules`** (filled at runtime), a `Camera3D`, a `DirectionalLight3D`, and a **`HUD`** `CanvasLayer` with score/lives/level/best-score labels + a centered `MessageLabel`.
- **Orchestrator** ([Scripts/GameManager.cs](Scripts/GameManager.cs)): attached to the root node; runs with `ProcessMode = Always` (so pause works). Owns the game state machine (`Menu` → `AttenteLancement` → `EnJeu` → `Pause`/`GameOver`/`Victoire`), level data (string-pattern grids), brick generation, ball/capsule spawning, bonus effects, scoring, audio, particles, and best-score persistence (`user://meilleur_score.dat`). It resolves scene nodes via `GetNode` in `_Ready` (typed `[Export]` node refs do **not** resolve reliably when hand-written into `.tscn` for .NET).
- **Reusable instanced scenes:** [Balle.tscn](Balle.tscn) (ball, `RigidBody3D`, group `balle`), [Brique.tscn](Brique.tscn) (brick, `StaticBody3D`, group `briques`), [Capsule.tscn](Capsule.tscn) (bonus, `Area3D`), [Explosion.tscn](Explosion.tscn) (`CpuParticles3D` burst). Each has a matching `Scripts/*Script.cs`.
- **Ball** ([Scripts/BalleScript.cs](Scripts/BalleScript.cs)): the `GameManager` launches it (`Lancer`), keeps a constant speed by re-normalizing `LinearVelocity` each physics frame, and sets the rebound angle off the paddle via `RebondSurBarre`. Per-ball `BodyEntered` is wired by the manager with a closure capturing the ball. Z axis locked, gravity off, frictionless/bouncy material → X/Y plane only.
- **Paddle** ([Scripts/BarScript.cs](Scripts/BarScript.cs)): `ui_left`/`ui_right` movement, clamped within the walls; `Redimensionner(facteur, duree)` for the size bonus/malus.
- **Audio:** WAV files in `Audio/` generated procedurally (short tones); loaded into per-sound `AudioStreamPlayer`s in `GameManager._Ready`.
- **Auto-test harness** ([Scripts/GameManager.Test.cs](Scripts/GameManager.Test.cs)): a `partial` of `GameManager`, gated by the `--test` CLI arg. Injects input via `Input.ParseInputEvent` and logs `[TEST] …` lines, then quits. See *Toolchain* below. Removable without touching the game.
- **Input:** `lancer_balle` (Space) defined in [project.godot](project.godot); `ui_left`/`ui_right`/`ui_cancel` are engine defaults (arrows + Escape for pause).
- **Physics:** **Jolt Physics**. Walls share the frictionless, fully-bouncy material [Physics_Material/Mur.tres](Physics_Material/Mur.tres).
- **Rendering:** Forward+ renderer, Direct3D 12 driver on Windows.
- **C# targets** ([FTLM.csproj](FTLM.csproj)): `net8.0` on desktop, `net9.0` when `GodotTargetPlatform == android`. Code analysis uses [.vscode/ruleset.xml](.vscode/ruleset.xml), which disables `CA1050` (namespace warning) — Godot scripts are intentionally namespace-less `public partial` classes extending engine node types.

## Conventions

- **Preserve the French node names** (`Mur_Gauche`, `Mur_Droit`, `Mur_Haut`, `Bar`, `Balle`, `Briques`, `Balles`, `Capsules`, `ZoneMort`) and existing comments — scripts resolve nodes by these names via `GetNode`. Code, identifiers, and comments are in **French** — match that.
- Scripts are `public partial` classes extending a Godot node type, with `_Ready` / `_Process` overrides. Match this style.
- Files are UTF-8 ([.editorconfig](.editorconfig)).
- Prefer editing scenes/resources through the Godot editor (or `godot-mcp`) over hand-editing `.tscn`/`.tres`; small textual edits are fine when the structure is clear.
- The repository is **not** a git repo.
