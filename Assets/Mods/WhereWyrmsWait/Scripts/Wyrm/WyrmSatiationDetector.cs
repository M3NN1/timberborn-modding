using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Lure;
using Timberborn.BaseComponentSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Each tick, asks the <see cref="LureStakeRegistry"/> whether any
    /// stocked stake is within range of this wyrm. Drains a per-tick
    /// share of the stake's Soothesop while the wyrm is sated. Toggles
    /// <see cref="WyrmComponent.SetSated"/> based on the answer.
    /// <para>
    /// Sandbox mode skips the whole loop — the wyrm is permanently
    /// marked unsated, which combined with sandbox-mode wall-eater
    /// short-circuits keeps the world undamaged.
    /// </para>
    /// </summary>
    public class WyrmSatiationDetector : TickableComponent
    {
        private readonly LureStakeRegistry _registry;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly WyrmSettings _settings;

        private WyrmComponent _wyrm;

        public WyrmSatiationDetector(
            LureStakeRegistry registry,
            IDayNightCycle dayNightCycle,
            WyrmSettings settings)
        {
            _registry = registry;
            _dayNightCycle = dayNightCycle;
            _settings = settings;
        }

        public override void StartTickable()
        {
            _wyrm = GetComponent<WyrmComponent>();
        }

        public override void Tick()
        {
            if (_wyrm == null) return;
            if (_settings == null)
            {
                WyrmDiagnostics.LogOnce(
                    "WyrmSatiationDetector.SettingsNull",
                    "WyrmSettings was null in WyrmSatiationDetector — DI binding issue.");
                return;
            }
            if (_settings.SandboxMode)
            {
                _wyrm.SetSated(false);
                return;
            }

            var stake = FindFeedingStake();
            if (stake == null)
            {
                _wyrm.SetSated(false);
                return;
            }

            _wyrm.SetSated(true);
            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;
            float demand = (stake.Spec?.ConsumptionPerWyrmPerDay ?? 1f) * deltaDays;
            stake.DrainSoothesop(demand);
        }

        private LureStake FindFeedingStake()
        {
            // Pick the closest stocked stake within its own SatiateRadius.
            // Each stake defines its own range — a future "Greater Lure
            // Stake" tier could override the value without touching this
            // code.
            LureStake best = null;
            float bestSqr = float.PositiveInfinity;
            Vector3 here = Transform.position;
            foreach (var stake in _registry.Stakes)
            {
                if (stake == null || !stake.HasSoothesop) continue;
                Vector3 stakeWorld = new Vector3(
                    stake.Coordinates.x + 0.5f,
                    stake.Coordinates.z,
                    stake.Coordinates.y + 0.5f);
                float sqr = (stakeWorld - here).sqrMagnitude;
                float radius = stake.SatiateRadius;
                if (sqr > radius * radius) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = stake;
                }
            }
            return best;
        }
    }
}
