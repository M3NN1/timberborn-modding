using System.Collections.Generic;
using Mods.DamDegradation.Components;
using UnityEngine;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Tracks every dam currently below its warning threshold and not already
    /// reserved by a builder. Queried by <see cref="DamRepairJobProvider"/> to
    /// hand out repair jobs to free builder beavers.
    /// Intentionally a plain non-singleton container: its lifecycle is per-game
    /// because <see cref="DamDeterioration"/> components register on Awake and
    /// unregister on entity deletion.
    /// </summary>
    public class DamRepairRegistry
    {
        // How long (in real seconds) a dam stays in the unreachable blacklist
        // after a walk fails. Long enough that the builder hub doesn't keep
        // trying the same broken dam every tick, short enough that a player
        // who places a stair has the dam serviced again within the minute.
        private const float UnreachableCooldownSeconds = 30f;

        private readonly HashSet<DamDeterioration> _pendingRepairs = new HashSet<DamDeterioration>();
        private readonly Dictionary<DamDeterioration, float> _unreachableUntil =
            new Dictionary<DamDeterioration, float>();

        public IReadOnlyCollection<DamDeterioration> PendingRepairs => _pendingRepairs;

        public bool HasAnyPending => _pendingRepairs.Count > 0;

        public bool IsPending(DamDeterioration dam) =>
            dam != null && _pendingRepairs.Contains(dam);

        public void Register(DamDeterioration dam)
        {
            if (dam != null)
            {
                _pendingRepairs.Add(dam);
            }
        }

        public void Unregister(DamDeterioration dam)
        {
            if (dam != null)
            {
                _pendingRepairs.Remove(dam);
                _unreachableUntil.Remove(dam);
            }
        }

        /// <summary>
        /// Marks the dam as "tried but unreachable" so the job provider skips
        /// it for a short cooldown, letting other reachable dams be served
        /// instead. Auto-expires after <c>UnreachableCooldownSeconds</c>.
        /// </summary>
        public void MarkUnreachable(DamDeterioration dam)
        {
            if (dam == null)
            {
                return;
            }
            _unreachableUntil[dam] = Time.time + UnreachableCooldownSeconds;
        }

        /// <summary>True if the dam was recently flagged unreachable and the cooldown is still active.</summary>
        public bool IsUnreachable(DamDeterioration dam)
        {
            if (dam == null)
            {
                return false;
            }
            if (!_unreachableUntil.TryGetValue(dam, out var expiresAt))
            {
                return false;
            }
            if (Time.time >= expiresAt)
            {
                _unreachableUntil.Remove(dam);
                return false;
            }
            return true;
        }
    }
}
