using System.Collections.Generic;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Tracks all Lure Stakes in the world. Used by
    /// <c>WyrmSatiationDetector</c> to ask "is there a stocked stake near
    /// this wyrm?" without iterating the entire entity list.
    /// <para>
    /// Stakes register themselves on Awake and unregister on delete.
    /// The set is kept small (players place a handful per zone), so
    /// linear scans are fine.
    /// </para>
    /// </summary>
    public class LureStakeRegistry
    {
        private readonly HashSet<LureStake> _stakes = new HashSet<LureStake>();

        public IReadOnlyCollection<LureStake> Stakes => _stakes;

        public void Register(LureStake stake)
        {
            if (stake != null) _stakes.Add(stake);
        }

        public void Unregister(LureStake stake)
        {
            if (stake != null) _stakes.Remove(stake);
        }
    }
}
