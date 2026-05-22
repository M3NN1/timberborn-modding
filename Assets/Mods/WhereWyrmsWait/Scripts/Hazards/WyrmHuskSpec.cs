using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Map-author-placed marker for a single dormant wyrm. Wakes when
    /// the topmost natural-ground tile of the husk's column has
    /// positive soil moisture; sleeps again if the surface goes brown
    /// before the warmup timer expires. On wake, spawns one Wyrm and
    /// the husk is consumed.
    /// </summary>
    public record WyrmHuskSpec : ComponentSpec
    {
        /// <summary>Warmup time in days when the husk is the surface tile (cover depth 0).</summary>
        [Serialize]
        public float BaseWarmupDays { get; init; } = 0.5f;

        /// <summary>Extra warmup days per natural-ground block stacked above the husk. No hard cap.</summary>
        [Serialize]
        public float DaysPerCoverBlock { get; init; } = 3f;

        /// <summary>Horizontal-tile search radius when the surface tile directly above is blocked.</summary>
        [Serialize]
        public int EmergenceShiftRadius { get; init; } = 2;
    }
}
