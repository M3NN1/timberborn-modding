# Art Placeholders

This folder is the parking spot for damaged-dam art. v1 of DDD ships without
custom textures — `DamDamageVisuals` tints the existing mesh yellow at the
warning threshold and red at the critical threshold via a
`MaterialPropertyBlock`, so you get visible feedback without any image files.

## Why no PNGs in the shipped mod

After Unity imports the project, textures don't live as `.png` anymore. Unity
packs them into AssetBundles as binary `Texture2D` data (BCn/DXT compressed).
The vanilla Timberborn install has the same shape: `.json` blueprints,
`.blend` models, DLLs, and AssetBundles — but no loose PNGs. So if you want
real damaged-dam art in this mod, you can't just drop a PNG into a folder
and expect the game to pick it up.

## What lives here

This folder is for *source* iteration art only. Drop PNGs you're sketching:

- `dam_damaged_warning.png` — used at 30%–80% health. Suggested look:
  weathered, mossy, a few cracks. 1024×1024 albedo.
- `dam_damaged_critical.png` — used below 30%. Suggested look: split planks,
  visible water seepage tracks. 1024×1024 albedo.
- `levee_*.png`, `floodgate_*.png`, `doublefloodgate_*.png`,
  `triplefloodgate_*.png` — same idea per building. Smaller meshes can use
  512×512.

These PNGs are *not* loaded by the mod. They sit here as reference for the
day someone builds the AssetBundle.

## Three paths to real damaged textures

### Path 1 — Ship a custom AssetBundle (the proper way)
1. In Unity, import a PNG from this folder as a `Texture2D`.
2. Assign it to a new AssetBundle via the `AssetBundle` dropdown in the
   Inspector.
3. Run `BuildPipeline.BuildAssetBundles(...)`.
4. Drop the resulting binary bundle file (no extension) into
   `Assets/Mods/DamnDamDegradation/AssetBundles/`. See `BeaverHRDepartment`
   as a layout reference.
5. Load at runtime via
   `AssetBundle.LoadFromFile(Path.Combine(modEnv.ModPath, "AssetBundles", "damvisuals"))`
   — needs `IModEnvironment` injected into a singleton; entry point is
   `DamDegradationModStarter.StartMod`.
6. Replace the tint logic in `DamDamageVisuals` with
   `MaterialPropertyBlock.SetTexture(_BaseMap, loadedTexture)`.

This is the only way to get real new textures into the running game.

### Path 2 — Reuse vanilla textures with an overlay
The vanilla game bundles contain the dam/levee/floodgate albedo textures.
Loading them via `Resources.Load<Texture2D>("...")` and blending a small
decal layer (cracks, moss) over them keeps your mod's bundle tiny. The
exact resource path needs to be inspected from a vanilla AssetBundle dump.

### Path 3 — Stay with the runtime tint (current default)
No PNG, no bundle, no Unity rebuild. Survives Timberborn patches without
any work from us. The visual signal is clear enough that players notice
the dam is in trouble. Recommended until the mod has stable users who
specifically ask for proper damaged art.

## Why I'm in no rush to do Path 1

AssetBundles tie the mod to a specific Unity version + Timberborn shader
pipeline. A patch that changes either breaks the bundle and requires a
rebuild. Until the mod has actual playtester feedback worth pinning, the
runtime tint approach is more robust — it only depends on the public
`Renderer` and `MaterialPropertyBlock` APIs, which Unity has kept stable
for many years.
