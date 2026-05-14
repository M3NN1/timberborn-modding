using Timberborn.BlueprintSystem;

namespace Mods.DamDegradation.Components
{
    /// <summary>
    /// ComponentSpec attached to dam / levee / floodgate blueprints to mark them as deteriorating.
    /// All gameplay tunables live here so balancing happens in JSON, not in code.
    /// </summary>
    public record DamDeteriorationSpec : ComponentSpec
    {
        /// <summary>Maximum health of the block. Default 100.</summary>
        [Serialize]
        public float MaxHealth { get; init; } = 100f;

        /// <summary>
        /// Wear (HP per in-game day) applied even when no water is present.
        /// Default <c>0</c> — built blocks do not deteriorate without hydraulic load.
        /// </summary>
        [Serialize]
        public float BaseWearPerDay { get; init; } = 0f;

        /// <summary>
        /// Stress wear coefficient. Effective wear per day is
        /// <c>BaseWearPerDay + StressWearCoefficient * waterDepth^StressExponent</c>,
        /// scaled by support factor and the global multiplier.
        /// Default 0.09 — at ~5 m depth a single block dies after roughly 100 in-game days.
        /// </summary>
        [Serialize]
        public float StressWearCoefficient { get; init; } = 0.09f;

        /// <summary>
        /// Exponent applied to water depth when computing stress wear. A value
        /// greater than 1 makes deeper reservoirs degrade disproportionately faster.
        /// Default 1.5.
        /// </summary>
        [Serialize]
        public float StressExponent { get; init; } = 1.5f;

        /// <summary>
        /// Wear multiplier when there is a deteriorating block directly downstream.
        /// Should be &lt; 1.0; default 0.5 — a back-supported wall takes half wear.
        /// </summary>
        [Serialize]
        public float DownstreamSupportFactor { get; init; } = 0.5f;

        /// <summary>
        /// Health below which the dam is shown as "needs repair" (yellow) and
        /// a status icon appears.
        /// Default 80%.
        /// </summary>
        [Serialize]
        public float WarningHealthFraction { get; init; } = 0.8f;

        /// <summary>
        /// Health below which the dam is shown as "critical" (red), starts to
        /// leak via a partial obstacle, and notifies the player.
        /// Default 20%.
        /// </summary>
        [Serialize]
        public float CriticalHealthFraction { get; init; } = 0.2f;

        /// <summary>HP restored by a single repair action. Default 25.</summary>
        [Serialize]
        public float RepairAmount { get; init; } = 25f;

        /// <summary>
        /// Damage applied to each adjacent dam block when this one breaches.
        /// One hop only — no chain reactions in the same tick. Default 25.
        /// </summary>
        [Serialize]
        public float CascadeDamage { get; init; } = 25f;

        /// <summary>
        /// When true, below the critical health threshold the dam meters water
        /// flow through its own coordinate via <c>IWaterService.SetInflowLimit</c>
        /// — the same engine API the Throttling Valve uses. Water genuinely
        /// passes through the dam from upstream to downstream, capped at the
        /// rate set by <see cref="MaxLeakRatePerTick"/>. Conservation, flow
        /// vectors and contamination are handled by the engine.
        /// Default <c>true</c>.
        /// </summary>
        [Serialize]
        public bool EnableLeakage { get; init; } = true;

        /// <summary>
        /// Maximum metered water flow per tick that passes through the dam
        /// when health is at zero. Scales linearly from zero at the critical
        /// threshold to this value at zero HP. Default 0.02 — a slow but
        /// visible trickle. Increase for more dramatic floods.
        /// </summary>
        [Serialize]
        public float MaxLeakRatePerTick { get; init; } = 0.02f;
    }
}