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
    /// When a hungry wyrm can't reach its target because a player-built
    /// block is in the way, this component chews through it. After
    /// <c>BlockChewDays</c> in-game days of contact, the offending
    /// <see cref="BlockObject"/> is deleted and the wyrm replans.
    /// <para>
    /// Vanilla Timberborn doesn't have a damage model on most building
    /// blocks — they exist or they're <c>EntityService.Delete</c>d. So
    /// "chew" is just a per-block timer; once it hits the threshold,
    /// the whole block disappears at once. Visually that reads as the
    /// wyrm punching a hole through a wall, which is what we want.
    /// </para>
    /// <para>
    /// Sated wyrms (Soothesop in range of a Lure Stake) skip the chew
    /// loop entirely — that's the whole point of the bait economy.
    /// Hungry-but-pathing wyrms also skip: chewing only happens when
    /// the path planner failed AND there's a chewable adjacent block.
    /// </para>
    /// <para>
    /// Phase 3 stub: focuses on vanilla blocks. <c>WyrmHusk</c> /
    /// future <c>WyrmDen</c> blocks are explicitly skipped so wyrms
    /// don't accidentally cannibalize their own siblings.
    /// </para>
    /// </summary>
    public class WyrmWallEater : TickableComponent, IAwakableComponent
    {
        private const float BlockChewDays = 1.5f;

        // We consider the wyrm "stuck" if it hasn't made meaningful
        // forward progress over this many ticks. This catches the case
        // where the planner returned a long detour the wyrm can't reach
        // in finite time, and where the planner returned no path at all.
        private const int ProgressWindowTicks = 12;
        // Required movement during the progress window to count as
        // "still making progress." A few cm covers normal slow walking.
        private const float ProgressDistanceThreshold = 0.25f;

        // 4-neighbour offsets in grid space (X, Y, Z=height). We only
        // chew laterally — wyrms aren't supposed to dig down through
        // floors or up through ceilings; that's the dynamite-only path.
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

        private BlockObject _currentChewTarget;
        private float _chewProgressDays;

        // Progress tracking: we record the wyrm's position once per tick
        // and check whether it's actually moving. If movement stalls for
        // ProgressWindowTicks ticks while we still have a target, we're
        // stuck against something — at which point we look for adjacent
        // chewable blocks.
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
        public float ChewFraction => _chewProgressDays / BlockChewDays;

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _movement = GetComponent<WyrmMovement>();
            _lastProgressPosition = Transform.position;
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

            // Sated, sandbox, no-target → don't chew.
            if (_wyrm.IsSated || _settings.SandboxMode || !_movement.HasTarget)
            {
                ClearChew();
                return;
            }

            // Below the hunting threshold the wyrm is digesting — no
            // wall damage, even if it happens to be next to a levee.
            if (!_wyrm.IsHunting)
            {
                ClearChew();
                return;
            }

            // Only chew while the wyrm is "stuck" — i.e. has a target but
            // no current path to it. WyrmMovement clears its corner list
            // on a planning failure, so we can sniff that as the cue.
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
                if (_currentChewTarget == null)
                {
                    return;
                }
                // Log only when the chew target actually changed (not on
                // every tick of an existing chew). With multi-cell
                // adjacency now correct, "switched targets" is the
                // genuinely interesting signal: it tells you the wyrm
                // walked past one wall to start on a different one, or
                // that progress is being reset on something.
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
            if (_chewProgressDays >= BlockChewDays)
            {
                Chew(_currentChewTarget);
            }
        }

        private bool IsMovementStuck()
        {
            // We're "done", not "stuck", when the wyrm is right next to
            // the live target — that's a successful catch in progress.
            // Use world-space distance so we don't depend on path-planner
            // bookkeeping (ReachedTarget would lie if the planner failed
            // and the corner list is empty).
            float toTarget =
                (_movement.TargetPosition - Transform.position).magnitude;
            if (toTarget < ContactDistance)
            {
                _ticksWithoutProgress = 0;
                _lastProgressPosition = Transform.position;
                return false;
            }

            // Sample movement progress this tick. If we moved more than
            // ProgressDistanceThreshold since the last sample, reset the
            // counter — we're walking fine.
            float moved =
                (Transform.position - _lastProgressPosition).magnitude;
            if (moved >= ProgressDistanceThreshold)
            {
                _ticksWithoutProgress = 0;
                _lastProgressPosition = Transform.position;
                // Don't drop existing chew progress just because we
                // shuffled a bit — only if we *also* have no current
                // chew target. The chew flow itself decides when to
                // clear progress.
                return false;
            }

            _ticksWithoutProgress++;
            _lastProgressPosition = Transform.position;
            return _ticksWithoutProgress >= ProgressWindowTicks;
        }

        // The contact distance below which the wyrm is "on top of" its
        // target and isn't stuck — copied conservatively to match the
        // hunter's eat-on-contact threshold.
        private const float ContactDistance = 0.6f;

        private BlockObject FindAdjacentChewable()
        {
            var origin = WorldToGridInt(Transform.position);
            foreach (var offset in NeighbourOffsets)
            {
                var probe = origin + offset;
                // Use GetObjectsAt rather than GetBottomObjectAt so we
                // notice levees stacked on terrain — the "bottom" object
                // at a given 3D cell isn't always the player-built block.
                foreach (var bo in _blockService.GetObjectsAt(probe))
                {
                    if (IsChewable(bo))
                    {
                        return bo;
                    }
                }
            }
            return null;
        }

        private bool IsAdjacent(BlockObject bo)
        {
            if (bo == null || bo.GameObject == null) return false;
            // Multi-cell buildings (stairs, lodges, dams) have several
            // PositionedBlocks. Checking only `bo.Coordinates` (the
            // origin tile) misses the case where the wyrm is next to a
            // non-origin cell of the same building, which made the
            // chew loop think the target had moved every tick. Probe
            // each of the wyrm's lateral neighbours and ask the block
            // object whether *any* of its positioned blocks live there.
            var origin = WorldToGridInt(Transform.position);
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
            // Only chew *player-built* things. BuildingSpec is the
            // marker shared by every constructible blueprint (levee,
            // dam, wall, lodge…). Natural terrain, water sources,
            // creatures, and our own husk/den blocks all lack BuildingSpec
            // and are therefore safe from chewing — which avoids
            // EntityService.Delete on entities that don't expect to be
            // deleted that way.
            if (!bo.HasComponent<BuildingSpec>()) return false;
            // Don't chew our own family. Wyrm's own GameObject doesn't
            // have BlockObject so it's automatically excluded; husks and
            // dens both have BlockObject + their own spec, so check both.
            if (bo.HasComponent<WyrmHuskSpec>()) return false;
            if (bo.HasComponent<WyrmDenSpec>()) return false;
            // Don't chew blocks that aren't finished yet — construction
            // sites are already going away on their own.
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

        private static Vector3Int WorldToGridInt(Vector3 world)
        {
            // Route through the engine's coordinate helper so the wyrm's
            // grid position stays consistent with how vanilla code reads
            // creature positions.
            return CoordinateSystem.WorldToGridInt(world);
        }
    }
}
