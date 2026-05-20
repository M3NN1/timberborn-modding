# Placeholder Assets

This folder is the parking spot for art that doesn't ship as binary
`.timbermesh` yet — sculpts, source `.fbx`, source `.blend`, reference
PNGs. Real game-ready meshes go next to their blueprint inside
`Data/Buildings/...` as `.timbermesh` files.

## What ships in v0.1.0

The mod has **no `.timbermesh` files yet**. The blueprints reference
paths that don't exist on disk:

```
Data/Buildings/Hazards/WyrmHusk/WyrmHusk.Common.Model.timbermesh   <- NOT SHIPPED
Data/Buildings/Hazards/WyrmHusk/WyrmHuskIcon.png                   <- NOT SHIPPED
```

That means in-game the husk will appear as a missing-mesh placeholder
(usually an untextured cube) and the icon slot will be blank. The
gameplay logic still runs — the husk dormancy ticker, surface moisture
probe, and warmup math don't need a mesh.

This is intentional for v0.1.0. The wave plan:

1. **v0.1**: invisible husks, gameplay only. Useful for testing the wake
   cycle and cover-depth math without arting first.
2. **v0.2**: simple sculpted husk mesh + icon.
3. **v0.3**: wyrm body, dirt particles, lure stake, wyrm forager.

## How to author a `.timbermesh` (workflow reference)

1. **Sculpt in Blender** (or your tool of choice). Reference scale: a
   1×1×1 husk should fit inside a 1×1×1 Timberborn block.
2. **Export `.fbx`** with applied transforms, single mesh, no materials
   embedded.
3. **Open the Unity project** (`workspace2`) in the Unity Editor.
4. **Import the `.fbx`** into `Assets/Mods/WhereWyrmsWait/Placeholders/`
   first, so it lives outside the shipped folder.
5. **Create / assign a Material** — for placeholder use, copy one of the
   `.mat` files from `Assets/Mods/ShantySpeaker/Placeholders/` (e.g.
   `BaseWood_DarkBrown.IronTeeth.mat`). For real art, build a material
   that matches the Folktails palette.
6. **Run the Timberborn mesh export tool**. The official Timberborn mod
   SDK ships a menu item under `Timberborn → Export Selected as
   Timbermesh` (the exact path can shift between SDK versions; check the
   ShantySpeaker readme or the SDK release notes for the current name).
   Select the imported mesh first, then run the export.
7. **Save the resulting `.timbermesh`** to
   `Assets/Mods/WhereWyrmsWait/Data/Buildings/Hazards/WyrmHusk/`
   with the exact name `WyrmHusk.Common.Model.timbermesh` (matching the
   `TimbermeshSpec.Model` path in the blueprint).
8. **Author the icon**: 256×256 PNG showing the husk silhouette. Save as
   `WyrmHuskIcon.png` in the same folder.
9. **Reload the game.** The husk should now have a real mesh.

## Naming contract

The path in `WyrmHusk.Common.blueprint.json` →
`Children.#Finished.TimbermeshSpec.Model` is

```
Buildings/Hazards/WyrmHusk/WyrmHusk.Common.Model
```

The engine resolves that against the mod's `Data/` root, so the
on-disk file must be at

```
Data/Buildings/Hazards/WyrmHusk/WyrmHusk.Common.Model.timbermesh
```

— extension implicit. Same convention for the icon
(`LabeledEntitySpec.Icon`):

```
Buildings/Hazards/WyrmHusk/WyrmHuskIcon
↓
Data/Buildings/Hazards/WyrmHusk/WyrmHuskIcon.png
```

## Why no procedural runtime art

Earlier mods in this project (DDD's `DamLeakEffects`,
`DamDamageDecals`) generate textures and particle systems at runtime to
avoid the AssetBundle / Unity-rebuild pipeline. That's great for
*decoration* layered on top of an existing mesh, but a `BlockObject`
needs a `.timbermesh` to render at all — the engine reads it in
`TimbermeshSpec` very early in instantiation, and there's no obvious
hook to substitute a procedural mesh without forking the model loader.

So:

- **Decoration / overlays / particles**: still procedural. Same trick
  DDD uses. No bundle.
- **Base block meshes**: `.timbermesh`. No way around it.

The wyrm body itself (Phase 2) is in a grey zone: it's a moving entity,
not a `BlockObject`, so we may be able to spawn the body procedurally
(capsule chain) for v1 and replace with a sculpted skinned mesh in v2.
That's deferred to Phase 2 design.

## Vanilla status sprite IDs (placeholder, ship-blocker for v1.0)

`WyrmStatusIndicator` reuses three vanilla status-icon sprite names
because the mod doesn't yet ship its own:

| State    | Sprite ID                          |
|----------|------------------------------------|
| Sated    | `LackOfResources`                  |
| Hunting  | `GenericError`                     |
| Poisoned | `BuildingBlockedByContamination`   |

These IDs are stable as of Timberborn 1.0.x but are owned by Mighty
Yak — a future game patch could rename or remove any of them, silently
breaking the floating icons over wyrms. **Before tagging v1.0**, author
three mod-owned sprites and swap the constants in
`Scripts/Wyrm/WyrmStatusIndicator.cs`. Sprite naming follows the same
`Sprites/StatusIcons/<Name>` convention as vanilla; ship them via the
mod's AssetBundle.
