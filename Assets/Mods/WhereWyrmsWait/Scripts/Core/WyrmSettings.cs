using ModSettings.Common;
using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace Mods.WhereWyrmsWait.Core
{
    /// <summary>
    /// Mod-wide tuning surfaced to the player through the eMka.ModSettings
    /// panel. Mirrors the pattern used by <c>DamSettings</c> in DDD so the
    /// same architectural lessons apply: each property is read lazily on
    /// access, so changes from the panel apply live without a restart.
    /// </summary>
    public class WyrmSettings : ModSettingsOwner
    {
        public ModSetting<bool> ModEnabledSetting { get; } = new(
            true,
            ModSettingDescriptor
                .CreateLocalized("WWW.Settings.ModEnabled")
                .SetLocalizedTooltip("WWW.Settings.ModEnabled.Tooltip"));

        public RangeIntModSetting WakeSpeedPercentSetting { get; } = new(
            100, 50, 200,
            ModSettingDescriptor
                .CreateLocalized("WWW.Settings.WakeSpeedPercent")
                .SetLocalizedTooltip("WWW.Settings.WakeSpeedPercent.Tooltip"));

        public RangeIntModSetting HungerRatePercentSetting { get; } = new(
            100, 10, 300,
            ModSettingDescriptor
                .CreateLocalized("WWW.Settings.HungerRatePercent")
                .SetLocalizedTooltip("WWW.Settings.HungerRatePercent.Tooltip"));

        public RangeIntModSetting ContaminationResistancePercentSetting { get; } = new(
            100, 25, 400,
            ModSettingDescriptor
                .CreateLocalized("WWW.Settings.ContaminationResistancePercent")
                .SetLocalizedTooltip("WWW.Settings.ContaminationResistancePercent.Tooltip"));

        public ModSetting<bool> SandboxModeSetting { get; } = new(
            false,
            ModSettingDescriptor
                .CreateLocalized("WWW.Settings.SandboxMode")
                .SetLocalizedTooltip("WWW.Settings.SandboxMode.Tooltip"));

        public WyrmSettings(
            ISettings settings,
            ModSettingsOwnerRegistry modSettingsOwnerRegistry,
            ModRepository modRepository) : base(
            settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        public override string HeaderLocKey => "WWW.Settings.Header";

        public override ModSettingsContext ChangeableOn =>
            ModSettingsContext.MainMenu | ModSettingsContext.Game;

        protected override string ModId => "M3NN1.WhereWyrmsWait";

        // Public API consumed by the rest of the mod. Lazy reads so panel
        // edits apply immediately to live wyrms/husks.
        public bool ModEnabled => ModEnabledSetting.Value;
        public float WakeSpeedMultiplier => WakeSpeedPercentSetting.Value / 100f;
        public float HungerRateMultiplier => HungerRatePercentSetting.Value / 100f;
        public float ContaminationResistanceMultiplier =>
            ContaminationResistancePercentSetting.Value / 100f;
        public bool SandboxMode => SandboxModeSetting.Value;
    }
}
