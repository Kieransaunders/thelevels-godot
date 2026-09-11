# The Levels — Unity → Godot Port Plan

> **Revision 3, 2026-09-11.** The previous plan built a voxel MVP from scratch. That is cancelled.
> A working Unity 6 prototype exists and is the specification. Read
> [the design ADR](2026-09-11-from-dust-mvp-design.md) first — it records why this is C#, why it
> is a heightfield, and which earlier options are closed.

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work task-by-task.

**Goal:** Reproduce the Unity prototype in Godot 4.7.2 .NET, with behaviour verified by the
prototype's own ported tests, then add the god-hand VFX that Unity's purchased packs can't
follow us into.

**Source of truth:** `/Volumes/External/DevExteralHD/The Levels Unity/Assets/TheLevels/`
Read the C# before porting each piece. Where this plan and that code disagree, the code wins.

**Target:** `/Volumes/External/DevExteralHD/1_Current /TheLevels/the-levels`

---

## Porting crib sheet

Applies to every task; don't re-derive it each time.

| Unity | Godot .NET |
|---|---|
| `MonoBehaviour` | `Node` / `Node3D` |
| `Awake()` | `_Ready()` |
| `Update()` | `_Process(double delta)` |
| `FixedUpdate()` | `_PhysicsProcess(double delta)` |
| `[SerializeField] private float x` | `[Export] private float _x` |
| `Time.deltaTime` | the `delta` parameter |
| `Time.realtimeSinceStartup` | **In the pure solver: `System.Diagnostics.Stopwatch`.** In the Node wrapper only: `Time.GetTicksUsec() / 1_000_000.0` |
| `Mathf.Clamp` / `Min` / `Max` | `Godot.Mathf.Clamp` / `Min` / `Max` — same names |
| `Vector3` | `Godot.Vector3` — both Y-up |
| `Debug.Log` | `GD.Print` |
| `Instantiate` / `Destroy` | `PackedScene.Instantiate()` / `QueueFree()` |
| `System.Array.Copy` / `Clear` | unchanged — plain BCL |

**⚠ Handedness.** Unity is left-handed, Godot right-handed. The Z axis flips. Any world↔grid
mapping (`SampleBilinear`, `ContainsWorldPosition`, brush placement, druid targets) must be
checked, not copied blind. A mirrored world is the most likely silent bug in this whole port.

**⚠ `Godot.Time` is an engine singleton — keep it out of the pure class.** The solver calls
`Time.realtimeSinceStartup` at `HeightfieldSimulation.cs:270` and `:387` to fill
`LastStepMilliseconds`, i.e. *inside* `StepWater`. Map that to `Stopwatch`, not `Godot.Time`:
under `dotnet test` there is no initialised engine, so every test that calls `StepOnce` would
fail with a Godot init error that looks nothing like an algorithm bug.

**⚠ `Mathf.PerlinNoise` has no output-identical Godot equivalent.** `GenerateSomersetTestMap`
uses it at `:400` for `lowUndulation`. Godot's `FastNoiseLite` is a different algorithm and will
produce a different map. Everything else in the generator is deterministic arithmetic
(`Mathf.Sin` channel at `:408`, `Mathf.Exp` falloff at `:409`, Gaussian tor at `:463`) and ports
exactly. Port `lowUndulation` as a small hand-rolled deterministic value-noise function rather
than calling any engine noise. The map will not be pixel-identical to Unity's; that is acceptable
because the undulation amplitude is `0.45` against terrain heights up to `18`, and the tests
assert *structure* ("the map contains a wet cell", "burnable reed beds exist", momentum develops
then settles) rather than exact heights. Do not skip this and reach for `FastNoiseLite`.

**One deliberate structural change.** In Unity the solver *is* a `MonoBehaviour`. Here, split it:
a plain C# class holding the math with no Godot node dependency, plus a thin `Node` wrapper that
owns `_Process` and `[Export]`s. This is what lets the ported tests run under `dotnet test`
without booting the engine. Nothing else about the algorithm changes.

---

### Task 0: Repo, engine, and .NET project

**Step 1: git**

```bash
cd "/Volumes/External/DevExteralHD/1_Current /TheLevels/the-levels"
git init
printf '.godot/\n.mono/\nbin/\nobj/\nreference/\n' >> .gitignore
git add -A && git commit -m "Initial commit: empty Godot 4.7 project"
```

**Step 2: install Godot 4.7.2 .NET**

```bash
cd /tmp
curl -L -o godot-mono.zip \
  "https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_macos.universal.zip"
unzip -q godot-mono.zip -d /tmp/godot-mono
mkdir -p "$HOME/Applications"
mv /tmp/godot-mono/*.app "$HOME/Applications/Godot-Mono.app"
xattr -dr com.apple.quarantine "$HOME/Applications/Godot-Mono.app"
export GODOT="$HOME/Applications/Godot-Mono.app/Contents/MacOS/Godot"
"$GODOT" --version     # expect 4.7.2.stable.mono
dotnet --version       # .NET SDK must be present; install if not
```

**Step 3: make it a C# project**

Open the project once in the editor so Godot generates `TheLevels.csproj` and the solution, or
create them via `Project > Tools > C# > Create C# solution`.

Verify: a trivial `Node` script in C# builds and prints on run.

**Step 4: commit**

```bash
git add -A && git commit -m "Add Godot .NET project scaffolding"
```

---

### Task 1: Port the heightfield solver (the crown jewel)

**Files:** `Simulation/HeightfieldSimulation.cs`, `Simulation/HeightfieldNode.cs`,
`Tests/HeightfieldSimulationTests.cs`, `TheLevels.Tests.csproj`

**Step 1: port the tests first (RED)**

Read `Assets/TheLevels/Tests/Editor/HeightfieldSimulationTests.cs` (131 lines) in full. Port all
five cases verbatim — they are the acceptance criteria:

- `ResetRestoresInitialMatterAndClearsBuffers`
- `EarthScoopAndDropConserveWorldPlusBufferVolume`
- `WaterPickupAndDropConserveWorldPlusBufferVolume`
- `WaterSolverConservesVolumeAndNeverCreatesNegativeDepth`
- `MomentumDevelopsThenSettlesOnPooledMap`

Keep the tolerances exactly as written (`Within(0.02)`, `Within(0.001)` etc.) — they were tuned
against this solver. Create a standard NUnit test project referencing the game project.

```bash
dotnet test    # expect: does not compile — HeightfieldSimulation does not exist. That is RED.
```

**Step 2: port the solver (GREEN)**

Read `Assets/TheLevels/Runtime/HeightfieldSimulation.cs` (480 lines) in full before writing
anything. Port as a plain C# class — no `Node` base — preserving exactly:

- fields `terrain`, `water`, `initialTerrain`, `initialWater`, `velX`, `velZ`, `outgoing`, `delta`
- the public surface: `Resolution`, `WorldSize`, `CellSize`, `CellArea`, `BrushRadius`,
  `EarthCapacity`, `WaterCapacity`, `EarthBuffer`, `WaterBuffer`, `Paused`, `StepCount`,
  `FixedStep`, `LastStepMilliseconds`, `MaxWaterSpeed`, `NegativeCorrections`,
  `InvalidValueCount`, `HeightClampCount`
- methods `Initialize`, `ResetSimulation`, `StepOnce`, `SampleTerrain`, `SampleWater`,
  `SampleSurface`, `ContainsWorldPosition`, `GetTerrain`, `GetWater`, `ApplyBrush`,
  `CalculateMetrics`, and private `StepWater`, `Surface`, `DonorScale`, `Index`, `SampleBilinear`,
  `GenerateSomersetTestMap`
- **the `StepWater` algorithm unchanged**: CFL clamp `dx/dt*0.5`, blocked-edge dry-cell rule,
  two-pass outgoing/donor-scale flux. Including the existing `ponytail:` note about the skipped
  overshoot term — carry the comment across, it documents a real ceiling.

```bash
dotnet test    # expect: 5/5 green
```

If `MomentumDevelopsThenSettlesOnPooledMap` fails, suspect the Z-axis handedness flip in
`GenerateSomersetTestMap` or `SampleBilinear` before suspecting the solver.

**Step 3: the Node wrapper**

`HeightfieldNode.cs`: a `Node` owning a `HeightfieldSimulation`, with `[Export]`s mirroring
Unity's `[SerializeField]`s, and `_Process` running the accumulator/substep loop from Unity's
`Update()` (`maxSubstepsPerFrame`, `simulationStep`).

**Step 4: commit**

```bash
git add -A && git commit -m "Port HeightfieldSimulation and its tests from Unity"
```

---

### Task 2: Render the heightfield

**Files:** `View/HeightfieldView.cs`, `Shaders/terrain.gdshader`, `Shaders/water.gdshader`

**Step 1:** Port `VertexColorTerrain.shader` (47 lines) and `VertexColorWater.shader` (90) to
Godot shading language. Both are vertex-colour driven, so the port is mostly syntax:
`v2f`/`appdata` → `varying`, `_Time.y` → `TIME`, `fixed4`/`half` → `vec4`.

**Step 2:** Port `HeightfieldView.cs` (197 lines). It holds **two** meshes (`terrainMesh`,
`waterMesh`), creates them in `Start()`/`CreateMeshes()` and rebuilds in `LateUpdate()` only when
a `dirty` flag is set — it does *not* rebuild unconditionally every frame. Preserve that:
`LateUpdate` → `_Process` (after the sim node, via process priority), `MarkDirty()` hooked to the
simulation's `StateChanged` event.

Unity `Mesh` → Godot `ArrayMesh`. Build the index buffer once; on rebuild, refill pre-allocated
`PackedVector3Array` / `PackedColorArray` from the simulation arrays, then `ClearSurfaces()` +
`AddSurfaceFromArrays()`.

```
// ponytail: CPU mesh rebuild, same as the Unity original, so behaviour is comparable and the
// port is verifiable. If 129² per frame shows up in the profile, move height to an R32F
// ImageTexture and displace in the vertex shader — that's the Godot-native path.
```

**Step 3: Verify.** Run windowed; screenshot. Expect the Somerset test map: flat wetland, a rhyne,
a tor. Compare against the Unity project running the same generator.

**Step 4:** `git commit -m "Port heightfield mesh view and terrain/water shaders"`

---

### Task 3: Camera and the earth/water tools

**Files:** `Player/StrategyCamera.cs`, `Player/WorldCursor.cs`

**Step 1:** Port `StrategyCameraController.cs` (70 lines) — WASD pan, Q/E rotate, ↑/↓ tilt, wheel
zoom. Define these in Godot's Input Map rather than reading raw keys.

**Step 2:** Port the earth and water half of `WorldCursorController.cs` (488 lines). Fire and
lightning come in Task 5. Needed now:

- screen ray → heightfield intersection (march the ray against `SampleSurface`; there is no
  physics collider)
- `1`/`2` select earth/water; hold LMB to scoop, RMB to drop, both calling
  `ApplyBrush(worldPos, matter, scoop, delta)`
- expose `SelectedTool`, `SelectedMatter`, `CursorPosition`, `HasTarget`

**Step 3: Verify.** Scoop earth, drop it in the rhyne, watch water reroute. This is the first
moment the game is playable — spend real time here comparing feel against the Unity build.

**Step 4:** `git commit -m "Port strategy camera and earth/water cursor tools"`

---

### Task 4: God-hand VFX (new work — nothing to port)

**Files:** `Vfx/GodHandVfx.cs`, `Scenes/god_hand.tscn`

The Unity packs (NamuFX, Matthew Guz AoE, Houidisoft) do not transfer. Built native instead —
no custom shader needed. See design ADR D5 for the verified feature list.

**Step 1: build the scene**

- golden pointer: small emissive sphere `MeshInstance3D`; glow enabled on `WorldEnvironment`
- trail: `GPUParticles3D`, `TrailEnabled = true`, `TrailLifetime ≈ 0.5`, white, drawn with a
  `RibbonTrailMesh`
- swirl: `GPUParticles3D` parented to the pointer, `ParticleProcessMaterial` with
  `OrbitVelocityMin/Max` for the spin and a slightly negative `RadialVelocityMin/Max` to draw
  matter inward; sphere emission shape
- `GPUParticlesAttractorSphere3D` at the pointer

**Step 2: drive it from the buffer**

```csharp
// ponytail: one number drives the whole effect — the sim already exposes it.
float fill = matter == MatterType.Water
    ? sim.WaterBuffer / sim.WaterCapacity
    : sim.EarthBuffer / sim.EarthCapacity;
_swirl.AmountRatio = fill;                                  // particle count, no restart
_processMaterial.EmissionSphereRadius = Mathf.Lerp(0.2f, 1.4f, fill);
_swirl.DrawPass1 = /* earth or water mesh/material */;
```

Textures from `reference/vfx-textures/` (CC0 — safe to ship). Patterns from
`reference/vfx-library/` (MIT). Do **not** ship anything out of `reference/gdquest-vfx/` — its
art is CC-BY-NC-SA.

**Step 3: Verify.** Hold scoop and screenshot at empty, half and full buffer — the swirl should
visibly thicken and widen. Confirm the trail follows the pointer.

**Step 4:** `git commit -m "Add native god-hand VFX driven by matter buffer fill"`

---

### Task 5: Fire and lightning

**Files:** `Simulation/FireSimulation.cs`, `Tests/FireSimulationTests.cs`, cursor tools 3–4

**Step 1:** Port `FireSimulationTests.cs` (227 lines) first — RED. It is the larger suite, with
**nine** cases, and it documents the spread and extinguish rules:
`InitialMapHasBurnableReedBeds`, `LightningStrikeIgnitesDryReeds`, `WetGroundShrugsOffLightning`,
`FireSpreadsToNeighbouringCells`, `WaterDouseExtinguishesFire`, `FireBurnsOutAndCharresTheGround`,
`ScoopingFireFillsEmberBufferAndClearsFlames`, `DroppingEmbersKindlesNewGround`,
`ResetFireClearsFlamesAndEmbers`.

**Step 2:** Port `FireSimulation.cs` (337 lines): `fuel`/`fire`/`charred` arrays over the same
grid; `Ignite`, `Strike`, `ApplyFireBrush`, `StepOnce`, `EmberBuffer`/`EmberCapacity`,
`BurningCells`/`CharredCells`. `dotnet test` → all 14 cases across both suites green (5 heightfield + 9 fire).

**Step 3:** Add tools `3` (fire — scoop embers, hurl them) and `4` (lightning — refuses wet
ground, boils a splash off open water) to `WorldCursor`.

**Step 4:** `git commit -m "Port fire simulation, tests, and fire/lightning tools"`

---

### Task 6: Druids

**Files:** `Agents/DruidManager.cs`, `Scenes/druid.tscn`

Port `DruidManager.cs` (442 lines). This is a re-author, not a translation — it is
`Transform`/`GameObject`/`Material` throughout. Preserve the *behaviour*: eight druids spawn east
of the rhyne, walk to the standing stones, wade shallow water, detour around deep water and
flame, drown when submerged too long, burn when fire reaches them, and complete the ritual when
every living druid reaches the circle. Keep `RitualComplete`, `AliveCount`, `Total`,
`HavenCenter`, `ResetAll`, and the per-agent `Tick(fireThreatCenter, panicRadius)`.

`DruidAgent.SharedMaterial(color, smoothness)` → a shared `StandardMaterial3D`; capsules are fine.

**Verify:** bridge a channel with earth and see them across; flood it and watch one drown.

`git commit -m "Port druid agents and ritual completion"`

---

### Task 7: Diagnostics HUD and end-to-end

**Step 1:** Port `SimulationDiagnostics.cs` (104 lines) to a `CanvasLayer`; `F1` toggles.
Surface `StepCount`, `LastStepMilliseconds`, `MaxWaterSpeed`, `InvalidValueCount`,
`NegativeCorrections`, `BurningCells`, `AliveCount`.

**Step 2:** Port the remaining global controls from the Unity README: `R` reset, `Space` pause,
`N` single-step while paused.

**Step 3: Acceptance pass.** Against the Unity README's own description:

1. `dotnet test` — 14/14 green
2. tools `1`–`4` select earth / water / fire / lightning
3. scoop and drop both matters; water flows downhill and pools
4. fire spreads on dry reed, is doused by water, leaves char
5. lightning refuses wet ground, splashes on open water
6. druids reach the circle when bridged; drown when flooded; ritual completes
7. `R` / `Space` / `N` / `F1` behave
8. **no mirrored world** — compare a screenshot against the Unity build side by side

**Step 4:** `git tag port-v0.1`

---

## Cleanup (any time)

The voxel investigation is dead. When convenient:

```bash
rm -rf reference/godot_voxel-docs reference/voxelgame   # ~85MB, re-clonable
```

Leave `reference/vfx-library`, `reference/vfx-textures`, `reference/gdquest-vfx`.
