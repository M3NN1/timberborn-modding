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
    /// Per-tick badwater-drink driver for the wyrm.
    /// <para>
    /// The wyrm passively absorbs whatever contaminated water sits in the
    /// column under its feet. We model that by enqueueing a real water
    /// change every sample cycle: <see cref="IWaterService.RemoveContaminatedWater"/>
    /// pulls a slice of the contaminated portion out of the column (the
    /// engine clamps to whatever's actually there). The depth of the slice
    /// we successfully removed is the same number we add to the wyrm's
    /// contamination bucket via <see cref="WyrmComponent.AbsorbContamination"/>.
    /// </para>
    /// <para>
    /// Effects of routing through the engine's water system:
    /// <list type="bullet">
    /// <item>Concentration scales lethality automatically — a 50/50 mix
    /// has half as much contaminated water in the column to drink, so
    /// the wyrm absorbs at half speed.</item>
    /// <item>The wyrm physically removes badwater from the world. A
    /// static badwater puddle gets drained dry over time, which means
    /// killing a wyrm requires sustained inflow, matching the design.
    /// As a side effect, players who route badwater through a wyrm zone
    /// can produce clean water once the wyrm has drunk the bad portion
    /// — a deliberate emergent verb.</item>
    /// <item>If the column is empty or fully clean, the request returns
    /// no contaminated water removed; the wyrm regenerates contamination
    /// instead.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Sampling cadence: the water sim updates at the engine's tick rate;
    /// sampling every <see cref="RecomputeEveryTicks"/> ticks (~0.8s game
    /// time) is plenty and keeps the per-tick water-change queue light.
    /// </para>
    /// </summary>
    public class WyrmContaminationSampler : TickableComponent
    {
        private const int RecomputeEveryTicks = 8;

        // Below this many depth-units of contaminated water in the column
        // we treat it as "no badwater here" — avoids spending water-change
        // queue slots on rounding noise.
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

            // Number of in-game days that elapsed across the sample window.
            // FixedDeltaTimeInHours is per-engine-tick, so we multiply by
            // the cadence to get the real interval.
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
                if (waterDepth <= 0f)
                {
                    return 0f;
                }
                float concentration = _waterMap.ColumnContamination(coords);
                if (concentration <= 0f)
                {
                    return 0f;
                }

                // What's actually available in this column right now.
                float availableContaminated = waterDepth * concentration;
                if (availableContaminated < MinDrinkableDepth)
                {
                    return 0f;
                }

                // What the wyrm wants to drink across this sample window.
                // Clamped to what's actually there so a half-empty puddle
                // doesn't credit more contamination than it physically held.
                float wanted = (_spec?.DrinkDepthPerDay ?? 1f) * deltaDays;
                float drunk = Mathf.Min(wanted, availableContaminated);
                if (drunk <= 0f) return 0f;

                // Real water-system change: the wyrm absorbs the dirty
                // fraction of the column and excretes clean water. We
                // mirror that with two enqueued changes — first removing
                // `drunk` depth of fully contaminated water, then adding
                // back the same depth as clean water. The wyrm keeps the
                // contamination internally; the column ends up at the
                // same depth but proportionally less contaminated.
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
