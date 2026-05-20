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
        IInitializableEntity, IPersistentEntity, IDeletableEntity, IWyrmHazard
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

        // Wyrms attributed to this den. Maintained incrementally via
        // WyrmRegistry's WyrmRegistered/WyrmUnregistered events plus per-tick
        // boundary checks for wanderers crossing in/out of the tracking
        // radius — far cheaper than re-scanning the global registry every
        // tick at scale.
        private readonly HashSet<WyrmComponent> _trackedWyrms = new HashSet<WyrmComponent>();

        // Cooldown for the boundary recheck. Wyrms move slowly; checking
        // every few ticks is plenty.
        private const int RadiusRecheckEveryTicks = 32;
        private int _ticksSinceRadiusRecheck = RadiusRecheckEveryTicks;

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
        public int LiveSpawnCount => _trackedWyrms.Count;
        public bool SurfaceIsGreen => _surfaceIsGreen;
        public int CoverDepth => ComputeCoverDepth();

        public void InitializeEntity()
        {
            _spec = GetComponent<WyrmDenSpec>();
            _blockObject = GetComponent<BlockObject>();
            CacheOwnTiles();
            _explosionService.TilesExplosion += OnTilesExplosion;
            _wyrmRegistry.WyrmRegistered += OnWyrmRegistered;
            _wyrmRegistry.WyrmUnregistered += OnWyrmUnregistered;
            // Seed the tracked set from any wyrms already alive at load.
            foreach (var wyrm in _wyrmRegistry.LiveWyrms)
            {
                if (IsWithinTrackingRadius(wyrm))
                {
                    _trackedWyrms.Add(wyrm);
                }
            }
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
            _ticksSinceRadiusRecheck = RadiusRecheckEveryTicks;
        }

        public void DeleteEntity()
        {
            _explosionService.TilesExplosion -= OnTilesExplosion;
            _wyrmRegistry.WyrmRegistered -= OnWyrmRegistered;
            _wyrmRegistry.WyrmUnregistered -= OnWyrmUnregistered;
            _trackedWyrms.Clear();
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

            ReconcileTrackedWyrmsIfDue();

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
            if (_blockObject == null) return;
            foreach (var coord in _blockObject.PositionedBlocks.GetAllCoordinates())
            {
                _ownTiles.Add(coord);
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
            try
            {
                // Same column-snap trick as WyrmHusk: SoilMoistureService
                // resolves to the column's ceiling tile, so any coordinate
                // inside the column works.
                return _soilMoistureService.SoilIsMoist(
                    _blockObject.CoordinatesAtBaseZ);
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
                // Same logic as WyrmHusk: query terrain height from above
                // all terrain so we get the column's actual surface,
                // then count any block-objects on top.
                var coords = _blockObject.Coordinates;
                int aboveAll = _terrainService.MaxTerrainHeight + 1;
                int surfaceZ = _terrainService.GetTerrainHeight(
                    new Vector3Int(coords.x, coords.y, aboveAll));
                int naturalCover = Mathf.Max(0, surfaceZ - 1 - coords.z);

                int blockObjectCover = 0;
                int z = Mathf.Max(coords.z + 1, surfaceZ);
                int ceiling = z + 64;
                while (z < ceiling)
                {
                    var probe = new Vector3Int(coords.x, coords.y, z);
                    if (_blockService.GetObjectsAt(probe).Count == 0) break;
                    blockObjectCover++;
                    z++;
                }
                return naturalCover + blockObjectCover;
            }
            catch
            {
                return 0;
            }
        }

        private float ComputeWarmupTargetDays()
        {
            if (_spec == null) return float.PositiveInfinity;
            return _spec.BaseWarmupDays + _spec.DaysPerCoverBlock * CoverDepth;
        }

        private int CountLiveSpawns()
        {
            // Convenience accessor maintained by WyrmRegistry events plus
            // a periodic radius recheck (see ReconcileTrackedWyrmsIfDue).
            return _trackedWyrms.Count;
        }

        private bool IsWithinTrackingRadius(WyrmComponent wyrm)
        {
            if (wyrm == null || wyrm.GameObject == null || _blockObject == null)
            {
                return false;
            }
            float radius = _spec?.SpawnTrackingRadius ?? 24f;
            float radiusSqr = radius * radius;
            Vector3 here = new Vector3(
                _blockObject.Coordinates.x + 0.5f,
                _blockObject.Coordinates.z,
                _blockObject.Coordinates.y + 0.5f);
            return (wyrm.Transform.position - here).sqrMagnitude <= radiusSqr;
        }

        private void OnWyrmRegistered(object sender, WyrmComponent wyrm)
        {
            // A new wyrm is born close enough to count toward this den's
            // cap. The most common case: this den just spawned it.
            if (IsWithinTrackingRadius(wyrm))
            {
                _trackedWyrms.Add(wyrm);
            }
        }

        private void OnWyrmUnregistered(object sender, WyrmComponent wyrm)
        {
            // Always remove on unregister, no boundary check needed —
            // a dead wyrm doesn't count even if it died inside the radius.
            _trackedWyrms.Remove(wyrm);
        }

        private void ReconcileTrackedWyrmsIfDue()
        {
            // Wyrms walk; one that wandered out of range stops counting,
            // and one that wandered in starts counting. Cheap re-walk of
            // the (small) set every few ticks instead of per-tick.
            if (_ticksSinceRadiusRecheck++ < RadiusRecheckEveryTicks) return;
            _ticksSinceRadiusRecheck = 0;

            // Drop any tracked wyrms that have died, been deleted, or
            // wandered out of range.
            _trackedWyrms.RemoveWhere(w => w == null || w.GameObject == null
                || !IsWithinTrackingRadius(w));

            // Add any registry-known wyrms that have wandered into range
            // but weren't spawned by us (e.g. another den's wyrm crossing
            // our radius). Two dens within 2× tracking radius will
            // therefore share their wyrms in their per-den caps —
            // documented design behaviour.
            foreach (var wyrm in _wyrmRegistry.LiveWyrms)
            {
                if (!_trackedWyrms.Contains(wyrm) && IsWithinTrackingRadius(wyrm))
                {
                    _trackedWyrms.Add(wyrm);
                }
            }
        }

        private void SpawnIfPossible()
        {
            if (CountLiveSpawns() >= _spec.MaxLiveWyrms) return;

            if (!_emergencePicker.TryPick(
                _blockObject.Coordinates, _spec.EmergenceShiftRadius, out var cell))
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Den at {_blockObject.Coordinates} could not " +
                    "find an open emergence tile; skipping this spawn cycle.");
                return;
            }

            var worldPos = new Vector3(
                cell.x + 0.5f, cell.z, cell.y + 0.5f);
            var wyrm = _wyrmFactory.Spawn(worldPos);
            if (wyrm != null)
            {
                _notifications.Post(WyrmNotifications.WyrmEmergedKey, wyrm);
                Debug.Log(
                    $"[WhereWyrmsWait] Den at {_blockObject.Coordinates} spawned a " +
                    $"wyrm at {cell} (now {CountLiveSpawns()}/{_spec.MaxLiveWyrms} live).");
            }
        }
    }
}
