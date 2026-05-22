using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
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
    /// Per-husk dormancy ticker. Wakes when the topmost natural-ground
    /// tile of the husk's column has positive soil moisture; resets the
    /// warmup timer to zero when the surface goes brown. Warmup target
    /// = <c>BaseWarmupDays + DaysPerCoverBlock × CoverDepth</c>, scaled
    /// by the global wake-speed multiplier. On wake, asks
    /// <see cref="WyrmFactory"/> to spawn a Wyrm and self-deletes.
    /// </summary>
    public class WyrmHusk : TickableComponent,
        IInitializableEntity, IPersistentEntity, IDeletableEntity, IWyrmHazard
    {
        private static readonly ComponentKey SaveKey = new ComponentKey("WyrmHusk");
        private static readonly PropertyKey<float> WarmupDaysKey =
            new PropertyKey<float>("WarmupDays");

        // Surface moisture changes at human pace; resampling every 16
        // ticks (~1.5s game) is plenty.
        private const int SurfaceRecomputeEveryTicks = 16;

        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;
        private readonly ISoilMoistureService _soilMoistureService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly WyrmSettings _settings;
        private readonly WyrmFactory _wyrmFactory;
        private readonly WyrmEmergencePicker _emergencePicker;
        private readonly WyrmNotifications _notifications;

        private WyrmHuskSpec _spec;
        private BlockObject _blockObject;

        private float _warmupDays;
        private bool _surfaceIsGreen;
        private int _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;

        public WyrmHusk(
            ITerrainService terrainService,
            IBlockService blockService,
            ISoilMoistureService soilMoistureService,
            IDayNightCycle dayNightCycle,
            EntityService entityService,
            WyrmSettings settings,
            WyrmFactory wyrmFactory,
            WyrmEmergencePicker emergencePicker,
            WyrmNotifications notifications)
        {
            _terrainService = terrainService;
            _blockService = blockService;
            _soilMoistureService = soilMoistureService;
            _dayNightCycle = dayNightCycle;
            _entityService = entityService;
            _settings = settings;
            _wyrmFactory = wyrmFactory;
            _emergencePicker = emergencePicker;
            _notifications = notifications;
        }

        public float WarmupDays => _warmupDays;
        public float WarmupTargetDays => ComputeWarmupTargetDays();
        public float WarmupFraction =>
            WarmupTargetDays > 0f ? Mathf.Clamp01(_warmupDays / WarmupTargetDays) : 0f;
        public bool SurfaceIsGreen => _surfaceIsGreen;
        public int CoverDepth => ComputeCoverDepth();
        public WyrmHuskSpec Spec => _spec;

        // Single-cell husk: probe origin is the husk's own coordinate.
        // Cached as a 1-entry array so callers iterate without allocs.
        private Vector3Int[] _probeCellsCache;
        public System.Collections.Generic.IReadOnlyList<Vector3Int>
            EmergenceProbeCells
        {
            get
            {
                if (_blockObject == null)
                {
                    return System.Array.Empty<Vector3Int>();
                }
                if (_probeCellsCache == null
                    || _probeCellsCache[0] != _blockObject.Coordinates)
                {
                    _probeCellsCache = new[] { _blockObject.Coordinates };
                }
                return _probeCellsCache;
            }
        }

        public void InitializeEntity()
        {
            // Resolve here so the entity-panel fragment can read
            // WarmupFraction right after load without dividing by ∞.
            // The engine runs Load before InitializeEntity, so by here
            // _warmupDays is already populated from the save.
            _spec = GetComponent<WyrmHuskSpec>();
            _blockObject = GetComponent<BlockObject>();
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
        }

        public override void StartTickable()
        {
            // Defensive: an unparented prefab (test harness) may skip
            // the IInitializableEntity phase.
            if (_spec == null) _spec = GetComponent<WyrmHuskSpec>();
            if (_blockObject == null) _blockObject = GetComponent<BlockObject>();
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
        }

        public override void Tick()
        {
            if (!_settings.ModEnabled || _spec == null || _blockObject == null) return;

            RecomputeSurfaceIfDue();

            if (!_surfaceIsGreen)
            {
                // Brown surface = reset warmup. Same depth means same
                // timer next time, per design.
                if (_warmupDays > 0f)
                {
                    _warmupDays = 0f;
                }
                return;
            }

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;
            _warmupDays += deltaDays * _settings.WakeSpeedMultiplier;

            if (_warmupDays >= ComputeWarmupTargetDays())
            {
                Emerge();
            }
        }

        public void Save(IEntitySaver entitySaver)
        {
            entitySaver.GetComponent(SaveKey).Set(WarmupDaysKey, _warmupDays);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(SaveKey, out var loader)
                && loader.Has(WarmupDaysKey))
            {
                _warmupDays = loader.Get(WarmupDaysKey);
            }
        }

        public void DeleteEntity()
        {
            // Husk doesn't register anywhere — interface satisfaction only.
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
                // SoilIsMoist snaps to the column ceiling internally,
                // so any coordinate in the husk's column resolves to
                // the topmost natural-ground tile.
                return _soilMoistureService.SoilIsMoist(
                    _blockObject.CoordinatesAtBaseZ);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not probe surface moisture at " +
                    $"{_blockObject.Coordinates}: {ex.Message}");
                return false;
            }
        }

        private int ComputeCoverDepth()
        {
            if (_blockObject == null) return 0;
            try
            {
                // Probe from a cell above the entire terrain grid.
                // Probing from inside the husk's column would make
                // GetTerrainHeight walk downward and return the wrong
                // answer for a husk sandwiched between soil layers.
                var coords = _blockObject.Coordinates;
                int aboveAll = _terrainService.MaxTerrainHeight + 1;
                int surfaceZ = _terrainService.GetTerrainHeight(
                    new Vector3Int(coords.x, coords.y, aboveAll));
                // surfaceZ is the empty cell above the topmost terrain
                // voxel; natural cover is the voxels between huskZ+1
                // and surfaceZ-1.
                int naturalCover = Mathf.Max(0, surfaceZ - 1 - coords.z);

                // Block-objects (levees, soil cubes) above the natural
                // surface also count as cover. Walk up from the surface
                // and count consecutive filled cells.
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

        private void Emerge()
        {
            int radius = _spec?.EmergenceShiftRadius ?? 2;
            if (!_emergencePicker.TryPick(_blockObject.Coordinates, radius, out var emergenceCell))
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Husk at {_blockObject.Coordinates} could not " +
                    $"find an open emergence tile within radius {radius}; suppressed " +
                    "for this cycle. Timer reset.");
                _warmupDays = 0f;
                return;
            }

            // Grid (X, Y, Z=height) → Unity (X, Z=Y, Y=Z); +0.5 centres
            // the spawn in the cell.
            var worldPos = new Vector3(
                emergenceCell.x + 0.5f,
                emergenceCell.z,
                emergenceCell.y + 0.5f);

            var wyrm = _wyrmFactory.Spawn(worldPos);
            if (wyrm == null)
            {
                // Preserve the husk and reset for retry — a config issue
                // would otherwise silently burn through every husk on
                // the map without ever spawning a wyrm.
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Husk at {_blockObject.Coordinates} reached " +
                    "warmup but no wyrm could be spawned (template missing or " +
                    "spawn refused). Husk preserved; timer reset for retry.");
                _warmupDays = 0f;
                return;
            }

            Debug.Log(
                $"[WhereWyrmsWait] Husk at {_blockObject.Coordinates} woke after " +
                $"{_warmupDays:F1} days of warmup (cover depth {CoverDepth}). " +
                $"Wyrm spawned at {emergenceCell}.");
            _notifications.Post(WyrmNotifications.WyrmEmergedKey, wyrm);

            // One wyrm per husk; consume after the successful spawn.
            _entityService.Delete(this);
        }
    }
}
