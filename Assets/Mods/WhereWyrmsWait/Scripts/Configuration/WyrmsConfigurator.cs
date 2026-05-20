using Bindito.Core;
using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Hazards;
using Mods.WhereWyrmsWait.Lure;
using Mods.WhereWyrmsWait.UI;
using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.EntityPanelSystem;
using Timberborn.StatusSystem;
using Timberborn.TemplateInstantiation;

namespace Mods.WhereWyrmsWait.Configuration
{
    /// <summary>
    /// Wires the mod's components into Bindito and registers template
    /// decorators so that any blueprint carrying a <see cref="WyrmHuskSpec"/>
    /// also gets a <see cref="WyrmHusk"/> ticker at runtime.
    /// <para>
    /// <see cref="WyrmSettings"/> is bound separately by
    /// <see cref="WyrmsSettingsConfigurator"/> so the eMka panel can find it
    /// from both the main menu and the in-game scene, mirroring DDD's
    /// <c>DamDegradationSettingsConfigurator</c>.
    /// </para>
    /// </summary>
    [Context("Game")]
    public class WyrmsConfigurator : Configurator
    {
        protected override void Configure()
        {
            // Game-wide singletons.
            Bind<WyrmRegistry>().AsSingleton();
            Bind<WyrmEmergencePicker>().AsSingleton();
            Bind<WyrmFactory>().AsSingleton();
            Bind<WyrmNotifications>().AsSingleton();
            Bind<LureStakeRegistry>().AsSingleton();
            Bind<LureStakeInventoryInitializer>().AsSingleton();

            // Entity panel fragments.
            Bind<WyrmHuskFragment>().AsSingleton();
            Bind<WyrmFragment>().AsSingleton();
            Bind<LureStakeFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<EntityPanelProvider>().AsSingleton();

            // Per-entity transient bindings.
            Bind<WyrmHusk>().AsTransient();
            Bind<WyrmDen>().AsTransient();
            Bind<WyrmComponent>().AsTransient();
            Bind<WyrmContaminationSampler>().AsTransient();
            Bind<WyrmMovement>().AsTransient();
            Bind<WyrmHunter>().AsTransient();
            Bind<WyrmWanderer>().AsTransient();
            Bind<WyrmWallEater>().AsTransient();
            Bind<WyrmSatiationDetector>().AsTransient();
            Bind<WyrmStatusIndicator>().AsTransient();
            Bind<LureStake>().AsTransient();

            MultiBind<TemplateModule>()
                .ToProvider<TemplateModuleProvider>()
                .AsSingleton();
        }
    }

    /// <summary>
    /// Builds the template module with all our decorators, including the
    /// dedicated Inventory initializer for Lure Stakes.
    /// </summary>
    internal class TemplateModuleProvider : IProvider<TemplateModule>
    {
        private readonly LureStakeInventoryInitializer _lureStakeInventoryInitializer;

        public TemplateModuleProvider(
            LureStakeInventoryInitializer lureStakeInventoryInitializer)
        {
            _lureStakeInventoryInitializer = lureStakeInventoryInitializer;
        }

        public TemplateModule Get()
        {
            var builder = new TemplateModule.Builder();

            // Hazards.
            builder.AddDecorator<WyrmHuskSpec, WyrmHusk>();
            builder.AddDecorator<WyrmDenSpec, WyrmDen>();

            // Wyrm creature stack.
            builder.AddDecorator<WyrmSpec, WyrmComponent>();
            builder.AddDecorator<WyrmSpec, WyrmContaminationSampler>();
            builder.AddDecorator<WyrmSpec, WyrmMovement>();
            builder.AddDecorator<WyrmSpec, WyrmHunter>();
            builder.AddDecorator<WyrmSpec, WyrmWanderer>();
            builder.AddDecorator<WyrmSpec, WyrmWallEater>();
            builder.AddDecorator<WyrmSpec, WyrmSatiationDetector>();
            builder.AddDecorator<WyrmSpec, StatusSubject>();
            builder.AddDecorator<WyrmSpec, WyrmStatusIndicator>();
            // Make wyrms clickable in the entity panel. Vanilla creatures
            // get this via Character → SelectableObject; wyrms have no
            // Character, so we add it directly. Selection raycast walks
            // colliders → GetComponentInParent<SelectableObject>, so the
            // capsule collider on the wyrm body is enough to be hit.
            builder.AddDecorator<WyrmSpec, Timberborn.SelectionSystem.SelectableObject>();

            // Lure Stake stack — kept deliberately minimal.
            //
            // Why no FillInputHaulBehaviorProvider / FillInputWorkplaceBehavior:
            // those are for *workplaces* that pull goods to themselves
            // through assigned workers (Manufactory, GoodConsumingBuilding).
            // The Lure Stake is a passive destination inventory with
            // PublicInput set; haulers find it the same way they find a
            // Stockpile — via DistrictInventoryRegistry/DistrictInventoryPicker
            // walking inventories that accept the good. The Forager's
            // Manufactory output side pushes Soothesop out of its own
            // inventory; the district picker matches a Lure Stake's input
            // capacity to that output, and a hauler is dispatched.
            //
            // Decorator chain that auto-builds the rest:
            //   BuildingSpec → BlockableObject + DistrictBuilding
            //   BuildingAccessibleSpec → BuildingAccessible → DistrictBuilding
            //                          → DistrictInventoryAssigner
            //   Inventory (our decorator) → Inventories (auto)
            // So the only mod-owned wiring needed is the LureStake component
            // itself plus the dedicated InventoryInitializer.
            builder.AddDecorator<LureStakeSpec, LureStake>();
            builder.AddDedicatedDecorator(_lureStakeInventoryInitializer);

            return builder.Build();
        }
    }

    /// <summary>
    /// Registers <see cref="WyrmSettings"/> in both the main menu and game
    /// scene so the eMka panel can render the cog button on this mod's card.
    /// </summary>
    [Context("MainMenu")]
    [Context("Game")]
    public class WyrmsSettingsConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<WyrmSettings>().AsSingleton();
        }
    }

    /// <summary>
    /// Builds the EntityPanelModule that exposes our middle-fragment UIs
    /// (husk warmup, wyrm health, lure stake stock) to the in-game entity
    /// panel. Mirrors DDD's <c>DamHealthPanelProvider</c>.
    /// </summary>
    internal class EntityPanelProvider : IProvider<EntityPanelModule>
    {
        private readonly WyrmHuskFragment _husk;
        private readonly WyrmFragment _wyrm;
        private readonly LureStakeFragment _lure;

        public EntityPanelProvider(
            WyrmHuskFragment husk,
            WyrmFragment wyrm,
            LureStakeFragment lure)
        {
            _husk = husk;
            _wyrm = wyrm;
            _lure = lure;
        }

        public EntityPanelModule Get()
        {
            var builder = new EntityPanelModule.Builder();
            builder.AddMiddleFragment(_husk);
            builder.AddMiddleFragment(_wyrm);
            builder.AddMiddleFragment(_lure);
            return builder.Build();
        }
    }
}
