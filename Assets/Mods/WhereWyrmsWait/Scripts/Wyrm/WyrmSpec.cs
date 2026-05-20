using Timberborn.BlueprintSystem;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Marker + per-blueprint tunables for the active Wyrm creature. The
    /// blueprint that carries this spec is NOT placeable — it's the
    /// template <see cref="WyrmFactory"/> looks up via
    /// <c>TemplateService.GetSingle&lt;WyrmSpec&gt;()</c> and instantiates
    /// at the emergence tile when a husk wakes.
    /// </summary>
    public record WyrmSpec : ComponentSpec
    {
        /// <summary>
        /// Total contaminated-water units the wyrm can absorb before dying.
        /// Measured in column depth-units (the same scale vanilla uses for
        /// water depth — 1.0 ≈ a full block of pure badwater). Default 5
        /// means roughly five in-game days of standing in pure badwater
        /// at the default drink rate before lethality.
        /// <para>
        /// Overridden at runtime by
        /// <c>WyrmSettings.ContaminationResistanceMultiplier</c>.
        /// </para>
        /// </summary>
        [Serialize]
        public float LethalContamination { get; init; } = 5f;

        /// <summary>
        /// How much water depth the wyrm draws from its current tile per
        /// in-game day at full saturation (concentration 1.0 / pure
        /// badwater). The engine clamps the actual draw to whatever
        /// contaminated water is available, so this is an upper bound.
        /// In mixed water the contaminated portion is what's drunk —
        /// dilution proportionally slows the kill.
        /// </summary>
        [Serialize]
        public float DrinkDepthPerDay { get; init; } = 1f;

        /// <summary>
        /// Contamination shed per in-game day on ticks where no badwater
        /// was drunk. Net rate (drink - regen) drives lethality; keeping
        /// regen positive but smaller than the drink rate means killing
        /// a wyrm requires sustained, mostly-contiguous badwater
        /// coverage rather than a quick splash.
        /// </summary>
        [Serialize]
        public float ContaminationRegenPerDay { get; init; } = 0.2f;

        /// <summary>
        /// Hunger increase per in-game day. At <see cref="HungerMax"/> the
        /// wyrm starts chewing walls; eating a beaver resets hunger to 0.
        /// </summary>
        [Serialize]
        public float HungerPerDay { get; init; } = 1f;

        /// <summary>
        /// Hunger cap. Beyond this value hunger doesn't grow — the wyrm
        /// is just "very hungry" and stays in attack mode.
        /// </summary>
        [Serialize]
        public float HungerMax { get; init; } = 1f;

        /// <summary>
        /// HP per day a hungry wyrm chews off an adjacent player-built
        /// block (levee, dam, wall). Sated wyrms don't chew.
        /// </summary>
        [Serialize]
        public float WallChewPerDay { get; init; } = 5f;

        /// <summary>
        /// World-units-per-second walking speed for the wyrm. Slower than
        /// beavers (vanilla beaver is ~1.5) so a careful player can outrun
        /// one in the open and lure-stake choices have weight.
        /// <para>
        /// Effective speed is scaled by hunger between
        /// <see cref="MinSpeedMultiplier"/> (hunger 0, freshly fed) and
        /// 1.0 (hunger at <see cref="HungerMax"/>). Sated wyrms also slow
        /// to <see cref="MinSpeedMultiplier"/>.
        /// </para>
        /// </summary>
        [Serialize]
        public float WalkSpeed { get; init; } = 1.0f;

        /// <summary>
        /// Hunger fraction (of <see cref="HungerMax"/>) at which the wyrm
        /// switches from "digesting" to "hunting." Below this, the wyrm
        /// ignores beavers and doesn't chew walls — it just crawls. Above,
        /// it pursues prey and tears through obstacles.
        /// </summary>
        [Serialize]
        public float HuntingThreshold { get; init; } = 0.6f;

        /// <summary>
        /// Floor for the hunger-scaled walk speed. At hunger 0 (or while
        /// Soothesop-sated), effective speed is
        /// <see cref="WalkSpeed"/> × this multiplier. Default 0.2 means a
        /// freshly-fed wyrm moves at 20% speed — visible threat presence
        /// without an instant second course.
        /// </summary>
        [Serialize]
        public float MinSpeedMultiplier { get; init; } = 0.2f;
    }
}
