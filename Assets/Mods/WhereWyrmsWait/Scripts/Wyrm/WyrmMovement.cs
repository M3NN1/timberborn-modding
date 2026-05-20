using System.Collections.Generic;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Coordinates;
using Timberborn.Navigation;
using Timberborn.TerrainPhysics;
using Timberborn.TerrainSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Placeholder movement for wyrms. Path planning runs on the tick
    /// scheduler (cheap); the transform is interpolated every frame in
    /// <see cref="Update"/> so wyrms walk smoothly between corners
    /// instead of jumping tile-to-tile at the simulation tick rate.
    /// <para>
    /// Skips the engine's <c>PathFollower</c> + <c>MovementAnimator</c>
    /// stack to avoid pulling in skinned-mesh and animation requirements
    /// before we have a rigged wyrm model. Swap in <c>PathFollower</c>
    /// once a rigged model exists.
    /// </para>
    /// <para>
    /// Movement constraints — the +1 step rule, path blockers, normal
    /// water swimming — fall out automatically because we ask the
    /// vanilla nav-service to plan, and the nav-mesh already encodes
    /// what beavers (and now wyrms) can traverse.
    /// </para>
    /// </summary>
    public class WyrmMovement : TickableComponent,
        IAwakableComponent, IUpdatableComponent
    {
        // Replan no more than once per N ticks to avoid pathfinding spam
        // when the target is stable. Replanning happens automatically when
        // we reach the end of the current path or the target changes.
        private const int MinTicksBetweenPlans = 6;

        // If the live target moves more than this in world-space units,
        // throw away the current path and replan immediately so we don't
        // chase a stale corner sequence.
        private const float TargetMovedThreshold = 1.0f;

        // Free-fall speed (world units / second) when the cell beneath the
        // wyrm has no support — terrain or block-object floor. Slow enough
        // to read as "the wyrm slumps down" rather than teleport.
        private const float FallSpeed = 4f;

        private readonly INavigationService _navigationService;
        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;

        private WyrmComponent _wyrm;
        private WyrmSpec _spec;

        private Vector3 _targetPosition;
        private bool _hasTarget;
        private bool _replanRequested;
        private readonly List<PathCorner> _pathCorners = new List<PathCorner>(64);
        private int _nextCornerIndex;
        private int _ticksSincePlan = MinTicksBetweenPlans;

        public WyrmMovement(
            INavigationService navigationService,
            ITerrainService terrainService,
            IBlockService blockService)
        {
            _navigationService = navigationService;
            _terrainService = terrainService;
            _blockService = blockService;
        }

        public bool HasTarget => _hasTarget;
        public Vector3 TargetPosition => _targetPosition;
        public bool ReachedTarget => _hasTarget && _nextCornerIndex >= _pathCorners.Count;

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _spec = GetComponent<WyrmSpec>();
        }

        public override void StartTickable()
        {
            // Spec resolution moved to Awake so per-frame Update() (which
            // runs even before the first Tick lands) sees a non-null spec
            // and can apply gravity correctly. StartTickable now only
            // exists to satisfy the framework call.
        }

        /// <summary>
        /// Set or update the live target. Tiny target jitter (<0.5 units)
        /// is ignored. Larger jumps invalidate the current path so the
        /// next tick will replan.
        /// </summary>
        public void SetTarget(Vector3 target)
        {
            if (_hasTarget && Vector3.Distance(target, _targetPosition) < 0.05f)
            {
                return;
            }
            bool movedFar = _hasTarget &&
                (target - _targetPosition).sqrMagnitude
                    >= TargetMovedThreshold * TargetMovedThreshold;
            _targetPosition = target;
            _hasTarget = true;
            if (movedFar)
            {
                _replanRequested = true;
            }
        }

        public void ClearTarget()
        {
            _hasTarget = false;
            _replanRequested = false;
            _pathCorners.Clear();
            _nextCornerIndex = 0;
        }

        public override void Tick()
        {
            if (!_hasTarget || _wyrm == null) return;
            EnsurePathPlanned();
        }

        private void EnsurePathPlanned()
        {
            bool needsPlan =
                _replanRequested
                || _pathCorners.Count == 0
                || _nextCornerIndex >= _pathCorners.Count;
            if (!needsPlan)
            {
                _ticksSincePlan++;
                return;
            }

            if (_ticksSincePlan++ < MinTicksBetweenPlans)
            {
                return;
            }
            _ticksSincePlan = 0;
            _replanRequested = false;

            _pathCorners.Clear();
            _nextCornerIndex = 0;
            try
            {
                if (!_navigationService.FindPathUnlimitedRange(
                    Transform.position, _targetPosition, _pathCorners, out _))
                {
                    _pathCorners.Clear();
                    return;
                }
                // The first corner is the start; we walk to subsequent ones.
                _nextCornerIndex = 1;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Wyrm path planning failed: {ex.Message}");
                _pathCorners.Clear();
            }
        }

        /// <summary>
        /// Per-frame transform interpolation. Runs every frame regardless
        /// of game-tick cadence so the wyrm visibly walks between corners
        /// instead of teleporting at the tick rate. Bound by the engine
        /// via <see cref="IUpdatableComponent"/> — Unity itself doesn't
        /// call <c>Update</c> on <c>BaseComponent</c>s; the adapter does.
        /// </summary>
        public void Update()
        {
            if (_spec == null) return;

            // Apply gravity first. If the cell directly beneath the wyrm
            // has neither natural terrain nor a block-object floor, slide
            // downward at FallSpeed. Path corners are invalidated on a
            // significant fall so the planner reroutes from the new tile
            // once we land.
            ApplyGravityIfFloating();

            if (!_hasTarget) return;
            if (_pathCorners.Count == 0 || _nextCornerIndex >= _pathCorners.Count) return;

            // Speed is hunger-scaled: slow while digesting, fast while
            // starving. WyrmComponent.EffectiveWalkSpeed handles the
            // hunger curve and the Soothesop-sated case.
            float speed = _wyrm != null
                ? _wyrm.EffectiveWalkSpeed
                : _spec.WalkSpeed;
            float stepBudget = speed * Time.deltaTime;

            // Walk through as many corners as the per-frame budget covers.
            // Most frames consume a fraction of one corner; on slow-frame
            // spikes we may snap through several at once, which is correct.
            while (stepBudget > 0f && _nextCornerIndex < _pathCorners.Count)
            {
                Vector3 corner = _pathCorners[_nextCornerIndex].Position;
                Vector3 here = Transform.position;
                Vector3 toCorner = corner - here;
                float distance = toCorner.magnitude;
                if (distance < 0.01f)
                {
                    Transform.position = corner;
                    _nextCornerIndex++;
                    continue;
                }
                if (stepBudget >= distance)
                {
                    Transform.position = corner;
                    stepBudget -= distance;
                    _nextCornerIndex++;
                }
                else
                {
                    Vector3 step = toCorner.normalized * stepBudget;
                    Transform.position = here + step;
                    stepBudget = 0f;
                }

                if (toCorner.sqrMagnitude > 0.01f)
                {
                    var lookRot = Quaternion.LookRotation(
                        new Vector3(toCorner.x, 0f, toCorner.z), Vector3.up);
                    Transform.rotation = Quaternion.Slerp(
                        Transform.rotation, lookRot,
                        Mathf.Clamp01(Time.deltaTime * 6f));
                }
            }
        }

        /// <summary>
        /// If the cell beneath the wyrm has no support (no natural
        /// terrain voxel and no block-object), translate the wyrm
        /// downward at <see cref="FallSpeed"/>. Once it lands on the
        /// next supported cell, snap to the cell's top and force a path
        /// replan so the navigation routing reflects the new starting
        /// position.
        /// </summary>
        private void ApplyGravityIfFloating()
        {
            var here = Transform.position;
            var grid = CoordinateSystem.WorldToGridInt(here);
            if (HasSupportBelow(grid))
            {
                return;
            }

            // Walk downward until we find a supported cell or run out of
            // map. The fall step is capped so a tall fall reads as a
            // visible slump instead of a teleport.
            float drop = FallSpeed * Time.deltaTime;
            float newY = here.y - drop;

            // Snap to the top of the supported cell once we cross it.
            // We probe one cell below the wyrm's current grid Z; if that
            // cell is supported, the floor surface is at Y == grid.z.
            int probeZ = grid.z - 1;
            bool landed = false;
            while (probeZ >= 0)
            {
                if (HasSupportAt(new Vector3Int(grid.x, grid.y, probeZ)))
                {
                    float floorY = probeZ + 1f;
                    if (newY <= floorY)
                    {
                        newY = floorY;
                        // Falling through path corners would otherwise
                        // make the wyrm walk forward in mid-air; reset.
                        _pathCorners.Clear();
                        _nextCornerIndex = 0;
                        _replanRequested = true;
                        landed = true;
                    }
                    break;
                }
                probeZ--;
            }
            if (probeZ < 0)
            {
                // No floor anywhere in the column. Stop at z=0 so the
                // wyrm doesn't sink to negative infinity.
                newY = Mathf.Max(0f, newY);
            }

            // Cheap visibility into the gravity pipeline. Logs only when
            // the wyrm transitions from "in air" to "landed" so we don't
            // spam ticks while falling. The presence of a fall-start log
            // without a matching landed log means the wyrm is truly in
            // mid-air with no floor anywhere.
            if (landed)
            {
                Debug.Log(
                    $"[WhereWyrmsWait] Wyrm landed at " +
                    $"{Transform.position} -> y={newY:F2}.");
            }

            Transform.position = new Vector3(here.x, newY, here.z);
        }

        private bool HasSupportBelow(Vector3Int grid)
        {
            return HasSupportAt(new Vector3Int(grid.x, grid.y, grid.z - 1));
        }

        private bool HasSupportAt(Vector3Int cell)
        {
            if (cell.z < 0) return true;
            // Natural terrain counts as floor.
            if (_terrainService.Underground(cell)) return true;
            // Block-objects count as floor too — levees, foundations,
            // lodges, even our own husk and den (so a wyrm placed on a
            // husk in the editor stays put). One exception: piles of
            // recovered goods that the engine spawns when a player
            // deletes a building. Those are tagged INonStackPickable —
            // the same marker vanilla terrain physics uses to ignore
            // them. Without this filter, the wyrm would float on the
            // refund pile instead of falling like a beaver does after
            // its floor disappears.
            var objects = _blockService.GetObjectsAt(cell);
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].HasComponent<INonStackPickable>()) continue;
                return true;
            }
            return false;
        }
    }
}
