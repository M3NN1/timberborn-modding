# Where Wyrms Wait — Design Document

A Timberborn mod adding a late-game predator threat: dormant wyrms
placed by map authors that wake when long-irrigated land covers them,
hunt and eat beavers, and can only be killed by sustained badwater
contamination.

## Lore

Wyrms slumber in dry, ancient earth. They wake when the world above
grows green again, and they cannot survive contaminated water. Beavers
can heal the same poison through their wells; the wyrm cannot. The
cycle inverts: drought is safety, badtide is salvation, the green is
danger.

## High-level mechanics

- Map authors place **Wyrm Husk** (single dormant, 1×1×1) and
  **Wyrm Den** (periodic spawner, 2×2×2) tiles anywhere on the map at
  any 3D coordinate.
- A husk/den wakes when the topmost natural-ground tile of any of its
  footprint columns has positive moisture (vanilla green/grass signal).
- Wake timer = `BaseWarmupDays + DaysPerCoverBlock × cover_depth`
  in-game days. Defaults: 0.5 + 3 × depth. No hard cap — map authors
  choose the depth, the global Wake Speed setting (50–200%) scales for
  difficulty.
- If the surface goes brown before the timer expires, the timer resets
  to zero. Same depth → same timer next attempt.
- After warmup, a **Wyrm** emerges at the topmost surface tile of one
  of the husk/den's footprint columns. If that tile is occupied
  (e.g. by a building), the picker spirals outward up to
  `EmergenceShiftRadius` tiles for an open surface tile; a 2×2×2 den
  also tries each of its four top columns in turn before giving up.
  If no column resolves, the wake is suppressed for that cycle.
- Active wyrms hunt beavers via vanilla `INavigationService`, eating
  them on contact. A kill notification is posted.
- Wyrms inherit beaver navigation rules for free: ≤1 step climb,
  vanilla path-blockers honored, normal water swimmable.
- **Sustained badwater contamination kills wyrms.** The wyrm passively
  drinks the contaminated portion of whatever water tile it sits in
  each sample tick. Drinking is a real water-system change — the
  badwater is removed from the column. The depth of badwater drunk
  goes into the wyrm's contamination bucket; when the bucket reaches
  the lethal threshold, the wyrm dies.
- **Wyrm Husk**: spawns one wyrm and is consumed. When the wyrm dies,
  the husk is gone forever for that game.
- **Wyrm Den**: spawns wyrms periodically while green, up to a per-den
  cap. Killed by digging through the cover with vanilla **dynamite**
  until a blast hits the den itself. Once destroyed, gone forever.
- **Wyrm Forager** workshop: brews **Soothesop** from Berries +
  Badwater.
- **Soothesop**: hauled good. While stocked at a Lure Stake within
  range of a wyrm, the wyrm is in **Sated** state.
- **Lure Stake**: small player-built placeable with an inventory.
  Haulers deliver Soothesop via vanilla `PublicInput` plumbing — no
  custom hauling code.
- **Sated wyrm**: still eats beavers and is still poisoned by badwater,
  but stops chewing player-built walls.
- **No beaver flee response.** The player owns the responsibility of
  keeping beavers out of the wyrm zone.

## Contamination kill model

The wyrm has a single damage pool: an internal contamination counter
that fills as it absorbs badwater and drains slightly while it isn't
absorbing. There is no separate HP bar.

```
Each sample tick (every ~0.8s game time):
  coord       = wyrm's grid position
  waterDepth  = WaterMap.WaterDepth(coord)
  if waterDepth ≤ 0:                                  // dry tile
      regen contamination by spec.ContaminationRegenPerDay × Δdays
      return
  concentration = WaterMap.ColumnContamination(coord) // 0..1
  available    = waterDepth × concentration           // contaminated portion
  wanted       = spec.DrinkDepthPerDay × Δdays
  drunk        = min(wanted, available)               // engine clamps too
  if drunk > 0:
      WaterService.RemoveContaminatedWater(coord, drunk)   // real water change
      contamination += drunk
  else:
      regen contamination by spec.ContaminationRegenPerDay × Δdays

If contamination ≥ spec.LethalContamination × settings.resistance:
    wyrm dies
```

Three things fall out of routing through vanilla `IWaterService`:

1. **Concentration scales lethality automatically.** A 50/50 mix has
   half the contaminated water in the column to drink, so the wyrm
   absorbs at half speed. Pure badwater kills fastest.
2. **Killing a wyrm requires flowing badwater.** A static puddle gets
   drunk dry; once empty, the wyrm regenerates and walks away.
   Sustained inflow is the design pillar.
3. **Wyrms purify badwater as a side effect of dying.** Players who
   route badwater through a wyrm zone end up with cleaner water
   downstream. Treat this as a deliberate emergent verb, not an
   exploit — the player traded the wyrm-fight infrastructure for a
   one-shot purification, and wyrms come from finite husks/killable
   dens.

## State machine

```
Wyrm Husk / Wyrm Den
────────────────────
Dormant ──── topmost natural ground in column has Moisture > 0 ──> Warming
   ▲
   │ surface goes brown ──── (timer resets)
   │
Warming ──── timer ≥ warmupDays ──> Emerging ──> Active (Wyrm)
   │
   └─ surface goes brown ──> Dormant (timer = 0)

Wyrm
────
Active ── hunger ↑, hunts nearest beaver ──> Eat ──> Active
Active ── stocked Lure Stake in range ──> Sated (no wall damage)
Active ── on contaminated water ──> drinks badwater, contamination ↑
       └─ otherwise ──> contamination ↓ (regen)
Active/Sated ── contamination ≥ lethal ──> Dies
```

## Numerical defaults

Spec values are per-blueprint (overridable per-husk/per-den/per-stake
by map authors). Settings-panel multipliers stack on top.

| Parameter | Default | Source | Notes |
|---|---|---|---|
| Base warmup days | 0.5 | `WyrmHuskSpec.BaseWarmupDays` | per husk/den |
| Days per cover block | 3 | `WyrmHuskSpec.DaysPerCoverBlock` | linear depth scaling |
| Emergence shift radius | 2 | `WyrmHuskSpec.EmergenceShiftRadius` | tiles |
| Den spawn cooldown | 6 days | `WyrmDenSpec.DaysBetweenSpawns` | between spawns |
| Den live-wyrm cap | 3 | `WyrmDenSpec.MaxLiveWyrms` | per-den ceiling, owner-tracked |
| Drink depth / day | 1.0 | `WyrmSpec.DrinkDepthPerDay` | water-depth units, pure badwater |
| Lethal contamination | 5.0 | `WyrmSpec.LethalContamination` | depth-units; ~5 days pure badwater |
| Contamination regen / day | 0.2 | `WyrmSpec.ContaminationRegenPerDay` | when not drinking |
| Hunger per day | 1.0 | `WyrmSpec.HungerPerDay` | scaled by settings |
| Wall chew duration | 1.5 days | `WyrmSpec.BlockChewDays` | per block, hungry only |
| Walk speed | 1.0 u/s | `WyrmSpec.WalkSpeed` | slower than beavers |
| Lure Stake capacity | 5 | `LureStakeSpec.Capacity` | Soothesop units |
| Satiate radius | 3 | `LureStakeSpec.SatiateRadius` | tiles |
| Soothesop consumption | 1/wyrm/day | `LureStakeSpec.ConsumptionPerWyrmPerDay` | per wyrm in range |

Settings-panel multipliers (player-tunable via eMka, default 100%):

| Setting | Range | Effect |
|---|---|---|
| Mod enabled | on/off | master switch |
| Wake speed | 50–200% | × on warmup and den cooldown |
| Hunger rate | 10–300% | × on hunger accumulation |
| Contamination resistance | 25–400% | × on lethal threshold |
| Sandbox mode | on/off | wyrms walk but don't hurt anything |

## Player verbs (counterplay summary)

- **Sustain a badwater stream over the wyrm zone.** The wyrm drinks
  the contaminated water as it flows in, accumulates poison, and
  eventually dies. As a bonus the downstream water comes out cleaner.
- **Wall it off** (vanilla levees from above) and supply Soothesop via
  Lure Stakes to keep it sated. Permanent logistics tax.
- **Dynamite the den**: dig down with vanilla dynamite and blast the
  den itself. Permanent removal.
- **Don't send beavers near it.** The cheapest counterplay is care.

## Build/cost decisions baked in

- Soothesop ingredient cost (Berries + Badwater) trades food output
  and badwater plumbing for containment. Pacifying wyrms is not free,
  and badwater becomes a shared resource between killing wyrms
  (channeling) and pacifying them (Soothesop).
- Map authors choose depth → choose how late-game the threat is.
  Depth 0 husks are early-game scares; depth 9 husks need a stable
  long-irrigation setup, i.e. mid-to-late game.

## Architecture (mirrors DDD's structure)

```
Mods/WhereWyrmsWait/
├── manifest.json
├── WhereWyrmsWait.asmdef
├── README.md
├── DESIGN.md                                     (this file)
├── Placeholders/
│   └── README.md                                 art-replacement workflow
├── Data/
│   ├── Buildings/
│   │   ├── Hazards/                              map-author-only blueprints
│   │   │   ├── WyrmHusk/
│   │   │   └── WyrmDen/
│   │   ├── Production/
│   │   │   └── WyrmForager/                      workshop
│   │   ├── Tools/
│   │   │   └── LureStake/                        small placeable
│   │   └── Creatures/
│   │       └── Wyrm/                             non-placeable creature template
│   ├── Goods/
│   │   └── Good.Soothesop.blueprint.json
│   ├── Recipes/
│   │   └── Recipe.Soothesop.blueprint.json
│   └── Localizations/
│       └── enUS_WhereWyrmsWait.csv
└── Scripts/
    ├── Configuration/
    │   ├── WyrmsModStarter.cs                    IModStarter
    │   ├── WyrmsConfigurator.cs                  Bindito wiring + decorators
    │   │                                          + WyrmsSettingsConfigurator
    │   └── WyrmsDiagnosticsConfigurator.cs       LogOnce / debug-only bindings
    ├── Core/
    │   ├── WyrmSettings.cs                       eMka ModSettingsOwner
    │   ├── WyrmRegistry.cs                       live-wyrm set + add/remove events
    │   ├── WyrmNotifications.cs                  NotificationBus wrapper
    │   └── WyrmDiagnostics.cs                    LogOnce helper
    ├── Hazards/
    │   ├── IWyrmHazard.cs                        shared husk/den surface
    │   ├── WyrmHuskSpec.cs                       per-husk tunables
    │   ├── WyrmHusk.cs                           dormancy ticker + wake logic
    │   ├── WyrmDenSpec.cs                        per-den tunables
    │   ├── WyrmDen.cs                            den state machine + dynamite kill
    │   ├── WyrmEmergencePicker.cs                emergence-tile shift logic
    │   └── HazardEmergenceVisual.cs              warmup-puff particles on the surface
    ├── Wyrm/
    │   ├── WyrmSpec.cs                           creature-template tunables
    │   ├── WyrmComponent.cs                      hunger / contamination / save
    │   ├── WyrmFactory.cs                        spawns wyrms from the template
    │   ├── WyrmMovement.cs                       INavigationService-based walker
    │   ├── WyrmHunter.cs                         retarget + eat-on-contact
    │   ├── WyrmWanderer.cs                       random-walk fallback when not hunting
    │   ├── WyrmContaminationSampler.cs           per-tick water-drink driver
    │   ├── WyrmSatiationDetector.cs              per-tick lure-stake probe + drain
    │   ├── WyrmWallEater.cs                      stuck-while-hungry chew loop
    │   ├── WyrmStatusIndicator.cs                floating sated/hunting/poisoned icon
    │   └── WyrmEmergenceDirtEffect.cs            sustained dirt burst on spawn
    ├── Lure/
    │   ├── LureStakeSpec.cs                      capacity / radius / consumption
    │   ├── LureStake.cs                          inventory facade + drain helper
    │   ├── LureStakeRegistry.cs                  set lookup for wyrms in range
    │   ├── LureStakeInventoryInitializer.cs      dedicated decorator wiring
    │   └── LureStakeBaitVisual.cs                stock-tier bait sub-mesh switcher
    └── UI/
        ├── WyrmPanelStyle.cs                     shared inline panel palette
        ├── WyrmHuskFragment.cs                   warmup progress + state label
        ├── WyrmDenFragment.cs                    warmup + spawn-cooldown + live count
        ├── WyrmFragment.cs                       hunger + contamination bars
        └── LureStakeFragment.cs                  Soothesop stock bar
```

## Visual assets

The mod ships finished `.timbermesh` files and icons for all
buildings and the wyrm itself:

```
Data/Buildings/Hazards/WyrmHusk/WyrmHusk.Common.Model.timbermesh
Data/Buildings/Hazards/WyrmDen/WyrmDen.Common.Model.timbermesh
Data/Buildings/Production/WyrmForager/WyrmForager.Common.Model.timbermesh
Data/Buildings/Tools/LureStake/LureStake.Common.Model.timbermesh
Data/Buildings/Creatures/Wyrm/Wyrm.Common.Model.timbermesh
```

with matching icons (`WyrmHuskIcon.png`, `WyrmDenIcon.png`,
`WyrmForagerIcon.png`, `LureStakeIcon.png`, `WyrmIcon.png`) and the
Soothesop good icon under `Data/Sprites/Goods/`.

Procedural visuals on top of the meshes:

- `HazardEmergenceVisual` — particle puffs on a husk/den's emergence
  tile that intensify with `WarmupFraction`; cadence-and-size scale
  from a slow trickle below 5 % to a constant cloud above 95 %.
- `WyrmEmergenceDirtEffect` — sustained dirt eruption around the wyrm
  for ~2.5 s after spawn; shares its procedural soft-dot texture with
  the hazard puffs via `WyrmEmergenceDirtEffect.GetSharedDirtMaterial`.
- `LureStakeBaitVisual` — toggles three bait sub-meshes
  (`Bait_Low`/`Bait_Mid`/`Bait_Full`, declared as `#Finished.Children`
  in the blueprint) based on Soothesop stock fraction.

There is an AssetBundle pipeline under `AssetBundles/Resources/`
shipping the per-blueprint materials (`Wyrm_Base.mat`,
`WyrmHusk_Base.mat`, `WyrmDen_Base.mat`); procedural particle effects
fall back to `Sprites/Default` / `Particles/Standard Unlit` /
`Universal Render Pipeline/Particles/Unlit` so they work without a
project-specific shader.

The replacement workflow (sculpt → export `.timbermesh` → drop into
`Data/`) is documented in `Placeholders/README.md` for future model
revisions and the bait sub-meshes.

The `WyrmStatusIndicator` reuses three vanilla status sprite IDs
(`LackOfResources`, `GenericError`, `BuildingBlockedByContamination`)
as placeholders for the entity-panel status icons. These show in the
panel's status row only (no floating world-space icon — we use
`CreateNormalStatus`, not the `…WithFloatingIcon` variants). This is a
v1.0 ship-blocker — real mod-owned sprites should replace them
before tagging.

## Save / load

Persisted per-entity:

- `WyrmHusk`: warmup days
- `WyrmDen`: warmup days, spawn cooldown, warmed-up flag, pending-kill
  flag (so an explosion mid-tick survives a save)
- `Wyrm` (`WyrmComponent`): hunger, contamination, sated flag,
  Transform position + rotation
- `LureStake`: stocked Soothesop count (via vanilla `Inventory` save —
  the stake itself owns no save keys)

Not persisted:

- `WyrmRegistry`: rebuilt from per-wyrm `IInitializableEntity`
  callbacks on load.
- `WyrmDen._trackedWyrms`: rebuilt from a one-shot scan in
  `InitializeEntity` plus the radius reconcile loop.

Save compatibility: the mod is pre-release, so save-format breaks are
fair game until v1.0. After v1.0, ComponentKey strings will be frozen
and future versions will add new keys with `BackwardCompatible`
markers rather than rename.

## Implemented features (as of v0.9.0)

- ✅ Husk + Den dormancy with cover-depth wake timer
- ✅ Surface moisture probe (vanilla `ISoilMoistureService`) — den
  probes its bottom-layer footprint columns to match the column
  ceiling exactly, so 2×2×2 dens wake reliably.
- ✅ Husk + Den entity-panel fragments (warmup status, progress bar;
  den additionally shows the spawn-cooldown bar and live-wyrm count)
- ✅ Procedural emergence-puff visuals on both husks and dens
- ✅ Wyrm spawning via `WyrmFactory` from a creature template blueprint
- ✅ Wyrm AI: hunt nearest beaver via `INavigationService`, eat on
  contact
- ✅ Wall-chewing while hungry and pathing fails (player-built blocks
  only)
- ✅ Real-water-drain contamination kill (concentration-scaled, drains
  the puddle, no separate HP)
- ✅ Wyrm Forager workshop with Berries + Badwater Soothesop recipe
- ✅ Lure Stake placeable with hauler delivery via vanilla
  `PublicInput`
- ✅ Wyrm Den dynamite kill via `ExplosionService.TilesExplosion`
- ✅ Entity-panel fragments for husk / den / wyrm / lure stake
- ✅ eMka settings panel (master switch, wake speed, hunger,
  contamination resistance, sandbox mode)
- ✅ Save/load round-trip on every persistent component

## Not implemented (deliberate gaps for later phases)

- ❌ **Mod-owned floating-icon sprites** for wyrm status (still using
  vanilla sprite IDs as placeholders). Last v1.0 ship-blocker.
- ❌ Map-preview integration, multi-language localization beyond
  English, faction-themed wyrm variants. All explicitly out of v1
  scope.

## Open balance questions

These are knobs, not architecture. Defaults above; players retune via
the eMka panel during play.

- Lure Stake range and capacity
- Soothesop consumption rate
- Drink depth / day and lethal threshold
- Hunger rate
- Wall chew rate
- Wake-speed multiplier

## Dependencies

- Timberborn engine (`BlockSystem`, `Navigation`, `WaterSystem`,
  `SoilMoistureSystem`, `Explosions`, `MortalSystem`,
  `InventorySystem`, `Workshops`, `EntityPanelSystem`, `StatusSystem`,
  `TickSystem`, `TimeSystem`, `WorldPersistence` — full list in
  `WhereWyrmsWait.asmdef`)
- `eMka.ModSettings` mod (declared in `manifest.json`, same dependency
  as DDD)
- No new third-party dependencies

## Naming reference

- Mod: **Where Wyrms Wait** (WWW)
- Dormant single: **Wyrm Husk**
- Spawner: **Wyrm Den**
- Active creature: **Wyrm**
- Workshop: **Wyrm Forager**
- Anti-rage good: **Soothesop**
- Bait placeable: **Lure Stake**
