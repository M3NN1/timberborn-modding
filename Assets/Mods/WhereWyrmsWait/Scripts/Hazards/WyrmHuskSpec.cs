using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Map-author-placed marker for a single dormant wyrm. Wakes when the
    /// topmost natural-ground tile of the husk's column has positive soil
    /// moisture; sleeps again if the surface goes brown before the warmup
    /// timer expires. On wake, spawns one Wyrm and the husk is consumed.
    /// <para>
    /// All gameplay tunables live here so balancing happens per-blueprint
    /// without recompiling. Map authors can place "easy mode" husks at
    /// depth 0 and "endgame" husks at depth 9 by stacking the husk under
    /// natural terrain in the Unity editor / map tool.
    /// </para>
    /// </summary>
    public record WyrmHuskSpec : ComponentSpec
    {
        /// <summary>
        /// Base warmup time in days when the husk is the surface tile (cover
        /// depth 0). Default 0.5 — surprise wake when irrigation arrives on
        /// a shallow husk.
        /// </summary>
        [Serialize]
        public float BaseWarmupDays { get; init; } = 0.5f;

        /// <summary>
        /// Extra warmup days per natural-ground block stacked above the
        /// husk. Default 3 — depth 3 husks need ~9.5 days of continuous
        /// irrigation to wake. There is no hard cap: map authors decide
        /// the depth, players tune the global wake-speed multiplier in
        /// the settings panel for difficulty.
        /// </summary>
        [Serialize]
        public float DaysPerCoverBlock { get; init; } = 3f;

        /// <summary>
        /// Search radius (in horizontal tiles) used when the surface tile
        /// directly above the husk is blocked by a building and the wyrm
        /// has to find an alternative emergence tile. Default 2.
        /// </summary>
        [Serialize]
        public int EmergenceShiftRadius { get; init; } = 2;
    }
}
