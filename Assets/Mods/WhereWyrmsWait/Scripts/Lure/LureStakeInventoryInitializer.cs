using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.TemplateInstantiation;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Sets up an <see cref="Inventory"/> on each Lure Stake at template
    /// instantiation time. The inventory holds Soothesop only, accepts
    /// hauler deliveries (PublicInput), capacity matches
    /// <see cref="LureStakeSpec.Capacity"/>.
    /// <para>
    /// Mirrors vanilla's <c>StockpileInventoryInitializer</c> / 
    /// <c>GoodConsumingBuildingInventoryInitializer</c>: a dedicated
    /// decorator that constructs the engine's <see cref="InventoryInitializer"/>
    /// against an existing <see cref="Inventory"/> component, so the
    /// stake plays nice with vanilla's hauling, district assignment,
    /// emptying, and entity-panel inventory fragments.
    /// </para>
    /// </summary>
    public class LureStakeInventoryInitializer
        : IDedicatedDecoratorInitializer<LureStake, Inventory>
    {
        // Soothesop ID must match Data/Goods/Good.Soothesop.blueprint.json.
        private const string SoothesopGoodId = "Soothesop";
        private const string InventoryComponentName = "LureStake";

        private readonly InventoryInitializerFactory _inventoryInitializerFactory;

        public LureStakeInventoryInitializer(
            InventoryInitializerFactory inventoryInitializerFactory)
        {
            _inventoryInitializerFactory = inventoryInitializerFactory;
        }

        public void Initialize(LureStake subject, Inventory decorator)
        {
            // Read capacity directly from the spec (via the engine cache)
            // rather than from subject.Capacity: at template-instantiation
            // time the LureStake's InitializeEntity() hasn't run yet, so
            // its cached _spec field is still null and subject.Capacity
            // would fall back to the hard-coded default. Same pattern
            // FireworkLauncherInventoryInitializer uses.
            var spec = subject.GetComponent<LureStakeSpec>();
            int capacity = spec != null ? spec.Capacity : 5;
            var initializer = _inventoryInitializerFactory.Create(
                decorator, capacity, InventoryComponentName);
            initializer.AddAllowedGood(
                new StorableGoodAmount(
                    StorableGood.CreateAsGivable(SoothesopGoodId), capacity));
            initializer.HasPublicInput();
            initializer.Initialize();
            subject.AttachInventory(decorator);
        }
    }
}
