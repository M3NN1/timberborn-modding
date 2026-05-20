# Where Wyrms Wait

Map authors place dormant **Wyrm Husks** (single-shot) and **Wyrm Dens**
(periodic spawners) anywhere on the map. They wake when the soil above
them turns green, and a hungry **Wyrm** climbs out to hunt the colony.
Killing a wyrm requires sustained badwater contamination — the wyrm
passively drinks the contaminated water it stands in, and once enough
poison has accumulated in its body it dies. Killing a den requires
vanilla dynamite to dig down to it and detonate.

> The cycle inverts: drought is safety, badtide is salvation, the green
> is danger.

This is a **late-game threat mod**. Husks placed deep in the terrain
need many days of continuous irrigation to wake, so the threat scales
naturally with the player's success at taming water.

## Status: v0.8.0 (Phases 1–6 in)

Working:

- **Husk + Den dormancy** with cover-depth-based wake timer
- **Wyrm spawning** through `WyrmFactory` at the topmost surface tile
  (shifts up to radius 2 if blocked; the picker checks the full block
  stack so wyrms never spawn inside floors or paths)
- **Wyrm AI**: hunts nearest beaver via vanilla `INavigationService`,
  inherits +1 step rule / path-blocker / water-swimming for free
- **Wyrm eats beavers** via `Mortal.DiePubliclyAsSoonAsPossible`
- **Wall-chewing** when hungry and pathing fails (chews only player-built
  blocks; natural terrain, husks, dens, and water sources are never
  chewable)
- **Badwater contamination kill via real water drain.** The wyrm uses
  vanilla `IWaterService.RemoveContaminatedWater` to drink the
  contaminated portion of its current water tile each sample. Dilution
  scales the kill rate; a static badwater puddle gets drained dry over
  time. Side effect: a wyrm fed flowing badwater purifies it into clean
  water as a byproduct of dying.
- **Wyrm Forager workshop** (Berries + Badwater → Soothesop)
- **Lure Stake placeable** with full hauler delivery via vanilla
  `Inventory` + `PublicInput`. Sated wyrms stop chewing walls (still
  eat beavers).
- **Wyrm Den** kill-by-dynamite via `ExplosionService.TilesExplosion`
- **Entity-panel fragments** for husk / wyrm / lure stake (UI Toolkit)
- **eMka settings panel**: master switch, wake speed, hunger rate,
  contamination resistance, sandbox mode
- Save/load round-trip on every component

Not yet shipped:

- **Real visuals.** All blueprints reference `.timbermesh` files that
  don't exist yet. In-game the entities are invisible cube placeholders.
- **Wyrm body procedural visuals.** No model = no animator = wyrms walk
  but you can't see them.

## Build

The asmdef declares its references explicitly (same style as DDD) so
external builds resolve consistently.

The mod depends on
[Mod Settings](https://steamcommunity.com/sharedfiles/filedetails/?id=3283831040)
(`eMka.ModSettings`) for the in-game settings panel — same dependency
as DDD; the workshop / mod manager installs it from `manifest.json`.

## See also

- `DESIGN.md` — full design document, all decisions tracked
- `Placeholders/README.md` — how to author real `.timbermesh` art
