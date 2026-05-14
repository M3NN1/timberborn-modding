using Timberborn.BaseComponentSystem;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Tracks whether a single builder has already claimed this dam for repair,
    /// so two beavers don't end up working on the same block at the same time.
    /// Lives on the dam entity itself (decorated like the rest of the dam stack).
    /// <para>
    /// State is intentionally <em>not</em> persisted on the dam side — the
    /// authoritative record of who reserved what lives on the beaver in
    /// <see cref="DamRepairBehavior"/>, which serializes a reference to the
    /// dam and re-acquires the reservation in <c>PostInitializeEntity</c>.
    /// On load every dam therefore starts unreserved; the first behavior to
    /// re-attach claims it back.
    /// </para>
    /// </summary>
    public class DamRepairReservation : BaseComponent
    {
        public bool IsReserved { get; private set; }

        public BaseComponent Reservist { get; private set; }

        public bool TryReserve(BaseComponent reservist)
        {
            if (IsReserved)
            {
                return false;
            }
            IsReserved = true;
            Reservist = reservist;
            return true;
        }

        public void Release()
        {
            IsReserved = false;
            Reservist = null;
        }
    }
}
