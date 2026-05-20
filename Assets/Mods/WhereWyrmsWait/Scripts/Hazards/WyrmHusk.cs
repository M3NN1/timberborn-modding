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
    /// Per-husk dormancy ticker. Sits on a placed-by-map-author block and
    /// drives the Dormant → Warming → Emerging state machine described in
    /// DESIGN.md. On wake, asks <see cref="WyrmFactory"/> to spawn a Wyrm
    /// at the emergence tile and self-deletes.
    /// <para>
    /// Wake rule: the topmost natural-ground tile of the husk's column has
    /// positive soil moisture (vanilla green/grass signal). Cover depth is
    /// derived from <see cref="ITerrainService.GetTerrainHeight"/>, which
    /// ignores buildings and returns the tile index of the topmost natural
    /// terrain. Warmup days = <c>BaseWarmupDays + DaysPerCoverBlock × depth</c>,
    /// scaled by the global wake-speed multiplier from
    /// <see cref="WyrmSettings"/>. No hard cap — map authors choose the
    /// depth, the global multiplier handles difficulty scaling.
    /// </para>
    /// <para>
    /// If the surface goes dry mid-warmup, the timer resets to zero (per
    /// the design: "same depth, same timer"). If a drought hits and the
    /// surface goes brown for any reason, same effect.
    /// </para>
    /// <para>
    /// Lifecycle ordering note: the engine's per-entity flow is
    /// <c>Awake → IPersistentEntity.Load → IInitializableEntity.InitializeEntity
    /// → IPostInitializableEntity.PostInitializeEntity → IPostLoadableEntity.PostLoadEntity
    /// → IStartableComponent.Start (calls TickableComponent.StartTickable)</c>.
    /// We resolve <c>_spec</c> and <c>_blockObject</c> in
    /// <see cref="InitializeEntity"/> so the entity-panel fragment can read
    /// <see cref="WarmupFraction"/> right after load — earlier is fine because
    /// <see cref="Load"/> only writes a primitive field. <see cref="StartTickable"/>
    /// is kept as a defensive fallback in case the engine's lifecycle skips
    /// the initialization phase for an unparented prefab.
    /// </para>
    /// <para>
    /// Recompute cadence: terrain and moisture change at human pace. We
    /// resample once every <see cref="SurfaceRecomputeEveryTicks"/> ticks
    /// rather than every tick — same trick DDD's <c>DamDeterioration</c>
    /// uses for water depth. Warmup itself accumulates every tick using the
    /// engine's per-tick delta.
    /// </para>
    /// </summary>
    public class WyrmHusk : TickableComponent,
        IInitializableEntity, IPersistentEntity, IDeletableEntity, IWyrmHazard
    {
        // Save keys.
        private static readonly ComponentKey SaveKey = new ComponentKey("WyrmHusk");
        private static readonly PropertyKey<float> WarmupDaysKey =
            new PropertyKey<float>("WarmupDays");

        // Recompute "is the surface above me green?" every N ticks. At ~10
        // ticks/sec and human-pace irrigation, 16 is far more than enough.
        private const int SurfaceRecomputeEveryTicks = 16;

        // Injected.
        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;
        private readonly ISoilMoistureService _soilMoistureService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly WyrmSettings _settings;
        private readonly WyrmFactory _wyrmFactory;
        private readonly WyrmEmergencePicker _emergencePicker;
        private readonly WyrmNotifications _notifications;

        // Resolved on Awake.
        private WyrmHuskSpec _spec;
        private BlockObject _blockObject;

        // Runtime state.
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

        // Public API used by the entity-panel fragment.
        public float WarmupDays => _warmupDays;
        public float WarmupTargetDays => ComputeWarmupTargetDays();
        public float WarmupFraction =>
            WarmupTargetDays > 0f ? Mathf.Clamp01(_warmupDays / WarmupTargetDays) : 0f;
        public bool SurfaceIsGreen => _surfaceIsGreen;
        public int CoverDepth => ComputeCoverDepth();
        public WyrmHuskSpec Spec => _spec;

        // -------- lifecycle --------

        public void InitializeEntity()
        {
            // Resolve spec + block object up front. The engine guarantees
            // Load() runs before InitializeEntity(), so by here the
            // _warmupDays primitive is already populated. Resolving here
            // means the entity panel can read WarmupFraction right after
            // load without dividing by Infinity.
            _spec = GetComponent<WyrmHuskSpec>();
            _blockObject = GetComponent<BlockObject>();
            // Force a fresh surface read on the first tick.
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
        }

        public override void StartTickable()
        {
            // Defensive: an unparented prefab (e.g. test harness) may skip
            // the IInitializableEntity phase. Resolve here too.
            if (_spec == null) _spec = GetComponent<WyrmHuskSpec>();
            if (_blockObject == null) _blockObject = GetComponent<BlockObject>();
            _ticksSinceSurfaceRecompute = SurfaceRecomputeEveryTicks;
        }

        public override void Tick()
        {
            if (!_settings.ModEnabled || _spec == null || _blockObject == null)
            {
                return;
            }

            RecomputeSurfaceIfDue();

            if (!_surfaceIsGreen)
            {
                // Brown surface = reset warmup. Same depth, same timer
                // next time, per design.
                if (_warmupDays > 0f)
                {
                    _warmupDays = 0f;
                }
                return;
            }

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;
            _warmupDays += deltaDays * _settings.WakeSpeedMultiplier;

            float target = ComputeWarmupTargetDays();
            if (_warmupDays >= target)
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
            // The husk is not registered anywhere, so there's nothing to
            // unregister. Method exists only to satisfy IDeletableEntity.
        }

        // -------- internals --------

        private void RecomputeSurfaceIfDue()
        {
            if (_ticksSinceSurfaceRecompute++ < SurfaceRecomputeEveryTicks)
            {
                return;
            }
            _ticksSinceSurfaceRecompute = 0;
            _surfaceIsGreen = ProbeSurfaceMoisture();
        }

        private bool ProbeSurfaceMoisture()
        {
            if (_blockObject == null)
            {
                return false;
            }
            try
            {
                // SoilMoistureService.SoilIsMoist snaps to the column ceiling
                // tile internally (see TryGetIndexAtCeiling), so we can pass
                // any coordinate inside the husk's column and it resolves to
                // the topmost natural-ground tile. Same pattern vanilla
                // DryObject uses with CoordinatesAtBaseZ.
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
            if (_blockObject == null)
            {
                return 0;
            }
            try
            {
                // Find the column's actual surface by querying the
                // terrain service from a cell above the entire terrain
                // grid. Querying from the husk's own cell (or any cell
                // inside the husk's column that isn't a terrain voxel)
                // makes GetTerrainHeight walk *downward* to find the
                // nearest terrain, which yields the wrong answer for our
                // case — the husk lives in a non-terrain cell sandwiched
                // between soil below and soil above.
                var coords = _blockObject.Coordinates;
                int aboveAll = _terrainService.MaxTerrainHeight + 1;
                int surfaceZ = _terrainService.GetTerrainHeight(
                    new Vector3Int(coords.x, coords.y, aboveAll));
                // surfaceZ is the empty cell above the topmost terrain
                // voxel in this column. Cover blocks above the husk:
                // surfaceZ - 1 - huskZ (terrain voxels at huskZ+1..surfaceZ-1).
                int naturalCover = Mathf.Max(0, surfaceZ - 1 - coords.z);

                // Add any block-objects (placed levees, soil cubes, etc.)
                // sitting in cells above the husk that aren't natural
                // terrain. Walk up from huskZ+1 and count consecutive
                // filled cells; stop at the first fully-empty cell.
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
            if (_spec == null)
            {
                return float.PositiveInfinity;
            }
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

            // Convert grid coordinate to world position. Timberborn uses
            // X/Y as horizontal, Z as height; Unity uses X/Z horizontal,
            // Y up. We add +0.5 horizontally so the wyrm sits in the cell
            // centre rather than at the corner.
            var worldPos = new Vector3(
                emergenceCell.x + 0.5f,
                emergenceCell.z,
                emergenceCell.y + 0.5f);

            var wyrm = _wyrmFactory.Spawn(worldPos);
            if (wyrm == null)
            {
                // Don't delete the husk if no wyrm could be spawned (template
                // missing, spawning blocked, etc.). Reset the warmup so the
                // husk re-tries on the next cycle. Otherwise a config issue
                // would silently consume every husk on the map without ever
                // producing a wyrm.
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

            // Husk is consumed only after a successful spawn — design says
            // one wyrm per husk, gone after waking.
            _entityService.Delete(this);
        }
    }
}
