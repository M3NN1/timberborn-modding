using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Per-blueprint tunables for the Lure Stake. Holds Soothesop
    /// hauled from the Wyrm Forager; while stocked, wyrms within
    /// <see cref="SatiateRadius"/> tiles are sated and stop chewing
    /// walls (they still hunt beavers).
    /// </summary>
    public record LureStakeSpec : ComponentSpec
    {
        [Serialize]
        public int Capacity { get; init; } = 5;

        /// <summary>Sphere radius in tiles within which wyrms are sated.</summary>
        [Serialize]
        public int SatiateRadius { get; init; } = 3;

        /// <summary>
        /// Soothesop consumed per wyrm per in-game day in range. With
        /// multiple wyrms, the stake empties faster.
        /// </summary>
        [Serialize]
        public float ConsumptionPerWyrmPerDay { get; init; } = 1f;
    }
}
