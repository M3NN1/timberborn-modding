using Timberborn.BlockSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Picks the world-space coordinate where a Wyrm climbs out of a
    /// waking <see cref="WyrmHusk"/>. The "ideal" emergence cell is the
    /// open tile directly above the topmost natural-ground block of the
    /// husk's column (i.e. the surface). If that cell is occupied by a
    /// building, the picker spirals outward up to
    /// <c>EmergenceShiftRadius</c> tiles to find an open neighbour;
    /// returns false if none is available.
    /// </summary>
    public class WyrmEmergencePicker
    {
        private readonly ITerrainService _terrainService;
        private readonly IBlockService _blockService;

        public WyrmEmergencePicker(
            ITerrainService terrainService,
            IBlockService blockService)
        {
            _terrainService = terrainService;
            _blockService = blockService;
        }

        /// <summary>
        /// Tries to pick an emergence tile for a husk at the given
        /// coordinate. <paramref name="searchRadius"/> defaults to 2;
        /// passing 0 only checks the column's surface tile.
        /// </summary>
        public bool TryPick(
            Vector3Int huskCoordinates,
            int searchRadius,
            out Vector3Int emergenceCoordinates)
        {
            // Surface tile of the husk's own column.
            int surfaceZ = _terrainService.GetTerrainHeight(huskCoordinates);
            var primary = new Vector3Int(huskCoordinates.x, huskCoordinates.y, surfaceZ);
            if (IsTileOpen(primary))
            {
                emergenceCoordinates = primary;
                return true;
            }

            // Spiral outward in the XY plane. Each ring's tiles are tested
            // against their own column's surface — the wyrm always emerges
            // on the natural surface, never midair.
            for (int ring = 1; ring <= searchRadius; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        // Only test the ring's outline, not the filled disc.
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;
                        var probe = new Vector3Int(
                            huskCoordinates.x + dx,
                            huskCoordinates.y + dy,
                            0); // z replaced below
                        int probeSurface = _terrainService.GetTerrainHeight(probe);
                        var candidate = new Vector3Int(probe.x, probe.y, probeSurface);
                        if (IsTileOpen(candidate))
                        {
                            emergenceCoordinates = candidate;
                            return true;
                        }
                    }
                }
            }
            emergenceCoordinates = default;
            return false;
        }

        private bool IsTileOpen(Vector3Int coordinates)
        {
            // Open = no BlockObject occupies this cell at all. Checking
            // only GetBottomObjectAt would miss a floor or path that sits
            // above the column's surface natural-ground tile, leading to
            // wyrms spawning inside player infrastructure.
            // AnyObjectAt is the right primitive here because it includes
            // both stacked and underground occupants.
            return !_blockService.AnyObjectAt(coordinates);
        }
    }
}
