using Timberborn.BaseComponentSystem;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.Navigation;
using Timberborn.TerrainSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Idle wandering for non-hunting wyrms. While the wyrm isn't hunting
    /// (digesting, Soothesop-sated, or sandbox mode), this picks a random
    /// reachable tile within a small radius every few seconds and feeds
    /// it to <see cref="WyrmMovement"/> as the target. The result reads
    /// as "wyrm shuffles aimlessly while not on the prowl" rather than
    /// the previous "wyrm freezes solid until prey appears."
    /// <para>
    /// Targeting precedence is owned by <see cref="WyrmHunter"/>: when
    /// hunting flips on, the hunter overwrites the movement target with
    /// the nearest beaver and the wanderer steps aside until hunting
    /// flips off again. The wanderer never argues with an active hunt.
    /// </para>
    /// </summary>
    public class WyrmWanderer : TickableComponent, IAwakableComponent
    {
        private const int WanderRadiusTiles = 4;
        private const int MinWaitTicks = 12;
        private const int MaxWaitTicks = 36;
        // Reject candidate tiles that are essentially the wyrm's current
        // tile so we don't no-op our way through ticks.
        private const int MinHopTiles = 2;

        private readonly INavigationService _navigationService;
        private readonly ITerrainService _terrainService;
        private readonly IRandomNumberGenerator _random;

        private WyrmComponent _wyrm;
        private WyrmMovement _movement;

        // Private to avoid re-allocating a path-corner buffer per pick.
        private readonly System.Collections.Generic.List<PathCorner> _scratch =
            new System.Collections.Generic.List<PathCorner>(8);

        // True when the current movement target was set by us. Lets us
        // refuse to override a hunter-owned target (HasTarget is true,
        // but we didn't set it) and lets us reset cleanly when hunting
        // flips on.
        private bool _ownsTarget;
        private int _ticksUntilNextPick;

        public WyrmWanderer(
            INavigationService navigationService,
            ITerrainService terrainService,
            IRandomNumberGenerator random)
        {
            _navigationService = navigationService;
            _terrainService = terrainService;
            _random = random;
        }

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _movement = GetComponent<WyrmMovement>();
            _ticksUntilNextPick = NextWaitTicks();
        }

        public override void Tick()
        {
            if (_wyrm == null || _movement == null) return;

            // While the wyrm is hunting AND the hunter has actually set a
            // movement target (a beaver in range), let the hunter own
            // movement. If hunting is on but the hunter found no beaver,
            // we still want the wyrm to wander instead of freezing — so
            // we drop the early return when no target is set.
            if (_wyrm.IsHunting && _movement.HasTarget && !_ownsTarget)
            {
                return;
            }

            // If we have an existing wander target and the wyrm hasn't
            // arrived, let it keep walking. Same applies whether or not
            // hunting is on — a hunter without a beaver shouldn't yank
            // the wheel from us.
            if (_ownsTarget && !_movement.ReachedTarget)
            {
                return;
            }

            // Cooldown between hops so the wyrm visibly pauses.
            if (_ticksUntilNextPick-- > 0)
            {
                if (_ownsTarget)
                {
                    _movement.ClearTarget();
                    _ownsTarget = false;
                }
                return;
            }
            _ticksUntilNextPick = NextWaitTicks();

            if (TryPickWanderDestination(out var dest))
            {
                _movement.SetTarget(dest);
                _ownsTarget = true;
            }
        }

        private int NextWaitTicks()
        {
            return _random.Range(MinWaitTicks, MaxWaitTicks + 1);
        }

        private bool TryPickWanderDestination(out Vector3 destination)
        {
            // Pick a random (dx, dy) offset in grid space within the
            // wander radius. Project that offset's column down to its
            // surface (the empty cell directly above the topmost
            // terrain) — that's the navigable tile a beaver could stand
            // on, and therefore a valid destination for our wyrm.
            //
            // Picking on a grid basis (rather than continuous offsets)
            // ensures the destination is always grid-aligned and on the
            // navigable surface, which matters because the nav-service
            // can't path to mid-air points.
            var here = CoordinateSystem.WorldToGridInt(Transform.position);
            int aboveAll = _terrainService.MaxTerrainHeight + 1;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int dx = _random.Range(-WanderRadiusTiles, WanderRadiusTiles + 1);
                int dy = _random.Range(-WanderRadiusTiles, WanderRadiusTiles + 1);
                if (Mathf.Abs(dx) + Mathf.Abs(dy) < MinHopTiles)
                {
                    continue;
                }
                var probe = new Vector3Int(
                    here.x + dx, here.y + dy, aboveAll);

                int surfaceZ;
                try
                {
                    surfaceZ = _terrainService.GetTerrainHeight(probe);
                }
                catch
                {
                    continue;
                }
                if (surfaceZ <= 0) continue;

                var destGrid = new Vector3Int(
                    here.x + dx, here.y + dy, surfaceZ);
                Vector3 destWorld = CoordinateSystem.GridToWorld(destGrid);

                _scratch.Clear();
                if (_navigationService.FindPathUnlimitedRange(
                    Transform.position, destWorld, _scratch, out _))
                {
                    destination = destWorld;
                    return true;
                }
            }
            destination = default;
            return false;
        }
    }
}
