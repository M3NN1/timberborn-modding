using Timberborn.BlockSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Picks the cell where a wyrm climbs out of a husk.
    /// Rule:
    /// 1. If the cell directly above the husk is dirt, walk upward
    ///    through dirt and pick the first air cell.
    /// 2. Otherwise the husk's own cell is the spawn cell.
    /// 3. If the picked cell is blocked by a building, fall back to the
    ///    closest walkable cell within <c>searchRadius</c>.
    /// </summary>
    public class WyrmEmergencePicker
    {
        // Body-blocking occupations: cells holding a building, wall,
        // bottom, or corner block. Floor / path occupations don't count
        // as "blocked" — beavers walk on top of those, so a wyrm can too.
        private const BlockOccupations BodyBlocking =
            BlockOccupations.Top
            | BlockOccupations.Middle
            | BlockOccupations.Bottom
            | BlockOccupations.Corners;

        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;

        public WyrmEmergencePicker(
            ITerrainService terrainService,
            IBlockService blockService)
        {
            _terrainService = terrainService;
            _blockService = blockService;
        }

        public bool TryPick(
            Vector3Int huskCoordinates,
            int searchRadius,
            out Vector3Int emergenceCoordinates)
        {
            emergenceCoordinates = default;

            var col2D = new Vector2Int(huskCoordinates.x, huskCoordinates.y);
            if (!_terrainService.Contains(col2D))
            {
                return false;
            }

            if (!TryFindAirAboveHusk(huskCoordinates, out var target))
            {
                return false;
            }

            if (!IsBuildingBlocked(target))
            {
                emergenceCoordinates = target;
                return true;
            }

            return TryFindClosestWalkable(target, searchRadius, out emergenceCoordinates);
        }

        private bool TryFindAirAboveHusk(Vector3Int husk, out Vector3Int air)
        {
            int sizeZ = _terrainService.Size.z;
            var above = new Vector3Int(husk.x, husk.y, husk.z + 1);

            // No dirt right above (or top of world): husk's own cell wins.
            if (above.z >= sizeZ || !_terrainService.Underground(above))
            {
                air = husk;
                return true;
            }

            // Dirt above: walk up until the first non-terrain cell.
            for (int z = above.z; z < sizeZ; z++)
            {
                var cell = new Vector3Int(husk.x, husk.y, z);
                if (!_terrainService.Underground(cell))
                {
                    air = cell;
                    return true;
                }
            }
            air = default;
            return false;
        }

        private bool TryFindClosestWalkable(
            Vector3Int origin, int searchRadius, out Vector3Int result)
        {
            // Cube-shell expansion; on each shell pick the cell with the
            // smallest sqr-distance to origin. The first shell with any
            // walkable hit is guaranteed to contain the closest.
            for (int r = 1; r <= searchRadius; r++)
            {
                Vector3Int best = default;
                float bestDist = float.MaxValue;
                bool found = false;

                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int chebyshev = Mathf.Max(
                        Mathf.Abs(dx),
                        Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
                    if (chebyshev != r) continue;

                    var probe = new Vector3Int(origin.x + dx, origin.y + dy, origin.z + dz);
                    if (!IsWalkable(probe)) continue;

                    float d = (probe - origin).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = probe;
                        found = true;
                    }
                }

                if (found)
                {
                    result = best;
                    return true;
                }
            }
            result = default;
            return false;
        }

        private bool IsWalkable(Vector3Int cell)
        {
            if (!_terrainService.Contains(cell)) return false;
            if (_terrainService.Underground(cell)) return false;
            if (IsBuildingBlocked(cell)) return false;
            return HasGroundBelow(cell);
        }

        private bool HasGroundBelow(Vector3Int cell)
        {
            var below = new Vector3Int(cell.x, cell.y, cell.z - 1);
            if (below.z < 0) return true;
            if (_terrainService.Underground(below)) return true;
            // Block-objects with a top / path / floor occupation count
            // as ground — beavers can stand on them and so can a wyrm.
            return _blockService.AnyTopObjectAt(below)
                || _blockService.GetPathObjectAt(below) != null
                || _blockService.GetBottomObjectAt(below) != null;
        }

        private bool IsBuildingBlocked(Vector3Int cell)
        {
            return _blockService.AnyNonOverridableObjectsAt(cell, BodyBlocking);
        }
    }
}
