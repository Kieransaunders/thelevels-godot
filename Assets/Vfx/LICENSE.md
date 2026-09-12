# Assets/Vfx — provenance and licences

Shipped VFX assets, each with its source and licence. Nothing else in the game
loads from `reference/` (which is gitignored and `.gdignore`d); the exit gate
for P6 is zero shipped dependency on it.

## textures/ — RPicster, Godot particle and VFX textures

| File | Source file in the pack |
|---|---|
| `textures/glow_soft.png` | `textures/256/alpha/spotlight_1.png` |
| `textures/spark_star.png` | `textures/256/alpha/spotlight_5.png` |
| `textures/burst_streaks.png` | `textures/256/alpha/effect_2.png` |

- Source: https://github.com/RPicster/Godot-particle-and-vfx-textures
  (pack commit present in `reference/vfx-textures/` on 2026-09-12)
- Licence: **CC0 1.0 Universal** (pack `LICENSE` and README: "The whole project
  is licensed as CC0"). No attribution required; credited here anyway as a
  courtesy to Raffaele Picca — raffaelepicca.com.
- Files are alpha versions, 256×256, unchanged apart from the rename.

## Deliberately not copied

- `reference/gdquest-vfx/` — code is MIT, but its textures and models are
  CC-BY-NC-SA 4.0 (non-commercial, share-alike): technique reference only,
  nothing ships from it.
- `reference/vfx-library/` — MIT, but nothing in it fitted the effects we
  needed better than native `ParticleProcessMaterial` setups; no copies made.
- `reference/druid`, concept imagery — outside VFX scope, provenance unclear.
