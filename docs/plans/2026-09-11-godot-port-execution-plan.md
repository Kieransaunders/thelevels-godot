# The Levels — implementation plan grounded in the Unity source

Status: ready for environment setup; implementation not started. Reviewed 2026-09-11.

## 1. Objective and document precedence

Reproduce the existing Unity prototype in Godot .NET: deformable earth, shallow water, fire, lightning, eight druids, and ritual completion. Replace Unity-only effects with native Godot effects. First reach behaviour parity; then profile and improve presentation.

Use this plan for execution sequencing and verification. It consolidates and corrects the existing [revision 3 implementation plan](2026-09-11-from-dust-mvp-implementation.md), rather than treating its tasks as already completed.

Precedence:

1. Current Unity runtime source and its NUnit assertions define existing behaviour.
2. The [revision 3 design ADR](2026-09-11-from-dust-mvp-design.md) defines the C# port decision and accepted changes, including replacement noise and native VFX.
3. This plan supplies implementation boundaries, dependencies, and acceptance gates.
4. [The Levels Game Overview](The%20Levels%20Game%20Overview.md) supplies setting, visual direction, and useful exploratory scenarios. Its earth/water-only scope, 256² grid, and GDScript/compute architecture are superseded for this port.

Unity source: `/Volumes/External/DevExteralHD/The Levels Unity/Assets/TheLevels/`.

Godot project: `/Volumes/External/DevExteralHD/1_Current /TheLevels/the-levels/`.

Defer voxels, a replacement solver, GPU simulation, expanded settlements, progression, networking, saving, and web export. Fire and druids are in scope because they already exist in the prototype.

## 2. Verified starting point and Godot MCP access

| Check | Result on 2026-09-11 |
|---|---|
| Godot project | `project.godot` exists, declares 4.7 / Forward Plus, and has no main scene configured. No game implementation or C# project is present at the project root. |
| Git | Repository already exists on `main`, with no commits. Existing project files are untracked. Do not run a blind initial `git add -A`. |
| Reference exclusion | `reference/README.md` says references are gitignored, but the current root `.gitignore` only excludes `.godot/` and `/android/`. Correct this before staging assets. |
| Unity source | Available and readable. Runtime scripts, both test files, both shaders, and README inspected. |
| Code knowledge graph | Accessible, but indexed under the old `/Users/boss/The Levels` location. Symbol lookup succeeds; source snippets are unavailable. Direct reads of the current Unity files were therefore necessary. |
| Godot MCP | Installed 2026-09-11: `@coding-solo/godot-mcp` registered as a `godot` stdio server in `~/.zcode/cli/config.json` with `GODOT_PATH` pointing at the installed engine. Stdio smoke test succeeded (`initialize` + `tools/list` returned 13 tools). Tools appear in new sessions only; not exposed in the session that wrote this row. |
| Engine / SDK | Godot 4.7.2 stable .NET (mono, universal) installed 2026-09-11 at `~/Applications/Godot_mono.app/Contents/MacOS/Godot` (reports `4.7.2.stable.mono.official.ed1daf0bf`; quarantine attribute removed). Not symlinked into PATH. `dotnet` SDK still absent from PATH — remaining P0 blocker for C# compilation. |

Godot 4.7.2 .NET for macOS is listed in the [official release archive](https://godotengine.org/download/archive/4.7.2-stable/). Godot requires a separately installed SDK for C# compilation; C# web export remains unsupported in the [official C# documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html).

MCP is optional for implementation: terminal build/test/run is a separate route once the engine and SDK are available. To claim MCP access later, the server must be exposed to the session and successfully return a read-only engine/project query for this project. A configured server name alone is insufficient.

## 3. Source facts that change the previous plan

| Topic | Source behaviour and implementation consequence |
|---|---|
| Grid | 129 × 129 samples over 96 m; spacing is `96 / (129 - 1) = 0.75 m`. Preserve the existing `CellArea` accounting, including edge samples. Do not substitute the overview's dimensions or reinterpret samples as finite-volume cells during the port. |
| Water timing | Fixed step 1/30 second, maximum four substeps per rendered frame, with incoming elapsed time capped. Preserve staggered velocities, donor scaling, dry-cell walls, and reflective edges. |
| Fire timing | Separate 0.1-second step, maximum two substeps per frame; automatic stepping stops when no cells burn. Do not advance fire once for every water step. |
| Brush | Radius 3.5 m, transfer budget 20 m³/s, earth and water capacities 140 m³, terrain bounds 0.2–18 m. Keep exact transferred-volume accounting. |
| Groundwater | Earth scooping fills hollows up to groundwater level 1.2 m. This intentionally introduces water. Closed-water conservation checks must exclude this action or account for introduced water separately. |
| Lightning | Cooldown 1.4 seconds; strike radius is 0.8 × brush radius. Over water deeper than 0.05 m, the cursor calls the water scoop method with duration 0.3 seconds. This fills the water buffer and is capacity-limited; it is not evaporation despite the README wording. |
| Pause and step | Space pauses water and fire. Camera, tools, cooldowns, VFX and druids keep updating. N while paused runs one water step and one fire step, with their different durations. Preserve this behaviour unless a separate gameplay change is requested. |
| Reset | Restores terrain/water, clears carried matter, resets fire and druids, resets the camera, and resumes simulation. Fire reset regenerates fuel but does not reseed its random stream. Druid appearance also varies on respawn. Do not promise identical post-reset stochastic playback. |
| Noise | Both terrain generation **and fire fuel generation** call Unity Perlin noise. The ADR accepts deterministic replacement noise; cover both call sites, and validate wet interiors and connected dry fuel patches. |
| Coordinates | Keep simulation coordinates unchanged, with positive Z increasing the grid row. Centralize conversion at the Godot boundary, including any Z reflection chosen for visual parity. Transform camera, mesh winding, normals, brush rays and agents consistently. Do not blindly negate Z throughout the solver. |
| Cursor | Earth/fire target terrain; water/lightning target water surface when wet. The source uses five surface-intersection refinements. Preserve tool-dependent targeting and test shallow viewing angles. |
| Druids | Spawn centre `(14, 0, 6)`; haven centre `(-8, 0, -4)` with radius 4.2 m. Keep these coordinates even though the README loosely describes a circle on the tor. Ritual requires at least one survivor and all survivors within the haven radius. |
| Rendering | View rebuilds only when dirty and listens to both water/terrain and fire changes. Normals and bounds are recomputed. Preserve this dependency, including visible fuel, flames and char. |

The source also permits ember overspend when the remaining drop budget is below the per-cell cost of 0.5. Record and characterize this existing defect; do not silently change it while claiming an unchanged simulation port. Any correction needs a distinct change and regression test.

## 4. Proposed implementation structure

Use three projects so simulation tests cannot accidentally require the Godot runtime:

```text
TheLevels.csproj                       # Godot .NET application
TheLevels.sln                         # game, core and test projects
Core/TheLevels.Core.csproj             # ordinary .NET library
Core/Simulation/HeightfieldSimulation.cs
Core/Simulation/FireSimulation.cs
Core/Simulation/SimulationConfig.cs
Core/Simulation/SimulationMetrics.cs
Core/Math/SimulationMath.cs
Core/Math/DeterministicNoise.cs
Tests/TheLevels.Tests.csproj           # NUnit, references Core only
Tests/HeightfieldSimulationTests.cs
Tests/FireSimulationTests.cs
Tests/PortContractTests.cs
Simulation/SimulationHost.cs           # initialization, scheduling, reset, errors
Player/StrategyCamera.cs
Player/WorldCursor.cs
View/HeightfieldView.cs
Agents/DruidManager.cs
Agents/DruidAgent.cs
Vfx/GodHandVfx.cs
Vfx/SpellVfx.cs
UI/Diagnostics.cs
Main.cs
Scenes/main.tscn
Scenes/druid.tscn
Scenes/god_hand.tscn
Shaders/terrain.gdshader
Shaders/water.gdshader
Assets/Vfx/
docs/verification/
```

The Godot project must exclude `Core/**/*.cs`, `Tests/**/*.cs`, and `reference/**/*.cs` from its default compile glob and reference Core as a project. Prevent nested build output from being compiled or exported. Add Godot import exclusions for reference and test material as appropriate; do not hide game scenes or scripts from Godot.

Core uses `float[]`, BCL math and an engine-independent position representation such as `System.Numerics.Vector3`. Preserve Unity's clamped interpolation, smooth-step and approximate-comparison semantics explicitly where used; same-named engine methods need not have identical semantics. Use `Stopwatch` for timings and a plain error event/state instead of engine logging. Godot adapters perform vector conversion and log presentation.

`SimulationHost` owns initialization and independent water/fire accumulators. Run water then fire, tools before publishing the frame's visual changes, and render after all state edits. A tool edit affects the next scheduled water step. Avoid accidental double stepping through both `_Process` and `_PhysicsProcess`.

## 5. Implementation phases

### P0 — Environment, source baseline, and runnable scaffold

**Dependencies:** none. **Deliverables:** project files, minimal main scene, build instructions, source baseline record.

- Locate or install Godot 4.7.2 .NET and a compatible .NET SDK. Record exact binary paths, architecture and versions; use Godot's generated SDK/target-framework values and pin the working SDK.
- Preserve the existing repository and files. Extend ignores for reference clones, `.DS_Store`, build output and local IDE output before making a scoped baseline commit during implementation.
- Record hashes of inspected Unity runtime/tests/shaders and capture a baseline Unity view if the editor is available. Source remains the baseline when a live comparison cannot be run.
- Create the separate Core, NUnit and Godot projects, wire references/exclusions, and configure `Scenes/main.tscn` as the startup scene.
- Instantiate a trivial C# Node and confirm it builds and prints in a headless run and opens windowed.
- Update the overview/reference README to explain superseded scope and which VFX references are still relevant. Reindex the current code location when code exists.
- If Godot MCP is configured later, verify it with a read-only call and record the result separately from CLI access.

**Exit gate:** clean solution build; windowed and headless startup succeed; tests can execute without launching Godot. Environment gaps are resolved before P1 execution is reported as verified.

### P1 — Heightfield core and original five tests

**Depends on:** P0. **Files:** Core heightfield/config/math/metrics; heightfield tests.

- Port the five original tests first, retaining assertions and absolute versus relative tolerances. Replace Unity object setup with pure class construction.
- Port the complete public API, including `WorldToGrid`, `StateChanged`, `MatterType`, and metrics types omitted from parts of the previous file list.
- Preserve array layout, loop order, brush falloff, caps, groundwater behaviour, flux math, CFL component limit, dry threshold, invalid-value handling and reset state.
- Replace terrain noise with the ADR's deterministic value noise, keeping the input scale and amplitude. Treat resulting map differences as an explicit accepted deviation.
- Add focused contract checks for grid/world round trips, boundaries, buffer/height limits and groundwater accounting. Preserve the skipped overshoot-term comment as a documented limitation, not a verified safety guarantee at arbitrary depths.

**Exit gate:** all five original heightfield tests pass with unchanged tolerances; added contract checks pass; no engine singleton is used by Core.

### P2 — Fire core and original nine tests

**Depends on:** P1. **Files:** Core fire; fire tests.

- Port all nine fire tests before the covered implementation.
- Preserve fuel/fire/char arrays, thresholds, seeded spread order, cardinal and diagonal probabilities, ember rates, extinguishing and burnout.
- Apply the same deterministic noise policy to fuel generation. Verify dry, fuelled neighbourhoods and a deep pool interior still exist; do not weaken fixtures or assertions to force a pass.
- Characterize RNG continuation across reset and fractional ember-drop behaviour in the verification notes. Keep intended fixes separate from parity work.

**Exit gate:** original suite is 14/14 green. This proves those simulation cases, not complete gameplay parity.

### P3 — Main world, terrain/water rendering and diagnostics

**Depends on:** P2. **Files:** Main, main scene, SimulationHost, HeightfieldView, shaders, initial Diagnostics.

- Initialize heightfield before fire, then subscribe the view and construct the world. Provide a default camera, warm directional light, fog and environment.
- Port both vertex-colour shaders, preserving terrain palette, water depth colours, ripples, rim treatment and transparency intent. Test dry-cell visibility and scene lighting explicitly.
- Build indices and UVs once. Reuse C# vertex/colour/normal buffers and update `ArrayMesh` when dirty. Validate Godot C# array marshaling against the installed API; do not paste GDScript packed-array type names into C# blindly.
- Recompute normals/bounds, restore material assignments after surface replacement, and unsubscribe on scene teardown.
- Show world and carried volumes, wet cells, max depth/speed, simulation timings and correction counters from the start; refresh expensive metrics at the source's 0.25-second cadence.

**Exit gate:** runnable Somerset world with flowing water; both simulation event sources trigger view changes; no shader errors or hidden/inverted mesh faces; saved screenshot and timing snapshot.

### P4 — Camera, cursor, all tools, and global controls

**Depends on:** P3. **Files:** StrategyCamera, WorldCursor, Input Map, SimulationHost controls, HUD.

- Implement WASD pan, Q/E rotate, arrow tilt, wheel zoom and F focus. Preserve source bounds, distance range, pitch range and starting view while adapting screen-space conventions.
- Add tool selection 1–4, left scoop/strike, right drop, and terrain-following brush ring with material feedback. Use Input Map actions throughout.
- Preserve actual transferred-volume return values for VFX and feedback; use elapsed duration for brush budgets. Test earth/fire ground targeting separately from water/lightning surface targeting.
- Port lightning cooldown and water-buffer transfer exactly. Add lightweight visible spell feedback before polished effects.
- Implement R/Space/N/F1 according to the source semantics above. Restore the native pointer when no valid target exists or the controller exits.

**Exit gate:** player can dig, build a bank, redirect water, transfer water, kindle/douse fire and call lightning; cursor stays aligned across camera angles; controls and pause/step/reset behaviour have recorded checks.

### P5 — Druids, haven, and ritual

**Depends on:** P4. **Files:** DruidManager, DruidAgent, druid scene; haven construction.

- Separate the two classes currently housed in the Unity DruidManager file. Use Godot nodes and shared mesh/material resources for simple bodies and seven standing stones.
- Preserve Travel/Panic/Dead states and steering probe order. Walk speed is 2.5 m/s; wading starts above 0.30 m; drowning above 0.85 m for more than 2.2 seconds; hazard probes reach 2.4 m.
- Preserve flame thresholds, 0.25-second threat scans, panic hysteresis, death timing, world bounds, spawn arrangement and haven test.
- Emit ritual completion once when all living agents are in the haven and at least one is alive. All-dead must not complete the ritual.
- Reset agents without retaining stale references to nodes awaiting `QueueFree`; avoid duplicate haven construction and event subscriptions.

**Exit gate:** bridge passage, blocked passage, shallow wading, flood drowning, flame death, panic recovery, survivor ritual and all-dead scenarios verified. Record that druids continue during water/fire pause.

### P6 — Native Godot VFX and presentation

**Depends on:** P4 and P5. **Files:** god-hand scene/controller, native SpellVfx, selected VFX assets.

- Build golden pointer, white spirit trail and orbiting matter using native particles/materials. Verify the installed Godot particle and trail API in a small scene before tuning.
- Drive earth/water/ember swirl from the actual selected buffer ratio; clamp the display ratio and handle zero capacity. Hide held matter for lightning. Validate empty, half and full states.
- Supply native feedback for water collection/drop, fire gathering/drop, lightning and ritual. Remove Unity Resources/prefab assumptions from all callers.
- Verify trails, attraction, emission bounds, visibility after reset, and blend/glow behaviour at normal gameplay distance.
- Copy only selected permitted assets into `Assets/Vfx`, keeping source/licence records. Use CC0 textures and permitted MIT assets after checking their licences. GDQuest art remains technique reference only under the ADR's non-commercial restriction.

**Exit gate:** recognizable feedback for every tool and ritual; buffer fill reads clearly without obscuring the terrain; no shipped dependency on `reference/`.

### P7 — Integration hardening, profiling, and release candidate

**Depends on:** P0–P6.

- Run the unchanged original 14 tests plus targeted adapter/contract tests. Run a headless main-scene smoke test and a standalone windowed build.
- Execute 50-action earth/water transfer scenarios, a channel/bank/overtopping scenario and a 10-minute soak. Distinguish legitimate groundwater input and the solver's dry-threshold losses from unexplained mass drift.
- Check repeated reset, paused editing, single-step durations, edge brushing, full/empty buffers, shallow cursor angles and scene reload cleanup.
- Measure water and fire step costs, mesh updates, allocations, druid scanning and VFX separately at the source's 129² resolution. Record hardware, engine build, 1920 × 1080 settings, average and slow-frame times.
- Use 60 FPS as an initial performance target and 30 FPS as the proposed floor on the recorded reference machine; these are targets, not measured results. Do not expand to 256² as a condition of port parity.
- If profiling identifies mesh upload as the bottleneck, consider a separate vertex-displacement change. Do not redesign the water solver without evidence and a distinct plan.
- Record known source defects and accepted noise/presentation deviations. Tag `port-v0.1` only after acceptance is complete and known limitations are explicitly recorded.

**Exit gate:** runnable standalone port; numerical gates green; integration checklist complete; no new untriaged crashes or invalid-state faults; visual and performance evidence stored in `docs/verification/`.

## 6. Verification commands and evidence

Planned commands, to run from the Godot project root **after P0 creates these projects and sets the engine path**:

```bash
dotnet build TheLevels.sln
dotnet test Tests/TheLevels.Tests.csproj --logger 'trx;LogFileName=port-tests.trx'
"$THELEVELS_GODOT_BIN" --headless --path . --editor --quit
"$THELEVELS_GODOT_BIN" --headless --path . --quit-after 120
"$THELEVELS_GODOT_BIN" --path .
```

`THELEVELS_GODOT_BIN` must point to the verified .NET executable. The last command is the interactive play-test. Headless startup cannot validate visible particles or rendering. The previous ADR's `-- --run-tests` argument is not a built-in test runner; use the NUnit command above unless a custom runner is explicitly implemented.

For each phase record: source baseline, changed files, commands and results, scenario steps, observed outcome, screenshots where relevant, measured costs and remaining deviations. Run the relevant gates once changes are complete; broaden testing when a failure or new change warrants it.

## 7. Completion checklist

- [x] P0: working Godot .NET/SDK, project wiring and startup. Completed 2026-09-11 (commit 0276a78). Evidence: `docs/verification/2026-09-11-p0.md`. Two environment gotchas recorded there: the macOS mono app has no embedded .NET runtime (`DOTNET_ROOT=/Volumes/External/DevExteralHD/dotnet` required), and the engine takes the project assembly name from `dotnet/project/assembly_name` in `project.godot` (not the csproj filename) — without it, every C# script fails with "associated class could not be found".
- [x] P1: original five heightfield tests and mapping/brush checks. Completed 2026-09-11 (commit 9cf18dd): 17/17 green (five originals with unchanged tolerances + 12 contract checks), 0 build warnings, Core references no engine assemblies.
- [ ] P2: original nine fire tests and documented source edge cases.
- [ ] P3: terrain, water, fire rendering and live diagnostics.
- [ ] P4: camera, targeting, four tools and global controls.
- [ ] P5: druid behaviour, haven, deaths and ritual completion.
- [ ] P6: native tool/ritual VFX and permitted asset provenance.
- [ ] P7: standalone verification, soak, profiling and release evidence.

No implementation, engine installation, test execution, MCP connection, or release is claimed by this planning document. The next concrete action is P0.
