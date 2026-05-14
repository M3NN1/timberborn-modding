using System.Collections.Generic;
using System.Linq;
using Mods.DamDegradation.Components;
using Timberborn.BlockObjectAccesses;
using UnityEngine;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Picks a world position from which a beaver can repair the dam.
    /// Delegates to the engine's <see cref="BlockObjectAccessGenerator"/>,
    /// which is the same component vanilla uses to decide where builders can
    /// stand to construct or demolish a block. It accounts for stairs,
    /// terrain, paths and any other walkable surface around the dam.
    /// <para>
    /// We avoid wrapping this in our own <see cref="Timberborn.Navigation.Accessible"/>
    /// because the construction site already has one while the dam is being
    /// built — adding a second leads to "More than one component of type
    /// Accessible found" crashes. Instead we sample the generator at runtime
    /// and walk via <see cref="Timberborn.WalkingSystem.WalkToPositionExecutor"/>.
    /// </para>
    /// <para>
    /// IMPORTANT: <c>BlockObjectAccessGenerator.GenerateAccesses</c> is a
    /// chained C# iterator that only clears its private bookkeeping
    /// (<c>_visitedValidAccesses</c>, <c>_terrainHeightCache</c>) at the
    /// natural end of enumeration. Stopping early (e.g. <c>FirstOrDefault</c>)
    /// leaves stale state and subsequent calls return fewer or zero results.
    /// We force full enumeration via <c>ToList()</c> before picking.
    /// </para>
    /// </summary>
    internal static class DamApproach
    {
        // Reach used for sampling: -2 below to +1 above the dam's z. The
        // generator scans every horizontal neighbour at every z in this range
        // and returns the cells that are actually walkable (terrain, paths,
        // stair tops, other dams). Beavers can lean further down than up, so
        // we ask for asymmetric reach.
        private const int MinZOffset = -2;
        private const int MaxZOffset = 1;

        /// <summary>
        /// Returns any approach world position. Use the overload that takes a
        /// reference position when you have a builder location to rank by.
        /// </summary>
        public static bool TryGetApproachWorldPosition(
            DamDeterioration dam,
            out Vector3 worldPosition)
        {
            return TryGetApproachWorldPosition(dam, null, out worldPosition);
        }

        /// <summary>
        /// Returns the access closest to <paramref name="referencePosition"/>
        /// (typically the builder's current world position). When the
        /// reference is null, returns the first generated access.
        /// </summary>
        public static bool TryGetApproachWorldPosition(
            DamDeterioration dam,
            Vector3? referencePosition,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (dam == null || dam.GameObject == null)
            {
                return false;
            }
            var generator = dam.GetComponent<BlockObjectAccessGenerator>();
            if (generator == null)
            {
                return false;
            }
            int z = dam.Coordinates.z;

            // ToList() forces full enumeration so the generator's internal
            // Clear() runs. Without this, the generator's HashSet/Dictionary
            // hold stale state across calls and we get phantom misses.
            List<Vector3> accesses =
                generator.GenerateAccesses(z + MinZOffset, z + MaxZOffset).ToList();
            if (accesses.Count == 0)
            {
                return false;
            }
            if (referencePosition.HasValue)
            {
                Vector3 reference = referencePosition.Value;
                worldPosition = accesses
                    .OrderBy(a => (a - reference).sqrMagnitude)
                    .First();
            }
            else
            {
                worldPosition = accesses[0];
            }
            return true;
        }
    }
}
