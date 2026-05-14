using Mods.DamDegradation.Components;
using Mods.DamDegradation.Core;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.Navigation;
using Timberborn.PrioritySystem;
using UnityEngine;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Hands out dam-repair jobs to free builders. Plugged into the vanilla
    /// builder hub via <see cref="IBuilderJobProvider"/>.
    /// <para>
    /// <see cref="BuilderHubWorkplaceBehavior"/> orders providers ascending by
    /// <see cref="ProviderPriority"/> and the first provider to return a job
    /// wins. Vanilla values: <c>BuildingJobProvider=1</c>, <c>DemolishJobProvider=2</c>,
    /// <c>RecoverGoodStackJobProvider=3</c>. We use <c>0</c> so that an active
    /// dam repair preempts any new construction work — a leaking dam is more
    /// urgent than starting another building.
    /// </para>
    /// </summary>
    public class DamRepairJobProvider : IBuilderJobProvider
    {
        private readonly DamRepairRegistry _registry;
        private readonly DamSettings _settings;

        public DamRepairJobProvider(DamRepairRegistry registry, DamSettings settings)
        {
            _registry = registry;
            _settings = settings;
        }

        public int ProviderPriority => 0;

        public (Behavior, Decision) GetJob(Accessible start, BehaviorAgent agent, Priority priority)
        {
            if (!_settings.DegradationEnabled || !_registry.HasAnyPending)
            {
                return (null, Decision.ReleaseNow());
            }

            DamDeterioration bestDam = null;
            DamRepairReservation bestReservation = null;
            float bestSqrDistance = float.PositiveInfinity;
            int totalScanned = 0;
            int skippedReserved = 0;
            int skippedNoApproach = 0;
            int skippedBlacklisted = 0;

            // Pick the dam closest to the builder by direct distance to its
            // engine-computed approach. We don't validate reachability here
            // because dams have no Accessible (we deliberately don't add one
            // to avoid colliding with the construction site's). If a dam ends
            // up unreachable, the WalkToPositionExecutor reports Failure and
            // the behaviour blacklists it for a short cooldown.
            Vector3 builderPos = GetBuilderPosition(start, agent);

            foreach (var dam in _registry.PendingRepairs)
            {
                totalScanned++;
                if (dam == null || dam.GameObject == null)
                {
                    continue;
                }
                if (_registry.IsUnreachable(dam))
                {
                    skippedBlacklisted++;
                    continue;
                }
                var reservation = dam.GetComponent<DamRepairReservation>();
                if (reservation == null || reservation.IsReserved)
                {
                    skippedReserved++;
                    continue;
                }
                // Sample the access closest to the builder so the chosen
                // approach matches the one the behaviour will walk towards.
                if (!DamApproach.TryGetApproachWorldPosition(dam, builderPos, out var approach))
                {
                    skippedNoApproach++;
                    continue;
                }
                float sqrDistance = (approach - builderPos).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestDam = dam;
                    bestReservation = reservation;
                }
            }

            if (bestDam == null)
            {
                if (totalScanned > 0)
                {
                    Debug.Log(
                        $"[DamDegradation] No repairable dam for builder. " +
                        $"scanned={totalScanned} reserved={skippedReserved} " +
                        $"noApproach={skippedNoApproach} blacklisted={skippedBlacklisted}");
                }
                return (null, Decision.ReleaseNow());
            }

            var behavior = agent.GetComponent<DamRepairBehavior>();
            if (behavior == null)
            {
                Debug.LogWarning(
                    $"[DamDegradation] Beaver {agent.Name} has no DamRepairBehavior — " +
                    "decorator likely didn't fire. Check AdultSpec decoration.");
                return (null, Decision.ReleaseNow());
            }

            Debug.Log(
                $"[DamDegradation] Dispatching {agent.Name} to repair dam at " +
                $"{bestDam.Coordinates} (sqrDist={bestSqrDistance:F1}).");
            return (behavior, behavior.StartRepair(bestDam, bestReservation));
        }

        private static Vector3 GetBuilderPosition(Accessible start, BehaviorAgent agent)
        {
            if (agent != null && agent.GameObject != null)
            {
                return agent.Transform.position;
            }
            var single = start.UnblockedSingleAccess;
            return single ?? Vector3.zero;
        }
    }
}
