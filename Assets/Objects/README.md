# Objects

`oak_tree.glb` — the basin's oak, from a Meshy AI generation of `oak_tree.png`.

**Nothing loads this yet.** The forest is `Core/Vegetation/ForestManager` plus
`View/FloraView`, which renders placeholder `CylinderMesh` trunks and
`SphereMesh` canopies. A measured spike putting this mesh on the 58 oak
instances took the scene from 100 to 44 FPS, under the 60 FPS target — see
below for why it will not simplify. The art pass needs a ~3-5 k-triangle oak,
and rowan and willow to match, or the real oaks just make the primitives look
worse. This file is kept as the input to that pass.

The Meshy source (`Meshy_AI_Oak_tree_0913080447_texture.glb`, 70 MB, 1.95 M triangles)
is **not** in git; it lives outside the repo at `../../../Meshy-sources/` with the
other raw generations. What is
committed is the game-ready reduction, produced with `@gltf-transform/cli`:

```
# --texture-size no-ops without sharp installed; the resize below is what shrinks them.
gltf-transform optimize <source> a.glb --simplify-error 0.004 \
    --compress false --texture-compress false
gltf-transform simplify a.glb b.glb --ratio 0.05 --error 0.05
gltf-transform resize   b.glb oak_tree.glb --width 1024 --height 1024
```

130 k triangles, three 1 K JPEGs, 7.4 MB. The simplifier will not go below that: the
Meshy mesh is split at every UV seam, so meshoptimizer has almost no collapsible edges.
Godot's import-time LOD generation covers the distance falloff, which is why
`View/TreeScatter.cs` uses one `MeshInstance3D` per tree rather than a `MultiMesh` —
a MultiMesh would draw LOD 0 for all sixteen.

Meshopt/Draco compression would halve the file, but Godot 4.7's glTF importer rejects
both `EXT_meshopt_compression` and `KHR_mesh_quantization`. Keep the plain buffers.

A second generation, `Meshy_AI_tree_0913080341_texture.glb`, was left out: one oak
species is enough scenery for node one.
