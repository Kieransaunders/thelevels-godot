# The Levels — agent notes

Godot 4.7 (.NET/C#), Jolt physics. The Unity project at
`/Volumes/External/DevExteralHD/The Levels Unity` is the frozen 2026-09-07
prototype this port reproduces — read it for baseline simulation behaviour,
do not develop in it.

## Project layout

Two .NET projects in `TheLevels.sln`; the game csproj `Compile Remove`s both,
so the dependency direction is game → `Core/` and never back (`Core/` must
not reference Godot):

- `Core/` — engine-free simulation library (`TheLevels.Core`): `Simulation/`
  (shallow-water heightfield, fire), `Math/` (`DeterministicNoise` etc.),
  `Agents/` (druid brains + `DruidManager`), `Levels/` (a level is a C# fill
  function — `RaisedWay` — plus one argument at the `HeightfieldSimulation`
  construction site; a new level never opens the solver).
- `Tests/` — NUnit project referencing `Core/` only.

Game assembly (`TheLevels`), everything wired into `Scenes/main.tscn`:

- `Main.cs` + `Main.Verification.cs` — one `partial class Main`. `Main.cs`
  builds the scene; `Main.Verification.cs` holds the `--verify-*` gates and
  `--showcase`/`--capture` routines, which need a live scene tree and so
  cannot move to `Tests/`.
- `Simulation/` — `SimulationHost`, the Node that steps the Core sim.
- `View/` — `HeightfieldView` (publishes Core heightfield/fire edits to
  meshes) and `WorldCoordinates`.
- `Agents/` — `DruidView`, visuals for the `Core/Agents/` druids.
- `Player/` — `StrategyCamera`, `WorldCursor`, `InputBindings` (Input Map).
- `UI/` — `Diagnostics` overlay. `Vfx/` — `SpellVfx`, `GodHandVfx`.
- `Shaders/` — `terrain`, `water`, `unlit_color` gdshaders.

Non-code:

- `Assets/Vfx/` — the only shipped third-party assets (three CC0 textures);
  provenance table in `Assets/Vfx/LICENSE.md`.
- `reference/` — `.gdignore`d, gitignored read-only clones (voxel docs, VFX
  libraries, concept art). Nothing ships from it; re-fetch, don't commit.
- `docs/` — `plans/` (design + execution plan), `verification/` (gate
  evidence screenshots), game story/overview. `Play The Levels.command` is
  the double-click launcher.

CI (`.github/workflows/verify.yml`) runs the same `./verify.sh` on every PR —
keep it as the single gate rather than forking a CI script.

## Verification gate

`./verify.sh` — build, `dotnet test` (NUnit, 80 tests), headless import, and
the headless runs below. The first passes `--sandbox` with the `--verify-p3` /
`--verify-p4` / `--verify-p5` / `--verify-p6` / `--verify-p7` / `--verify-p8`
/ `--verify-villagers` adapter checks; then one `--verify-level` boot per
catalogue entry, a `--verify-level-hop` run, and `--verify-level-one` on the
default scene. It must pass before any commit. P8 covers the flora and
fauna (`Core/Vegetation/`, deer herd, frog chorus); the villager gate
covers the settlement.

## Levels

`Core/Levels/Catalogue.cs` is the registry: name, title, config and fill for
every level. Adding a level is a fill function plus one line in `Catalogue.All`
— the CLI, the L key, the per-level tests and the `verify.sh` loop all read it,
so nothing else needs touching.

| Want | Do |
|---|---|
| See what exists | `godot --headless --path . -- --list-levels` |
| Play one | `--level=<name>` (or `./"Play The Levels.command" --level=<name>`) |
| Walk them all by hand | **L** in game — rebuilds the scene on the next entry |
| Prove one works | it is already in `verify.sh`; nothing to add |

`--sandbox` still works as an alias for `--level=sandbox`. The parity gates run
on it, because the bare `RaisedWay` map is what the port was measured against;
the default scene boots the level-one mission (`Core/Levels/FirstCrossing*`).

`Catalogue` deliberately carries config and fill but **not** the mission.
`FirstCrossingMission` is the only mission that exists, and one example is not
enough to design an interface from. Promote it when there are two to compare.

Test layers, in the order they will catch a mistake:

1. `Tests/CatalogueTests.cs` — runs engine-free against every catalogue entry:
   finite heights inside clamps, dry ground and water both present, thirty
   seconds of solver without faults, reset restores the map, generation is
   deterministic. A new level inherits all of it for free.
2. `Tests/<Level>Tests.cs` — the level's own rules (see `FirstCrossingTests`).
   This is where "can it be solved?" and "can it be lost?" get answered.
3. `--verify-level` — one headless boot per level: simulation built, terrain
   rendered, scene assembled, thirty frames clean, reset clean. `verify.sh`
   loops it from `--list-levels`, so it grows by itself.
4. `--verify-level-hop` — proves L actually rebuilds onto the next level.
5. `--capture=<path>@<frames>` — a screenshot, for the failures no assertion
   catches (groves on the road, a camera pointing at nothing).

Adapter gates brush the solver directly (`sim.ApplyBrush`). Do **not** reach
for synthesized mouse input in one: Godot reports the real OS cursor through
`GetViewport().GetMousePosition()`, so headless the window is a 64 px stub and
the cursor never moves, and windowed it lands wherever the physical mouse
happens to sit. `--verify-input` is the exception and runs windowed, on its
own, outside `verify.sh`. It drives a synthesized keypress through the Input
Map and asserts the camera moves, plus a synthesized trackpad pan-scroll and
asserts the camera zooms (macOS trackpads/Magic Mouse deliver scroll as
`InputEventPanGesture`, not wheel buttons); `--showcase` stages windowed
captures of the P6 effects (pins the window on top — an occluded macOS window
throttles to ~1 fps).

## Godot skills

Installed at `/Users/boss/.claude/skills/` and `/Users/boss/.zcode/skills/`
(user level — available to every agent, in any project), from
https://github.com/jame581/GodotPrompter (MIT):

| Skill | Use for |
|---|---|
| `csharp-godot` | GodotSharp vs GDScript, `partial` source generators, Variant marshalling |
| `shader-basics` | Shader language, recipes, post-processing |
| `particles-vfx` | GPUParticles, ParticleProcessMaterial, trails — the P6 art pass |
| `godot-optimization` | Profiler, draw calls, memory |
| `godot-debugging` | Remote debugger, signal tracing, systematic method |
| `godot-testing` | GUT / gdUnit4 TDD workflow |

Two places these skills conflict with this project — prefer the project:

- `shader-basics` documents `COLOR` only as the **2D canvas_item output
  colour**. `Shaders/terrain.gdshader` and `Shaders/water.gdshader` use
  `COLOR` as the **spatial vertex-colour input** fed by `HeightfieldView`.
  Its `COLOR` guidance does not transfer to those shaders.
- `godot-testing` assumes GUT/gdUnit4. This project tests the engine-free
  `Core/` assembly with NUnit via `dotnet test`, and covers the engine
  boundary with the `--verify-*` adapter gates in `Main.cs`. Keep that split;
  do not migrate to an in-engine test framework.

Neither those skills nor any other covers `ImmediateMesh` — note that
`SurfaceBegin`/`SurfaceEnd` append a surface, so a per-frame rebuild needs
`ClearSurfaces()` first (this caused a ring trail on 2026-09-12).

The water surface renders at `WaterSubdiv` (2×) the solver grid, bilinear-sampled
in `HeightfieldView`, so shorelines follow smooth depth contours instead of the
0.75 m cell edges; solver corners stay exact grid points and the P3 gate samples
their alpha at that stride. Don't flatten the water mesh back to sim resolution —
close-zoom shoreline smoothness depends on the sub-grid.
