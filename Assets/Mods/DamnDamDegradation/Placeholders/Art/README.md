# Art Placeholders

This folder is the parking spot for damaged-dam art. v1 of DDD ships with no
custom textures — `DamDamageVisuals` tints the existing mesh yellow at the
warning threshold and red at the critical threshold via a
`MaterialPropertyBlock`, so you get visible feedback without any PNGs.

## What lives here

Drop placeholder images you want to iterate on:

- `dam_damaged_warning.png` — used at 30%–80% health. Suggested look:
  weathered, mossy, a few cracks. 1024×1024 albedo.
- `dam_damaged_critical.png` — used below 30%. Suggested look: split planks,
  visible water seepage tracks. 1024×1024 albedo.
- `levee_damaged_warning.png`, `levee_damaged_critical.png` — same idea for
  the smaller levee mesh. 512×512 is fine.
- `floodgate_damaged_*.png`, `doublefloodgate_damaged_*.png`,
  `triplefloodgate_damaged_*.png` — mechanical wear, rust on the metal bits.

PNGs in this folder are *not* loaded by the mod — they're reference art.
Loading them happens via an AssetBundle (see below).

## Wiring real art

When you're ready to replace the tint with texture swaps:

1. Create `AssetBundles/` next to `manifest.json` (mirror of
   `BeaverBehaviorBreakdown` and `BeaverHRDepartment`).
2. Build the bundle in Unity with the damaged textures tagged into it.
3. Replace the tint logic in `Scripts/Components/DamDamageVisuals.cs` with
   `MaterialPropertyBlock.SetTexture("_BaseMap", damagedTexture)` calls,
   loading the textures via
   `AssetBundle.LoadFromFile(Path.Combine(modEnvironment.ModPath, "AssetBundles", "..."))`.
4. Inject `IModEnvironment` into a singleton loader (currently
   `DamDegradationModStarter` ignores `modEnvironment`; that's the entry
   point).

The `IDamDeteriorationListener` contract — `OnEnterWarning`,
`OnEnterCritical`, etc. — stays the same. You're only swapping out the
implementation, not the integration.

## Why no asset bundle in v1

Asset bundles tie the mod to a specific Unity / Timberborn version and need
rebuilding on patch days. The tint approach is shader-property-only and
survives game updates without a rebuild. Once we have stable art that's
worth pinning, the bundle is justified.
