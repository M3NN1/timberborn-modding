using ModSettings.Common;
using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace Mods.DamDegradation.Core
{
    /// <summary>
    /// Mod-wide tuning exposed to the player through the in-game Mod Settings
    /// panel (provided by the <c>eMka.ModSettings</c> helper mod). Each
    /// individual setting is persisted by Timberborn's vanilla
    /// <see cref="ISettings"/> backend (a wrapper around Unity's
    /// <c>PlayerPrefs</c>), so changes survive restarts and apply across
    /// every save game.
    /// <para>
    /// The class deliberately keeps the same public surface
    /// (<see cref="DegradationEnabled"/>, <see cref="GlobalWearMultiplier"/>,
    /// etc.) the rest of the mod was already reading, so the consumers
    /// (<c>DamDeterioration</c>, <c>DamRepairExecutor</c>,
    /// <c>DamHealthFragment</c>, <c>DamRepairJobProvider</c>) don't need to
    /// change. Internally each property is now backed by a typed
    /// <see cref="ModSetting{T}"/> that the panel can render and edit.
    /// </para>
    /// </summary>
    public class DamSettings : ModSettingsOwner
    {
        // ModSetting properties that the panel renders. Keep the property
        // names ending in "Setting" so the eMka panel auto-discovery picks
        // them up via reflection.

        /// <summary>Master switch. When false the mod ticks but never applies wear.</summary>
        public ModSetting<bool> DegradationEnabledSetting { get; } = new(
            true,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.DegradationEnabled")
                .SetLocalizedTooltip("DDD.Settings.DegradationEnabled.Tooltip"));

        /// <summary>
        /// Global wear multiplier expressed as a percentage. 100% = vanilla
        /// per-block coefficients, 50% = half wear, 200% = double wear, etc.
        /// </summary>
        public RangeIntModSetting GlobalWearPercentSetting { get; } = new(
            100, 0, 500,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.GlobalWearPercent")
                .SetLocalizedTooltip("DDD.Settings.GlobalWearPercent.Tooltip"));

        /// <summary>
        /// Per-tick random variance, in percent. 15 means each tick rolls a
        /// wear value within ±15% of the computed amount.
        /// </summary>
        public RangeIntModSetting RandomVariancePercentSetting { get; } = new(
            15, 0, 90,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.RandomVariancePercent")
                .SetLocalizedTooltip("DDD.Settings.RandomVariancePercent.Tooltip"));

        /// <summary>
        /// In-game hours a beaver spends on a single repair before
        /// productivity multipliers. Whole hours only.
        /// </summary>
        public RangeIntModSetting RepairTimeInHoursSetting { get; } = new(
            4, 1, 24,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.RepairTimeInHours")
                .SetLocalizedTooltip("DDD.Settings.RepairTimeInHours.Tooltip"));

        /// <summary>
        /// How many planks a repair would consume. Reserved for a future
        /// material-cost variant; the executor currently ignores this.
        /// </summary>
        public RangeIntModSetting RepairCostInPlanksSetting { get; } = new(
            0, 0, 20,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.RepairCostInPlanks")
                .SetLocalizedTooltip("DDD.Settings.RepairCostInPlanks.Tooltip"));

        /// <summary>
        /// If true, the entity-panel button repairs instantly (free, no
        /// beaver). Useful for debugging and for players who prefer the
        /// click-to-repair playstyle.
        /// </summary>
        public ModSetting<bool> AllowInstantRepairSetting { get; } = new(
            false,
            ModSettingDescriptor
                .CreateLocalized("DDD.Settings.AllowInstantRepair")
                .SetLocalizedTooltip("DDD.Settings.AllowInstantRepair.Tooltip"));

        public DamSettings(
            ISettings settings,
            ModSettingsOwnerRegistry modSettingsOwnerRegistry,
            ModRepository modRepository) : base(
            settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        /// <summary>Heading shown above the settings panel for this mod.</summary>
        public override string HeaderLocKey => "DDD.Settings.Header";

        /// <summary>
        /// Players can edit these settings either from the main menu or
        /// while a colony is loaded — values are stored in Unity's
        /// <c>PlayerPrefs</c> so they apply globally to every save.
        /// </summary>
        public override ModSettingsContext ChangeableOn =>
            ModSettingsContext.MainMenu | ModSettingsContext.Game;

        /// <summary>
        /// Must match the <c>Id</c> in <c>manifest.json</c>; eMka uses this
        /// to find the corresponding mod entry in the mod manager box.
        /// </summary>
        protected override string ModId => "M3NN1.DamnDamDegradation";

        // -------- Public API used by the rest of the mod --------
        // Same names and types as before so DamDeterioration, DamRepairExecutor,
        // DamHealthFragment and DamRepairJobProvider keep compiling and reading
        // values lazily on each access. That means settings changes from the
        // panel are observed live without any extra plumbing.

        public bool DegradationEnabled => DegradationEnabledSetting.Value;
        public float GlobalWearMultiplier => GlobalWearPercentSetting.Value / 100f;
        public float RandomVarianceFraction => RandomVariancePercentSetting.Value / 100f;
        public int RepairCostInPlanks => RepairCostInPlanksSetting.Value;
        public float RepairTimeInHours => RepairTimeInHoursSetting.Value;
        public bool AllowInstantRepair => AllowInstantRepairSetting.Value;
    }
}
