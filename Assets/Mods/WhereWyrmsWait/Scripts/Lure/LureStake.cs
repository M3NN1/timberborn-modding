using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Player-placed stake that holds a small amount of Soothesop. Wyrms
    /// in range read this stock through <c>WyrmSatiationDetector</c> and
    /// flip into the Sated state, which suppresses wall chewing.
    /// <para>
    /// Storage is delegated to a vanilla <see cref="Inventory"/> wired up
    /// by <see cref="LureStakeInventoryInitializer"/>. That gives us
    /// hauler delivery (PublicInput), automatic save/load, and
    /// integration with the entity-panel inventory fragments — without
    /// us reimplementing any of it.
    /// </para>
    /// <para>
    /// The wyrm-side <c>WyrmSatiationDetector</c> drains via
    /// <see cref="DrainSoothesop"/> rather than touching the Inventory
    /// directly, so this component keeps the rounding/clamping logic in
    /// one place (Inventory only deals in whole-good units).
    /// </para>
    /// </summary>
    public class LureStake : BaseComponent,
        IInitializableEntity, IDeletableEntity
    {
        private const string SoothesopGoodId = "Soothesop";

        private readonly LureStakeRegistry _registry;

        private LureStakeSpec _spec;
        private BlockObject _blockObject;
        private Inventory _inventory;

        // Fractional remainder for sub-unit drain rates: each tick the
        // satiation detector calls DrainSoothesop with a small fraction of
        // a unit. We accumulate until a whole unit is owed, then take it
        // from the integer-only inventory.
        private float _drainRemainder;

        public LureStake(LureStakeRegistry registry)
        {
            _registry = registry;
        }

        public LureStakeSpec Spec => _spec;

        /// <summary>Current Soothesop stock. Reads from the engine inventory.</summary>
        public float Stock => _inventory != null
            ? _inventory.UnreservedAmountInStock(SoothesopGoodId)
            : 0f;

        public bool HasSoothesop => Stock > 0f;

        public Vector3Int Coordinates =>
            _blockObject != null ? _blockObject.Coordinates : Vector3Int.zero;

        public int Capacity => _spec?.Capacity ?? 5;

        public int SatiateRadius => _spec?.SatiateRadius ?? 3;

        public void InitializeEntity()
        {
            _spec = GetComponent<LureStakeSpec>();
            _blockObject = GetComponent<BlockObject>();
            _registry.Register(this);
        }

        public void DeleteEntity()
        {
            _registry.Unregister(this);
        }

        /// <summary>
        /// Wired in by <see cref="LureStakeInventoryInitializer"/> so this
        /// component knows where its storage is. Could also be discovered
        /// by <c>GetComponent&lt;Inventory&gt;()</c>, but explicit
        /// passing matches the vanilla pattern (Stockpile, Manufactory).
        /// </summary>
        public void AttachInventory(Inventory inventory)
        {
            _inventory = inventory;
        }

        /// <summary>
        /// Drain up to <paramref name="amount"/> Soothesop. Sub-unit demands
        /// accumulate across calls in a fractional remainder until a whole
        /// unit can be removed from the integer-based engine inventory.
        /// Returns the integer number of whole units actually withdrawn
        /// during this call (0 in most ticks; 1 every few ticks at the
        /// default consumption rate).
        /// </summary>
        public int DrainSoothesop(float amount)
        {
            if (amount <= 0f || _inventory == null) return 0;
            int available = _inventory.UnreservedAmountInStock(SoothesopGoodId);
            if (available <= 0)
            {
                // Empty stake: throw away accumulated fractional debt so a
                // newly-restocked stake doesn't silently lose units to a
                // remainder built up while it was empty.
                _drainRemainder = 0f;
                return 0;
            }

            _drainRemainder += amount;
            int wholeUnits = Mathf.FloorToInt(_drainRemainder);
            if (wholeUnits <= 0) return 0;

            int toTake = Mathf.Min(wholeUnits, available);
            _inventory.Take(new GoodAmount(SoothesopGoodId, toTake));
            _drainRemainder -= toTake;
            return toTake;
        }
    }
}
