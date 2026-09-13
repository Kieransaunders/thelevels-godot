# The Levels — agent notes

Godot 4.7 (.NET/C#), Jolt physics. The Unity project at
`/Volumes/External/DevExteralHD/The Levels Unity` is the frozen 2026-09-07
prototype this port reproduces — read it for baseline simulation behaviour,
do not develop in it.

## Verification gate

`./verify.sh` — build, `dotnet test` (NUnit, 38 tests), headless import,
headless run with the `--verify-p3` / `--verify-p4` / `--verify-p5` /
`--verify-p6` / `--verify-p7` adapter checks. It must pass before any commit.
`--verify-input` additionally drives a synthesized keypress through the Input
Map and asserts the camera moves, plus a synthesized trackpad pan-scroll and
asserts the camera zooms (macOS trackpads/Magic Mouse deliver scroll as
`InputEventPanGesture`, not wheel buttons); `--showcase` stages windowed
captures of the P6 effects (pins the window on top — an occluded macOS window
throttles to ~1 fps).

## Godot skills

Installed at `/Users/boss/.claude/skills/` (user level — available to every
agent, in any project), from https://github.com/jame581/GodotPrompter (MIT):

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
