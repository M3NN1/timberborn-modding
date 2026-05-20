using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Map-author-placed marker for a wyrm spawner. Same wake rules as
    /// <see cref="WyrmHuskSpec"/> but instead of producing one wyrm and
    /// being consumed, the den keeps spawning wyrms periodically while
    /// the cover above remains green.
    /// <para>
    /// Killed only by dynamite — see DESIGN.md. The den subscribes to
    /// <c>ExplosionService.TilesExplosion</c> in
    /// <see cref="WyrmDen"/>; any explosion that hits the den's
    /// coordinate destroys it.
    /// </para>
    /// </summary>
    public record WyrmDenSpec : ComponentSpec
    {
        /// <summary>
        /// Base warmup before the *first* wyrm spawn after irrigation
        /// arrives. Same shape as <see cref="WyrmHuskSpec.BaseWarmupDays"/>.
        /// </summary>
        [Serialize]
        public float BaseWarmupDays { get; init; } = 0.5f;

        /// <summary>Same as <see cref="WyrmHuskSpec.DaysPerCoverBlock"/>.</summary>
        [Serialize]
        public float DaysPerCoverBlock { get; init; } = 3f;

        /// <summary>Same as <see cref="WyrmHuskSpec.EmergenceShiftRadius"/>.</summary>
        [Serialize]
        public int EmergenceShiftRadius { get; init; } = 2;

        /// <summary>
        /// Cooldown between spawns once active. Default 6 in-game days —
        /// not too dense, not too lazy. Multiplied by the global wake
        /// speed setting.
        /// </summary>
        [Serialize]
        public float DaysBetweenSpawns { get; init; } = 6f;

        /// <summary>
        /// Maximum live wyrms this den keeps active simultaneously. Once
        /// the cap is hit, no new spawn until one of its wyrms dies.
        /// Default 3 — manageable for the player without trivializing the
        /// threat.
        /// </summary>
        [Serialize]
        public int MaxLiveWyrms { get; init; } = 3;

        /// <summary>
        /// Horizontal world-units radius around the den used to count
        /// "this den's wyrms" against <see cref="MaxLiveWyrms"/>. A wyrm
        /// that wanders beyond this radius stops counting toward its
        /// originating den's cap, freeing the den to spawn another.
        /// <para>
        /// Note that two dens within twice this radius will share each
        /// other's wyrms in their per-den counts — they're effectively
        /// pooled. Map authors who want strictly independent dens should
        /// place them at least 2× this distance apart.
        /// </para>
        /// </summary>
        [Serialize]
        public float SpawnTrackingRadius { get; init; } = 24f;
    }
}
