using Mods.WhereWyrmsWait.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.Coordinates;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Per-tick badwater-drink driver. Every
    /// <see cref="RecomputeEveryTicks"/> ticks, asks the water service
    /// to remove a slice of contaminated water from the wyrm's tile and
    /// replaces it with clean water. The depth removed is added to the
    /// wyrm's contamination bucket. Three properties fall out of routing
    /// through the engine's water system:
    /// <list type="bullet">
    /// <item>Concentration scales lethality — a 50/50 mix has half as
    /// much contaminated water to drink.</item>
    /// <item>Static badwater puddles drain dry, so killing a wyrm
    /// requires sustained inflow.</item>
    /// <item>The wyrm purifies the water as a side effect — flowing
    /// badwater downstream of a wyrm zone comes out cleaner.</item>
    /// </list>
    /// </summary>
    public class WyrmContaminationSampler : TickableComponent
    {
        private const int RecomputeEveryTicks = 8;
        // Skip rounding-noise drinks to keep the water-change queue light.
        private const float MinDrinkableDepth = 0.001f;

        private readonly IThreadSafeWaterMap _waterMap;
        private readonly IWaterService _waterService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly WyrmSettings _settings;

        private WyrmComponent _wyrm;
        private WyrmSpec _spec;
        private int _ticksSinceRecompute = RecomputeEveryTicks;

        public WyrmContaminationSampler(
            IThreadSafeWaterMap waterMap,
            IWaterService waterService,
            IDayNightCycle dayNightCycle,
            WyrmSettings settings)
        {
            _waterMap = waterMap;
            _waterService = waterService;
            _dayNightCycle = dayNightCycle;
            _settings = settings;
        }

        public override void StartTickable()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _spec = GetComponent<WyrmSpec>();
        }

        public override void Tick()
        {
            if (_wyrm == null) return;
            if (_settings == null)
            {
                WyrmDiagnostics.LogOnce(
                    "WyrmContaminationSampler.SettingsNull",
                    "WyrmSettings was null in WyrmContaminationSampler — DI binding issue.");
                return;
            }
            if (_ticksSinceRecompute++ < RecomputeEveryTicks) return;
            _ticksSinceRecompute = 0;

            float deltaDays =
                _dayNightCycle.FixedDeltaTimeInHours / 24f * RecomputeEveryTicks;

            if (_settings.SandboxMode)
            {
                _wyrm.RegenContamination(deltaDays);
                return;
            }

            float drunk = TryDrinkContamination(deltaDays);
            if (drunk > 0f)
            {
                _wyrm.AbsorbContamination(drunk);
            }
            else
            {
                _wyrm.RegenContamination(deltaDays);
            }
        }

        private float TryDrinkContamination(float deltaDays)
        {
            try
            {
                var coords = CoordinateSystem.WorldToGridInt(Transform.position);
                float waterDepth = _waterMap.WaterDepth(coords);
                if (waterDepth <= 0f) return 0f;

                float concentration = _waterMap.ColumnContamination(coords);
                if (concentration <= 0f) return 0f;

                float availableContaminated = waterDepth * concentration;
                if (availableContaminated < MinDrinkableDepth) return 0f;

                // Clamp wanted-drink to what's physically in the column
                // so a half-empty puddle doesn't credit more
                // contamination than it actually held.
                float wanted = (_spec?.DrinkDepthPerDay ?? 1f) * deltaDays;
                float drunk = Mathf.Min(wanted, availableContaminated);
                if (drunk <= 0f) return 0f;

                // Two enqueued changes: remove `drunk` depth of
                // contaminated water, add the same depth of clean
                // water. Net effect on the column is "same depth,
                // proportionally less contaminated."
                _waterService.RemoveContaminatedWater(coords, drunk);
                _waterService.AddCleanWater(coords, drunk);
                return drunk;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Contamination drink failed at " +
                    $"{Transform?.position}: {ex.Message}");
                return 0f;
            }
        }
    }
}
