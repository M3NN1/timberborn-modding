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
    /// Wyrm movement: path planning on the tick scheduler, transform
    /// interpolation every frame so the wyrm walks smoothly between
    /// corners. Bypasses vanilla <c>PathFollower</c> +
    /// <c>MovementAnimator</c> so we don't need a rigged skinned mesh
    /// yet — swap in <c>PathFollower</c> when one ships. Movement
    /// constraints (+1 step rule, path blockers, water swimming) come
    /// for free from the vanilla nav-mesh.
    /// </summary>
    public class WyrmMovement : TickableComponent,
        IAwakableComponent, IUpdatableComponent
    {
        // Floor on replan rate so a stable target doesn't pin the
        // pathfinder. Replan also fires when we exhaust the corner list
        // or the target jumps far enough.
        private const int MinTicksBetweenPlans = 6;
        private const float TargetMovedThreshold = 1.0f;

        // Slow enough to read as a visible slump rather than a teleport.
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
            // Resolved here (not StartTickable) so the per-frame Update
            // sees a non-null spec and can apply gravity correctly even
            // before the first Tick lands.
            _wyrm = GetComponent<WyrmComponent>();
            _spec = GetComponent<WyrmSpec>();
        }

        /// <summary>
        /// Set or update the live target. Tiny jitter is ignored;
        /// jumps over <see cref="TargetMovedThreshold"/> trigger a replan.
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
                // Corner 0 is the start position; we walk to subsequent ones.
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
        /// Per-frame transform interpolation. Bound by the engine via
        /// <see cref="IUpdatableComponent"/> — Unity itself doesn't
        /// call Update on BaseComponents.
        /// </summary>
        public void Update()
        {
            if (_spec == null) return;

            ApplyGravityIfFloating();

            if (!_hasTarget) return;
            if (_pathCorners.Count == 0 || _nextCornerIndex >= _pathCorners.Count) return;

            // Hunger-scaled: WyrmComponent.EffectiveWalkSpeed handles
            // the lerp between digesting and starving.
            float speed = _wyrm != null
                ? _wyrm.EffectiveWalkSpeed
                : _spec.WalkSpeed;
            float stepBudget = speed * Time.deltaTime;

            // Walk through as many corners as the per-frame budget
            // covers. On slow-frame spikes we may snap through several;
            // that's correct.
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

        private void ApplyGravityIfFloating()
        {
            var here = Transform.position;
            var grid = CoordinateSystem.WorldToGridInt(here);
            if (HasSupportBelow(grid)) return;

            float drop = FallSpeed * Time.deltaTime;
            float newY = here.y - drop;

            // Snap to the top of the next supported cell. probeZ-1
            // because the wyrm's grid.z is the cell it currently
            // occupies; we want the floor *below* that.
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
                        // Old corners point to mid-air; force a replan
                        // from the new position.
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
                // No floor anywhere; clamp at z=0 instead of -∞.
                newY = Mathf.Max(0f, newY);
            }

            // Log only on landing to avoid spamming during the fall.
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
            if (_terrainService.Underground(cell)) return true;
            // Block-objects count as floor — levees, foundations, lodges,
            // even our own husk and den. Exception: recovered-good piles
            // are tagged INonStackPickable; without filtering them out
            // the wyrm would float on a refund pile instead of falling
            // like a beaver does.
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
