using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Marker + per-blueprint tunables for the active Wyrm creature.
    /// The blueprint that carries this spec is NOT placeable — it's the
    /// template <see cref="WyrmFactory"/> instantiates at the emergence
    /// tile when a husk wakes.
    /// </summary>
    public record WyrmSpec : ComponentSpec
    {
        /// <summary>
        /// Total contaminated-water units the wyrm absorbs before
        /// dying. In water-depth units (1.0 ≈ a full block of pure
        /// badwater). Settings panel can scale this via
        /// ContaminationResistanceMultiplier.
        /// </summary>
        [Serialize]
        public float LethalContamination { get; init; } = 5f;

        /// <summary>
        /// Maximum depth of pure badwater drunk per in-game day. Engine
        /// clamps to whatever's actually in the column; mixed water
        /// drinks proportionally slower.
        /// </summary>
        [Serialize]
        public float DrinkDepthPerDay { get; init; } = 1f;

        /// <summary>
        /// Contamination shed per in-game day on ticks where no
        /// badwater was drunk. Net rate (drink − regen) drives
        /// lethality, so a small puddle won't kill.
        /// </summary>
        [Serialize]
        public float ContaminationRegenPerDay { get; init; } = 0.2f;

        /// <summary>Hunger increase per in-game day, capped at <see cref="HungerMax"/>.</summary>
        [Serialize]
        public float HungerPerDay { get; init; } = 1f;

        /// <summary>Hunger cap.</summary>
        [Serialize]
        public float HungerMax { get; init; } = 1f;

        /// <summary>
        /// In-game days a hungry wyrm needs to chew through one
        /// adjacent player-built block. Sated wyrms don't chew.
        /// </summary>
        [Serialize]
        public float BlockChewDays { get; init; } = 1.5f;

        /// <summary>Walk speed in world units per second at full hunger.</summary>
        [Serialize]
        public float WalkSpeed { get; init; } = 1.0f;

        /// <summary>
        /// Hunger fraction at which the wyrm switches from
        /// "digesting" (ignores beavers, no wall chewing) to
        /// "hunting" (pursues prey, chews walls when stuck).
        /// </summary>
        [Serialize]
        public float HuntingThreshold { get; init; } = 0.6f;

        /// <summary>
        /// Floor for hunger-scaled walk speed. Effective speed at
        /// hunger 0 (or while sated) is <see cref="WalkSpeed"/> × this.
        /// </summary>
        [Serialize]
        public float MinSpeedMultiplier { get; init; } = 0.2f;
    }
}
