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
    /// Storage is delegated to a vanilla <see cref="Inventory"/> wired
    /// up by <see cref="LureStakeInventoryInitializer"/>; that gives us
    /// hauler delivery, save/load, and entity-panel inventory fragments
    /// for free.
    /// </summary>
    public class LureStake : BaseComponent,
        IInitializableEntity, IDeletableEntity, IFinishedStateListener
    {
        private const string SoothesopGoodId = "Soothesop";

        private readonly LureStakeRegistry _registry;

        private LureStakeSpec _spec;
        private BlockObject _blockObject;
        private Inventory _inventory;

        // Soothesop demand from the satiation detector arrives as a
        // sub-unit fraction per tick. We accumulate here until a whole
        // unit is owed, then take it from the integer-only inventory.
        private float _drainRemainder;

        public LureStake(LureStakeRegistry registry)
        {
            _registry = registry;
        }

        public LureStakeSpec Spec => _spec;

        // Public because GetComponent<Inventory>() throws "more than one
        // component of type Inventory" on a Lure Stake — vanilla
        // ConstructionSiteInventoryInitializer adds a second Inventory
        // for build-cost goods to every BuildingSpec, so the dedicated
        // initializer must hand us our specific instance. Same trick
        // Stockpile and FireworkLauncher use.
        public Inventory Inventory => _inventory;

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

        // Inventory is created disabled by InventoryInitializer; we flip
        // it on once construction finishes (mirrors Stockpile /
        // FireworkLauncher / BreedingPod). Without this, the inventory
        // never reaches Inventories.EnabledInventories and no hauler
        // ever delivers.
        public void OnEnterFinishedState()
        {
            _inventory?.Enable();
        }

        public void OnExitFinishedState()
        {
            _inventory?.Disable();
        }

        public void AttachInventory(Inventory inventory)
        {
            _inventory = inventory;
        }

        /// <summary>
        /// Drain up to <paramref name="amount"/> Soothesop. Sub-unit
        /// demand accumulates until a whole unit can be removed.
        /// Returns the integer units actually withdrawn this call.
        /// </summary>
        public int DrainSoothesop(float amount)
        {
            if (amount <= 0f || _inventory == null) return 0;
            int available = _inventory.UnreservedAmountInStock(SoothesopGoodId);
            if (available <= 0)
            {
                // Empty stake: drop accumulated debt so a re-stocked
                // stake doesn't silently lose units to old remainder.
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
