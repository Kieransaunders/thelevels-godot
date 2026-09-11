# The Levels — Design / Architecture Decision Record

> **Revision 3, 2026-09-11.** Supersedes the voxel MVP design entirely. See "History" at the
> bottom for what changed and why, so the dead ends aren't rediscovered.

## Context

There is an existing, working Unity 6 prototype at
`/Volumes/External/DevExteralHD/The Levels Unity` — a From Dust-style god game set on the
Somerset Levels, with earth/water/fire/lightning tools, a shallow-water solver, spreading reed
fires, and eight druids walking to a stone circle on the tor.

It is being moved to Godot **for licensing and cost reasons**, not because of any technical
shortcoming. The Unity code is good and is the specification for this project.

**This is a port, not a redesign.** Where this document and the Unity source disagree, the Unity
source wins.

## Decisions

### D1 — Godot 4.7.2 **.NET (C#)**, not GDScript

The simulation is pure C# math over `float[]`. Porting C#→C# is mechanical; C#→GDScript is a
rewrite. And the performance case is decisive: the solver runs ~5 full grid passes per substep
over 129² = 16,641 cells, up to 4 substeps per frame — roughly 330k cell-operations a frame.
GDScript is an order of magnitude too slow for that without dropping resolution or moving to
compute shaders.

Download: `Godot_v4.7.2-stable_mono_macos.universal.zip` (verified present on the 4.7.2-stable
release).

**Accepted cost:** Godot 4 cannot export C# projects to the web ("Projects written in C# using
Godot 4 currently cannot be exported to the web"). If web distribution ever becomes a
requirement, this decision must be revisited — a GDScript + compute-shader rewrite, or waiting on
upstream .NET web support.

### D2 — Heightfield terrain, as in Unity

Terrain and water are 2D `float` grids over a fixed world extent. No voxels. From Dust has no
caves or overhangs, and the Somerset Levels are flat wetland — flooding and drainage across a
height grid *is* the game.

### D3 — Keep the solver's algorithm exactly

Shallow-water (Müller) on a staggered velocity grid: velocity integration down the
terrain+water surface gradient, CFL clamp at half a cell per step, upwind flux height
integration, two-pass donor-cell scaling so no cell can go negative, reflective domain edges,
dry-cell-as-wall boundaries.

Do not "improve" this during the port. It is tuned and its properties are pinned by tests.

### D4 — Tests port first, and they are the acceptance criteria

The Unity project has **14** NUnit cases across `HeightfieldSimulationTests.cs` (131 lines) and
`FireSimulationTests.cs` (227 lines, 9 cases). They pin the things that actually matter — volume
conservation across scoop/drop and across reset, no negative water depth, momentum developing on
slopes then settling below the CFL bound, `InvalidValueCount == 0`.

Port each test **before** the code it covers. A ported simulation that passes them is correct;
one that doesn't, isn't. This is the cheapest possible guarantee that a 480-line numerical port
didn't silently break.

## Port map

| Unity source | Lines | Godot target | Difficulty* | Note |
|---|---|---|---|---|
| `HeightfieldSimulation.cs` | 480 | `Simulation/HeightfieldSimulation.cs` | **Low** | Pure `float[]` math. Only `Awake`/`Update` touch the engine. Swap `Vector3`/`Mathf`, drop `[SerializeField]` for `[Export]`. |
| `HeightfieldSimulationTests.cs` | 131 (5 cases) | `Tests/HeightfieldSimulationTests.cs` | Low | NUnit → GdUnit4 or a plain headless assert runner. |
| `FireSimulation.cs` | 337 | `Simulation/FireSimulation.cs` | **Low** | Same shape — pure arrays over the same grid. |
| `FireSimulationTests.cs` | 227 (9 cases) | `Tests/FireSimulationTests.cs` | Low | |
| `VertexColorTerrain.shader` | 47 | `Shaders/terrain.gdshader` | Medium | HLSL/ShaderLab → Godot shading language. |
| `VertexColorWater.shader` | 90 | `Shaders/water.gdshader` | Medium | |
| `HeightfieldView.cs` | 197 | `View/HeightfieldView.cs` | Medium | Unity `Mesh` → Godot `ArrayMesh` + `SurfaceTool`; per-frame vertex/colour upload. |
| `StrategyCameraController.cs` | 70 | `Player/StrategyCamera.cs` | Medium | Input system differs; behaviour is simple. |
| `WorldCursorController.cs` | 488 | `Player/WorldCursor.cs` | **High** | Input, raycast-to-heightfield, four tools, VFX triggers. Re-author against the same public behaviour. |
| `DruidManager.cs` | 442 | `Agents/DruidManager.cs` | **High** | Unity `Transform`/`GameObject`/`Material` throughout. Re-author as Godot nodes; keep the state machine and steering rules. |
| `SimulationDiagnostics.cs` | 104 | `UI/Diagnostics.cs` | Low | |
| `TheLevelsBootstrap.cs` | 63 | `Main.cs` | Low | |
| `SpellVfx.cs` | 46 | `Vfx/SpellVfx.cs` | **Rewrite** | See below. |

### What does NOT port

The purchased Unity VFX — NamuFX Stylized Water Effects, Matthew Guz Spell AoE, Houidisoft water
— are Unity VFX Graph / Shuriken / Shader Graph assets. **None of them transfer.** `SpellVfx.cs`
loads them by name from `Resources/`; that whole path is gone.

This is a real, accepted cost of the port. Replacement is native Godot particles (see D5), with
MIT/CC0 reference material cloned under `reference/`.

### D5 — God-hand VFX from native Godot particles

The desired effect: a golden pointer trailing white spirit-light, with scooped earth or water
swirling around it, the swirl growing as the buffer fills.

No custom shader is required. Verified-present Godot 4 features cover it:

| Element | Feature |
|---|---|
| Swirl around the pointer | `ParticleProcessMaterial.orbit_velocity_min/max`, `radial_velocity_min/max` (slightly negative to draw inward) |
| Grows with the held amount | `GPUParticles3D.amount_ratio`, driven by `EarthBuffer / EarthCapacity` (or the water pair) — the simulation already exposes exactly this 0→1 value |
| Spirit trail | `GPUParticles3D.trail_enabled` + `trail_lifetime`, drawn with a `RibbonTrailMesh` / `TubeTrailMesh` pass |
| Golden glow | emissive `StandardMaterial3D` + `WorldEnvironment` glow |
| Matter drawn toward the hand | `GPUParticlesAttractorSphere3D` |
| Earth vs water look | swap particle colour/texture on tool change |

A custom `.gdshader` is justified later only for the trail's *texture* — noise dissolve, gradient
fade — not for the mechanic.

## Reference material

Under `reference/` (carries a `.gdignore`, so Godot never imports it; gitignored):

| Path | Licence | Use |
|---|---|---|
| `vfx-library/` (`haowg/GODOT-VFX-LIBRARY`) | MIT | Godot 4 magic/action particle effects — closest match to the god-hand swirl |
| `vfx-textures/` (`RPicster/...`) | **CC0** | Particle sprites: soft glow dots, swirls, flares. Safe to ship. |
| `gdquest-vfx/` (`gdquest-demos/godot-4-VFX-assets`) | code MIT, **art CC-BY-NC-SA** | Technique reference only — the art is non-commercial, do not ship it |
| `godot_voxel-docs/`, `voxelgame/` | — | **Dead.** Left from the abandoned voxel design; delete when convenient. |

## Verification

No Godot MCP server exists in this environment and `godot` is not on `PATH`. Everything runs
through Bash against the installed binary:

```bash
GODOT="$HOME/Applications/Godot-Mono.app/Contents/MacOS/Godot"
"$GODOT" --path . --headless -- --run-tests    # ported assert suite
"$GODOT" --path .                              # windowed play-test
```

Screenshot the windowed run with `mcp__computer-use__app_screenshot` when visual confirmation is
needed.

**Acceptance for the port:** all 14 ported tests green, and the play-test reproduces the Unity
README's described behaviour — tools 1–4, scoop/drop, water flowing downhill and pooling, fire
spreading on dry reed and doused by water, lightning refusing wet ground, druids wading shallow
water, drowning when submerged, and the ritual completing when the living reach the circle.

## History — decisions already made and closed

Recorded so these aren't relitigated:

1. **Voxel terrain (`godot_voxel`) — rejected.** Chosen in revision 1 before the Unity prototype
   came to light. From Dust is a heightfield game; voxels only buy caves and overhangs, which this
   game does not have. ~85MB of reference was cloned and is now dead.
2. **Blocky vs smooth voxels — moot.** Revision 2 corrected blocky→smooth SDF; superseded by
   dropping voxels entirely.
3. **`godot_heightmap_plugin` — rejected.** 83MB editor plugin for authoring *static* terrain.
   Our heightfield deforms every frame and we own the array.
4. **`Zylann/godot_scatter_plugin` — rejected.** Dead since 2021, Godot 3 only. Scatter plugins
   bake onto static surfaces; our ground moves constantly. If props are wanted, repopulate a
   `MultiMeshInstance3D` at runtime.
5. **GDScript — rejected.** See D1.

\* Difficulty is an estimate from the public API surface and a skim, not a full read of every
file. `HeightfieldSimulation.cs`, its tests and `HeightfieldView.cs` were read; `DruidManager.cs`
and `WorldCursorController.cs` were not. Read them before planning around those two ratings.
