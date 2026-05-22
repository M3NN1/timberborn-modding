using System.Collections.Generic;
using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.EntitySystem;
using Timberborn.Explosions;
using Timberborn.Persistence;
using Timberborn.SoilMoistureSystem;
using Timberborn.TerrainSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Persistent wyrm spawner. Behaves like a <see cref="WyrmHusk"/> for
    /// wake-up purposes (same surface-moisture rule, same cover-depth
    /// math), but instead of producing one wyrm and self-deleting, the
    /// den keeps spawning while green and stays in the world until killed.
    /// <para>
    /// Kill path: subscribe to <c>ExplosionService.TilesExplosion</c>.
    /// Any blast that hits the den's coordinate deletes it. Players dig
    /// down through the natural cover with vanilla dynamite to reach
    /// the den, then a final blast destroys it. No custom dynamite
    /// integration required — the engine event is the integration.
    /// </para>
    /// </summary>
    public class WyrmDen : TickableComponent,
        IInitializableEntity, IPostInitializableEntity, IPersistentEntity, IDeletableEntity, IWyrmHazard
    {
        private static readonly ComponentKey SaveKey = new ComponentKey("WyrmDen");
        private static readonly PropertyKey<float> WarmupDaysKey =
            new PropertyKey<float>("WarmupDays");
        private static readonly PropertyKey<float> SpawnCooldownDaysKey =
            new PropertyKey<float>("SpawnCooldownDays");
        private static readonly PropertyKey<bool> WarmedUpKey =
            new PropertyKey<bool>("WarmedUp");
        private static readonly PropertyKey<bool> PendingDeletionKey =
            new PropertyKey<bool>("PendingDeletionByExplosion");

        private const int SurfaceRecomputeEveryTicks = 16;

        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;
        private readonly ISoilMoistureService _soilMoistureService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly WyrmSettings _settings;
        private readonly WyrmFactory _wyrmFactory;
        private readonly WyrmEmergencePicker _emergencePicker;
        private readonly ExplosionService _explosionService;
        private readonly WyrmNotifications _notifications;
        private readonly WyrmRegistry _wyrmRegistry;

        private readonly HashSet<Vector3Int> _ownTiles = new HashSet<Vector3Int>();

        // Air cells one tile above the den's top layer, one per X/Y
        // column the den occupies. Populated alongside _ownTiles in
        // CacheOwnTiles. Used by the cover-depth calculation and the
        // emergence picker, which both need to reason about "what's
        // covering the den" rather than the den's own cells. For a
        // 1×1×1 den this is a single cell; for a 2×2×2 den it's four
        // cells (the four top columns).
        private readonly List<Vector3Int> _topCells = new List<Vector3Int>();

        // Bottom-layer cells of the den, one per X/Y footprint column,
        // at z = CoordinatesAtBaseZ.z. These are what the surface-
        // moisture probe needs: ISoilMoistureService.SoilIsMoist
        // requires coordinates.z == natural-ground column Ceiling, and
        // for a den placed on the surface that ceiling is exactly the
        // den's base-Z layer. The husk gets away with passing its own
        // single coordinate because Size.z == 1 makes it coincide with
        // the ceiling; for a 2×2×2 den, _topCells sit two voxels above
        // the ceiling and never match, which is why the den would
        // otherwise never wake. Mirrors the husk's probe behaviour,
        // just fanned over the footprint columns.
        private readonly List<Vector3Int> _bottomCells = new List<Vector3Int>();

        // Wyrms attributed to this den: maintained via owner-stamping
        // rather than radius probing. WyrmFactory tags each new wyrm
        // with its owning den, the registry's add/remove events keep
        // the set in sync, and a save's GUIDs round-trip through
        // WyrmComponent.PostLoadEntity. No per-tick reconcile, no
        // radius math, no asymmetry-on-multi-block-footprints concerns.
        private readonly HashSet<WyrmComponent> _ownedWyrms = new HashSet<WyrmComponent>();

        private WyrmDenSpec _spec;
        private BlockObject _blockObject;

        private float _warmupDays;
        private float _spawnCooldownDays;
        private bool _warmedUp;
        private bool _surfaceIsGreen;
        private int _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;

        // Set by OnTilesExplosion when a blast hits us. Actual deletion
        // happens in the next Tick so we don't mutate the EntityService and
        // unsubscribe from the same event we're still being invoked from.
        private bool _pendingDeletionByExplosion;

        public WyrmDen(
            ITerrainService terrainService,
            IBlockService blockService,
            ISoilMoistureService soilMoistureService,
            IDayNightCycle dayNightCycle,
            EntityService entityService,
            WyrmSettings settings,
            WyrmFactory wyrmFactory,
            WyrmEmergencePicker emergencePicker,
            ExplosionService explosionService,
            WyrmNotifications notifications,
            WyrmRegistry wyrmRegistry)
        {
            _terrainService = terrainService;
            _blockService = blockService;
            _soilMoistureService = soilMoistureService;
            _dayNightCycle = dayNightCycle;
            _entityService = entityService;
            _settings = settings;
            _wyrmFactory = wyrmFactory;
            _emergencePicker = emergencePicker;
            _explosionService = explosionService;
            _notifications = notifications;
            _wyrmRegistry = wyrmRegistry;
        }

        public WyrmDenSpec Spec => _spec;
        public bool WarmedUp => _warmedUp;
        public float WarmupDays => _warmupDays;
        public float WarmupTargetDays => ComputeWarmupTargetDays();
        public float WarmupFraction =>
            WarmupTargetDays > 0f ? Mathf.Clamp01(_warmupDays / WarmupTargetDays) : 0f;
        public float SpawnCooldownDays => _spawnCooldownDays;
        public int LiveSpawnCount => _ownedWyrms.Count;
        public bool SurfaceIsGreen => _surfaceIsGreen;
        public int CoverDepth => ComputeCoverDepth();

        // For an N×M×K den, the picker probe origins are the topmost
        // own-cell of each footprint column — that's where the picker
        // starts walking upward. Built lazily from _topCells (which
        // stores the air cell *above* each column's top-own cell).
        // 1×1 legacy dens collapse to one entry, so callers that
        // iterate (the visual + the spawner) work for both.
        private Vector3Int[] _probeCellsCache;
        public IReadOnlyList<Vector3Int> EmergenceProbeCells
        {
            get
            {
                if (_blockObject == null)
                {
                    return System.Array.Empty<Vector3Int>();
                }
                if (_probeCellsCache == null
                    || _probeCellsCache.Length != Mathf.Max(1, _topCells.Count))
                {
                    if (_topCells.Count == 0)
                    {
                        _probeCellsCache = new[] { _blockObject.Coordinates };
                    }
                    else
                    {
                        _probeCellsCache = new Vector3Int[_topCells.Count];
                        for (int i = 0; i < _topCells.Count; i++)
                        {
                            var top = _topCells[i];
                            _probeCellsCache[i] =
                                new Vector3Int(top.x, top.y, top.z - 1);
                        }
                    }
                }
                return _probeCellsCache;
            }
        }

        public void InitializeEntity()
        {
            _spec = GetComponent<WyrmDenSpec>();
            _blockObject = GetComponent<BlockObject>();
            CacheOwnTiles();
            _explosionService.TilesExplosion += OnTilesExplosion;
            _wyrmRegistry.WyrmRegistered += OnWyrmRegistered;
            _wyrmRegistry.WyrmUnregistered += OnWyrmUnregistered;
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
        }

        public void PostInitializeEntity()
        {
            // Re-attribute already-loaded wyrms whose saved owner-id
            // matches our EntityId. Entity load order is Load(all) →
            // PreInitialize(all) → Initialize(all) → PostInitialize(all)
            // → PostLoad(all). Doing this in InitializeEntity would race
            // the wyrms' own InitializeEntity that registers them in
            // WyrmRegistry, so we wait for PostInitialize: by then every
            // loaded wyrm has registered itself, and HasOwningDenId
            // sidesteps the still-null _owningDen reference.
            foreach (var wyrm in _wyrmRegistry.LiveWyrms)
            {
                if (wyrm.HasOwningDenId(this))
                {
                    _ownedWyrms.Add(wyrm);
                }
            }
        }

        public void DeleteEntity()
        {
            _explosionService.TilesExplosion -= OnTilesExplosion;
            _wyrmRegistry.WyrmRegistered -= OnWyrmRegistered;
            _wyrmRegistry.WyrmUnregistered -= OnWyrmUnregistered;
            _ownedWyrms.Clear();
        }

        public override void Tick()
        {
            if (!_settings.ModEnabled || _spec == null || _blockObject == null) return;

            // Apply deferred kill-by-explosion before anything else this tick.
            if (_pendingDeletionByExplosion)
            {
                _pendingDeletionByExplosion = false;
                _notifications.Post(WyrmNotifications.DenDestroyedKey, this);
                Debug.Log(
                    $"[WhereWyrmsWait] Wyrm Den at {_blockObject.Coordinates} " +
                    "destroyed by explosion (deferred from previous tick).");
                _entityService.Delete(this);
                return;
            }

            RecomputeSurfaceIfDue();

            if (!_surfaceIsGreen)
            {
                if (!_warmedUp && _warmupDays > 0f)
                {
                    _warmupDays = 0f;
                }
                return;
            }

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;

            if (!_warmedUp)
            {
                _warmupDays += deltaDays * _settings.WakeSpeedMultiplier;
                if (_warmupDays >= ComputeWarmupTargetDays())
                {
                    _warmedUp = true;
                    SpawnIfPossible();
                    _spawnCooldownDays = _spec.DaysBetweenSpawns;
                }
                return;
            }

            // Already warmed up — count down the cooldown and spawn again.
            _spawnCooldownDays -= deltaDays * _settings.WakeSpeedMultiplier;
            if (_spawnCooldownDays <= 0f)
            {
                SpawnIfPossible();
                _spawnCooldownDays = _spec.DaysBetweenSpawns;
            }
        }

        public void Save(IEntitySaver entitySaver)
        {
            var saver = entitySaver.GetComponent(SaveKey);
            saver.Set(WarmupDaysKey, _warmupDays);
            saver.Set(SpawnCooldownDaysKey, _spawnCooldownDays);
            saver.Set(WarmedUpKey, _warmedUp);
            // Persist the deferred-kill flag too. If a player saves between
            // the explosion-handler firing and the next tick, we want the
            // den to still be killed by that blast on reload rather than
            // surviving by accident.
            saver.Set(PendingDeletionKey, _pendingDeletionByExplosion);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (!entityLoader.TryGetComponent(SaveKey, out var loader)) return;
            if (loader.Has(WarmupDaysKey)) _warmupDays = loader.Get(WarmupDaysKey);
            if (loader.Has(SpawnCooldownDaysKey))
                _spawnCooldownDays = loader.Get(SpawnCooldownDaysKey);
            if (loader.Has(WarmedUpKey)) _warmedUp = loader.Get(WarmedUpKey);
            if (loader.Has(PendingDeletionKey))
                _pendingDeletionByExplosion = loader.Get(PendingDeletionKey);
        }

        private void OnTilesExplosion(
            object sender,
            ReadOnlyHashSet<Vector3Int> tiles)
        {
            // If any of the den's own block coordinates are in the
            // explosion's affected set, mark the den for deletion and let
            // the next Tick handle it. We deliberately defer the
            // EntityService.Delete call out of this handler to avoid
            // mutating the entity registry mid-tick — the explosion event
            // can fire during ExplosionService.Tick(), which iterates the
            // affected tiles, and several other listeners (e.g. vanilla
            // UnstableCore.Activate) may still need to react. Same defer
            // pattern Mortal.DiePubliclyAsSoonAsPossible uses internally.
            if (_pendingDeletionByExplosion) return;
            foreach (var coord in _ownTiles)
            {
                if (tiles.Contains(coord))
                {
                    Debug.Log(
                        $"[WhereWyrmsWait] Wyrm Den at {coord} flagged for " +
                        "deletion by explosion; will be removed next tick.");
                    _pendingDeletionByExplosion = true;
                    return;
                }
            }
        }

        private void CacheOwnTiles()
        {
            _ownTiles.Clear();
            _topCells.Clear();
            _bottomCells.Clear();
            // Probe-cells cache is rebuilt lazily off _topCells; reset it
            // here so the next read picks up the fresh layout.
            _probeCellsCache = null;
            if (_blockObject == null) return;
            foreach (var coord in _blockObject.PositionedBlocks.GetAllCoordinates())
            {
                _ownTiles.Add(coord);
            }
            // Resolve the den's top-layer cells: the (X, Y) columns the
            // den covers, each at the topmost own-Z + 1. These are the
            // "above-the-den" air/cover cells we use for cover-depth
            // measurements and emergence picks. For a 1×1×1 den this
            // collapses to exactly one cell directly above; for the
            // shipped 2×2×2 den it produces 2×2 = 4 cells. The picker /
            // probes then iterate them.
            //
            // _bottomCells covers the same (X, Y) columns at the den's
            // base-Z. SoilIsMoist's ceiling check matches against the
            // natural-ground ceiling, which for a den placed on the
            // surface is exactly the den's base-Z layer.
            if (_ownTiles.Count == 0) return;
            int maxOwnZ = int.MinValue;
            int minOwnZ = int.MaxValue;
            foreach (var coord in _ownTiles)
            {
                if (coord.z > maxOwnZ) maxOwnZ = coord.z;
                if (coord.z < minOwnZ) minOwnZ = coord.z;
            }
            var columns = new HashSet<Vector2Int>();
            foreach (var coord in _ownTiles)
            {
                columns.Add(new Vector2Int(coord.x, coord.y));
            }
            foreach (var col in columns)
            {
                _topCells.Add(new Vector3Int(col.x, col.y, maxOwnZ + 1));
                _bottomCells.Add(new Vector3Int(col.x, col.y, minOwnZ));
            }
        }

        private void RecomputeSurfaceIfDue()
        {
            if (_ticksSinceSurfaceRecompute++ < SurfaceRecomputeEveryTicks) return;
            _ticksSinceSurfaceRecompute = 0;
            _surfaceIsGreen = ProbeSurfaceMoisture();
        }

        private bool ProbeSurfaceMoisture()
        {
            if (_blockObject == null) return false;
            try
            {
                // Multi-block den: any of the four (or one, for a 1×1
                // legacy den) footprint columns going green wakes the
                // den. SoilMoistureService.SoilIsMoist requires the
                // probe coordinate's Z to equal the natural-ground
                // column ceiling — for a den placed on the surface
                // that ceiling is the den's base-Z layer, so we probe
                // _bottomCells, not _topCells. (Probing _topCells is
                // the bug the husk got away with by being 1 voxel
                // tall: there, "above the husk" and "ground ceiling"
                // coincide. For a 2×2×2 den they don't.)
                if (_bottomCells.Count == 0)
                {
                    return _soilMoistureService.SoilIsMoist(
                        _blockObject.CoordinatesAtBaseZ);
                }
                foreach (var cell in _bottomCells)
                {
                    if (_soilMoistureService.SoilIsMoist(cell))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Den moisture probe failed at " +
                    $"{_blockObject.Coordinates}: {ex.Message}");
                return false;
            }
        }

        private int ComputeCoverDepth()
        {
            if (_blockObject == null)
            {
                return 0;
            }
            try
            {
                // For a multi-block den, the cover that gates the wake is
                // the *thinnest* spot — the player only needs to keep one
                // column irrigated long enough for the den to wake. Take
                // the minimum cover depth across all top-cell columns.
                // For a 1×1 legacy den this collapses to the single column.
                int aboveAll = _terrainService.MaxTerrainHeight + 1;

                if (_topCells.Count == 0)
                {
                    var coords = _blockObject.Coordinates;
                    return ComputeColumnCoverDepth(coords, aboveAll);
                }

                int minDepth = int.MaxValue;
                foreach (var top in _topCells)
                {
                    // Each top-cell sits one tile above its column's
                    // topmost own-Z; ComputeColumnCoverDepth treats the
                    // cell's Z minus 1 as the den's local top, so we
                    // pass (X, Y, topZ - 1).
                    var below = new Vector3Int(top.x, top.y, top.z - 1);
                    int d = ComputeColumnCoverDepth(below, aboveAll);
                    if (d < minDepth) minDepth = d;
                }
                return minDepth == int.MaxValue ? 0 : minDepth;
            }
            catch
            {
                return 0;
            }
        }

        private int ComputeColumnCoverDepth(Vector3Int denTopOfColumn, int aboveAll)
        {
            // Same logic the husk uses, parameterised on which (X, Y, denZ)
            // we measure from. denTopOfColumn is the topmost den-owned cell
            // in that column.
            int surfaceZ = _terrainService.GetTerrainHeight(
                new Vector3Int(denTopOfColumn.x, denTopOfColumn.y, aboveAll));
            int naturalCover = Mathf.Max(0, surfaceZ - 1 - denTopOfColumn.z);

            int blockObjectCover = 0;
            int z = Mathf.Max(denTopOfColumn.z + 1, surfaceZ);
            int ceiling = z + 64;
            while (z < ceiling)
            {
                var probe = new Vector3Int(denTopOfColumn.x, denTopOfColumn.y, z);
                if (_blockService.GetObjectsAt(probe).Count == 0) break;
                blockObjectCover++;
                z++;
            }
            return naturalCover + blockObjectCover;
        }

        private float ComputeWarmupTargetDays()
        {
            if (_spec == null) return float.PositiveInfinity;
            return _spec.BaseWarmupDays + _spec.DaysPerCoverBlock * CoverDepth;
        }

        private void OnWyrmRegistered(object sender, WyrmComponent wyrm)
        {
            // Owner-stamping: a new wyrm only counts if it belongs to us.
            // The factory stamps owner at spawn time, before the registry
            // event fires, so this works for in-game new spawns. For
            // post-load reattribution see InitializeEntity, which sweeps
            // already-loaded wyrms for OwningDen == this.
            if (wyrm != null && wyrm.OwningDen == this)
            {
                _ownedWyrms.Add(wyrm);
            }
        }

        private void OnWyrmUnregistered(object sender, WyrmComponent wyrm)
        {
            // Always remove on unregister — a dead wyrm doesn't count.
            _ownedWyrms.Remove(wyrm);
        }

        private void SpawnIfPossible()
        {
            if (_ownedWyrms.Count >= _spec.MaxLiveWyrms) return;

            // Multi-block dens: try the picker against each top column
            // (exposed via IWyrmHazard.EmergenceProbeCells) and use the
            // first one that finds an open emergence cell. Walking the
            // columns in their cached order is deterministic (matches
            // replay/save order); on a 1×1 legacy den the list collapses
            // to a single entry.
            int radius = _spec.EmergenceShiftRadius;
            Vector3Int chosenCell = default;
            bool found = false;
            foreach (var probeOrigin in EmergenceProbeCells)
            {
                if (_emergencePicker.TryPick(probeOrigin, radius, out var cell))
                {
                    chosenCell = cell;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Den at {_blockObject.Coordinates} could not " +
                    "find an open emergence tile in any of its top columns; " +
                    "skipping this spawn cycle.");
                return;
            }

            var worldPos = new Vector3(
                chosenCell.x + 0.5f, chosenCell.z, chosenCell.y + 0.5f);
            // Stamp ownership so this wyrm counts toward our cap and
            // re-attributes correctly on save/load. WyrmFactory routes
            // the owner reference into WyrmComponent.SetOwningDen.
            var wyrm = _wyrmFactory.Spawn(worldPos, Quaternion.identity, this);
            if (wyrm != null)
            {
                _notifications.Post(WyrmNotifications.WyrmEmergedKey, wyrm);
                Debug.Log(
                    $"[WhereWyrmsWait] Den at {_blockObject.Coordinates} spawned a " +
                    $"wyrm at {chosenCell} (now {_ownedWyrms.Count}/{_spec.MaxLiveWyrms} live).");
            }
        }
    }
}
