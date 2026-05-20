using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Per-blueprint tunables for the Lure Stake placeable. The stake
    /// holds a small stockpile of Soothesop hauled from the Wyrm
    /// Forager. While it has any in stock, Wyrms within
    /// <see cref="SatiateRadius"/> grid tiles enter the Sated state and
    /// stop chewing walls (still hunt beavers — that's player risk).
    /// </summary>
    public record LureStakeSpec : ComponentSpec
    {
        /// <summary>
        /// Maximum Soothesop the stake can hold before haulers stop
        /// requesting more. Default 5 — stake covers one wyrm for ~5
        /// in-game days at the default consumption rate.
        /// </summary>
        [Serialize]
        public int Capacity { get; init; } = 5;

        /// <summary>
        /// Horizontal grid distance within which wyrms are sated by
        /// this stake's stocked Soothesop. Default 3.
        /// </summary>
        [Serialize]
        public int SatiateRadius { get; init; } = 3;

        /// <summary>
        /// Soothesop consumed per wyrm per in-game day standing in
        /// range. Default 1. If multiple wyrms are in range they each
        /// consume independently — the stake empties faster the more
        /// wyrms it pacifies.
        /// </summary>
        [Serialize]
        public float ConsumptionPerWyrmPerDay { get; init; } = 1f;
    }
}
