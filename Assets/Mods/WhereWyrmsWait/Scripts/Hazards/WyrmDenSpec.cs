using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Map-author-placed wyrm spawner. Same wake rules as
    /// <see cref="WyrmHuskSpec"/>, but instead of producing one wyrm
    /// and being consumed, the den keeps spawning while the cover
    /// stays green. Killed only by dynamite (see
    /// <see cref="WyrmDen"/>'s explosion handler).
    /// </summary>
    public record WyrmDenSpec : ComponentSpec
    {
        [Serialize]
        public float BaseWarmupDays { get; init; } = 0.5f;

        [Serialize]
        public float DaysPerCoverBlock { get; init; } = 3f;

        [Serialize]
        public int EmergenceShiftRadius { get; init; } = 2;

        /// <summary>Cooldown between spawns once active.</summary>
        [Serialize]
        public float DaysBetweenSpawns { get; init; } = 6f;

        /// <summary>Maximum live wyrms attributed to this den simultaneously.</summary>
        [Serialize]
        public int MaxLiveWyrms { get; init; } = 3;
    }
}
