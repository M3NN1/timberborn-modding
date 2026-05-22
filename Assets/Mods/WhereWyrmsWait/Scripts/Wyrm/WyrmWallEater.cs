using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Hazards;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// When a hungry wyrm is stuck against a player-built block, this
    /// component chews through it. After
    /// <see cref="WyrmSpec.BlockChewDays"/> in-game days of contact,
    /// the offending <see cref="BlockObject"/> is deleted whole — most
    /// vanilla blocks have no damage model, so "chew" is just a timer.
    /// Sated, sandbox, digesting, or unstuck wyrms don't chew. Only
    /// lateral neighbours; vertical "chewing" is the dynamite path's
    /// territory.
    /// </summary>
    public class WyrmWallEater : TickableComponent, IAwakableComponent
    {
        // Safety net for the gap between Awake() and the template
        // decorator landing the spec on us. Once _spec is set,
        // ResolveBlockChewDays reads from there.
        private const float DefaultBlockChewDays = 1.5f;

        // "Stuck" = no meaningful forward progress over this window.
        // Catches both planner failure and "long detour we can't reach."
        private const int ProgressWindowTicks = 12;
        private const float ProgressDistanceThreshold = 0.25f;

        // Mirrors WyrmHunter.ContactDistance — close enough that the
        // wyrm is on its target rather than blocked by something else.
        private const float ContactDistance = 0.6f;

        private static readonly Vector3Int[] NeighbourOffsets =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
        };

        private readonly IBlockService _blockService;
        private readonly EntityService _entityService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly WyrmSettings _settings;

        private WyrmComponent _wyrm;
        private WyrmMovement _movement;
        private WyrmSpec _spec;

        private BlockObject _currentChewTarget;
        private float _chewProgressDays;

        private Vector3 _lastProgressPosition;
        private int _ticksWithoutProgress;

        public WyrmWallEater(
            IBlockService blockService,
            EntityService entityService,
            IDayNightCycle dayNightCycle,
            WyrmSettings settings)
        {
            _blockService = blockService;
            _entityService = entityService;
            _dayNightCycle = dayNightCycle;
            _settings = settings;
        }

        public BlockObject CurrentChewTarget => _currentChewTarget;
        public float ChewFraction => _chewProgressDays / ResolveBlockChewDays();

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _movement = GetComponent<WyrmMovement>();
            _spec = GetComponent<WyrmSpec>();
            _lastProgressPosition = Transform.position;
        }

        private float ResolveBlockChewDays()
        {
            float days = _spec?.BlockChewDays ?? DefaultBlockChewDays;
            return days > 0f ? days : DefaultBlockChewDays;
        }

        public override void Tick()
        {
            if (_wyrm == null || _movement == null) return;
            if (_settings == null)
            {
                WyrmDiagnostics.LogOnce(
                    "WyrmWallEater.SettingsNull",
                    "WyrmSettings was null in WyrmWallEater — DI binding issue.");
                return;
            }

            if (_wyrm.IsSated || _settings.SandboxMode || !_movement.HasTarget)
            {
                ClearChew();
                return;
            }

            // Digesting wyrms (below HuntingThreshold) don't chew.
            if (!_wyrm.IsHunting)
            {
                ClearChew();
                return;
            }

            if (!IsMovementStuck())
            {
                ClearChew();
                return;
            }

            if (_currentChewTarget == null
                || _currentChewTarget.GameObject == null
                || !IsAdjacent(_currentChewTarget))
            {
                var previous = _currentChewTarget;
                _currentChewTarget = FindAdjacentChewable();
                _chewProgressDays = 0f;
                if (_currentChewTarget == null) return;

                // Only log on actual target changes, not on every tick
                // of an existing chew.
                if (!ReferenceEquals(previous, _currentChewTarget))
                {
                    Debug.Log(
                        $"[WhereWyrmsWait] Wyrm at " +
                        $"{Transform.position} starts chewing " +
                        $"{_currentChewTarget.GameObject.name} " +
                        $"at {_currentChewTarget.Coordinates}.");
                }
            }

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;
            _chewProgressDays += deltaDays;
            if (_chewProgressDays >= ResolveBlockChewDays())
            {
                Chew(_currentChewTarget);
            }
        }

        private bool IsMovementStuck()
        {
            // "On the target" isn't stuck — that's a successful catch
            // in progress. World-space distance avoids depending on
            // planner bookkeeping (the corner list is empty after a
            // planning failure, so ReachedTarget would lie).
            float toTarget =
                (_movement.TargetPosition - Transform.position).magnitude;
            if (toTarget < ContactDistance)
            {
                _ticksWithoutProgress = 0;
                _lastProgressPosition = Transform.position;
                return false;
            }

            float moved =
                (Transform.position - _lastProgressPosition).magnitude;
            if (moved >= ProgressDistanceThreshold)
            {
                _ticksWithoutProgress = 0;
                _lastProgressPosition = Transform.position;
                return false;
            }

            _ticksWithoutProgress++;
            _lastProgressPosition = Transform.position;
            return _ticksWithoutProgress >= ProgressWindowTicks;
        }

        private BlockObject FindAdjacentChewable()
        {
            var origin = CoordinateSystem.WorldToGridInt(Transform.position);
            foreach (var offset in NeighbourOffsets)
            {
                var probe = origin + offset;
                // GetObjectsAt rather than GetBottomObjectAt so we
                // notice levees stacked on terrain.
                foreach (var bo in _blockService.GetObjectsAt(probe))
                {
                    if (IsChewable(bo)) return bo;
                }
            }
            return null;
        }

        private bool IsAdjacent(BlockObject bo)
        {
            if (bo == null || bo.GameObject == null) return false;
            // Multi-cell buildings (stairs, lodges, dams) span several
            // PositionedBlocks. Checking only bo.Coordinates would miss
            // adjacency to non-origin cells of the same building.
            var origin = CoordinateSystem.WorldToGridInt(Transform.position);
            foreach (var offset in NeighbourOffsets)
            {
                var probe = origin + offset;
                if (bo.PositionedBlocks.TryGetBlock(probe, out _))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsChewable(BlockObject bo)
        {
            if (bo == null || bo.GameObject == null) return false;
            // BuildingSpec is the marker for player-built blueprints.
            // Natural terrain, water sources, creatures, and our husks
            // / dens all lack it and are safe from chewing.
            if (!bo.HasComponent<BuildingSpec>()) return false;
            // Don't cannibalize our own family.
            if (bo.HasComponent<WyrmHuskSpec>()) return false;
            if (bo.HasComponent<WyrmDenSpec>()) return false;
            // Construction sites are already on their way out.
            if (!bo.IsFinished) return false;
            return true;
        }

        private void Chew(BlockObject bo)
        {
            Debug.Log(
                $"[WhereWyrmsWait] Wyrm chewed through " +
                $"{bo.GameObject.name} at {bo.Coordinates}.");
            try
            {
                _entityService.Delete(bo);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not delete chewed block: " +
                    $"{ex.Message}");
            }
            ClearChew();
        }

        private void ClearChew()
        {
            _currentChewTarget = null;
            _chewProgressDays = 0f;
        }
    }
}
