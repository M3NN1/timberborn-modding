using Bindito.Core;
using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Hazards;
using Mods.WhereWyrmsWait.Lure;
using Mods.WhereWyrmsWait.UI;
using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.Emptying;
using Timberborn.EntityPanelSystem;
using Timberborn.SelectionSystem;
using Timberborn.StatusSystem;
using Timberborn.TemplateInstantiation;
using Timberborn.Workshops;

namespace Mods.WhereWyrmsWait.Configuration
{
    /// <summary>
    /// Wires the mod's components into Bindito and registers template
    /// decorators. <see cref="WyrmSettings"/> is bound separately by
    /// <see cref="WyrmsSettingsConfigurator"/> so the eMka panel can find
    /// it from both the main menu and the in-game scene.
    /// </summary>
    [Context("Game")]
    public class WyrmsConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<WyrmRegistry>().AsSingleton();
            Bind<WyrmEmergencePicker>().AsSingleton();
            Bind<WyrmFactory>().AsSingleton();
            Bind<WyrmKiller>().AsSingleton();
            Bind<WyrmNotifications>().AsSingleton();
            Bind<LureStakeRegistry>().AsSingleton();
            Bind<LureStakeInventoryInitializer>().AsSingleton();

            Bind<WyrmHuskFragment>().AsSingleton();
            Bind<WyrmDenFragment>().AsSingleton();
            Bind<WyrmFragment>().AsSingleton();
            Bind<LureStakeFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<EntityPanelProvider>().AsSingleton();

            Bind<WyrmHusk>().AsTransient();
            Bind<WyrmDen>().AsTransient();
            Bind<HazardEmergenceVisual>().AsTransient();
            Bind<WyrmComponent>().AsTransient();
            Bind<WyrmContaminationSampler>().AsTransient();
            Bind<WyrmMovement>().AsTransient();
            Bind<WyrmHunter>().AsTransient();
            Bind<WyrmWanderer>().AsTransient();
            Bind<WyrmWallEater>().AsTransient();
            Bind<WyrmSatiationDetector>().AsTransient();
            Bind<WyrmStatusIndicator>().AsTransient();
            Bind<WyrmEmergenceDirtEffect>().AsTransient();
            Bind<LureStake>().AsTransient();
            Bind<LureStakeBaitVisual>().AsTransient();

            MultiBind<TemplateModule>()
                .ToProvider<TemplateModuleProvider>()
                .AsSingleton();
        }
    }

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
            builder.AddDecorator<WyrmHuskSpec, HazardEmergenceVisual>();
            builder.AddDecorator<WyrmDenSpec, HazardEmergenceVisual>();

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
            builder.AddDecorator<WyrmSpec, WyrmEmergenceDirtEffect>();
            // Wyrms have no Character (which would bring SelectableObject
            // along for free), so we add it explicitly to make the wyrm
            // body clickable in the entity panel.
            builder.AddDecorator<WyrmSpec, SelectableObject>();

            // Lure Stake hauler-delivery chain. Models vanilla
            // FireworkLauncher: Inventory + PublicInput + Emptiable +
            // FillInput* gives a passive "haulers fill me up" target.
            // LureStake.OnEnterFinishedState() calls Inventory.Enable()
            // — without that, haulers never see the stake.
            builder.AddDecorator<LureStakeSpec, LureStake>();
            builder.AddDecorator<LureStake, AutoEmptiable>();
            builder.AddDecorator<LureStake, Emptiable>();
            builder.AddDecorator<LureStake, FillInputHaulBehaviorProvider>();
            builder.AddDecorator<LureStake, FillInputWorkplaceBehavior>();
            builder.AddDecorator<LureStake, EmptyInventoriesWorkplaceBehavior>();
            builder.AddDecorator<LureStake, RemoveUnwantedStockWorkplaceBehavior>();
            builder.AddDecorator<LureStake, LureStakeBaitVisual>();
            builder.AddDedicatedDecorator(_lureStakeInventoryInitializer);

            return builder.Build();
        }
    }

    [Context("MainMenu")]
    [Context("Game")]
    public class WyrmsSettingsConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<WyrmSettings>().AsSingleton();
        }
    }

    internal class EntityPanelProvider : IProvider<EntityPanelModule>
    {
        private readonly WyrmHuskFragment _husk;
        private readonly WyrmDenFragment _den;
        private readonly WyrmFragment _wyrm;
        private readonly LureStakeFragment _lure;

        public EntityPanelProvider(
            WyrmHuskFragment husk,
            WyrmDenFragment den,
            WyrmFragment wyrm,
            LureStakeFragment lure)
        {
            _husk = husk;
            _den = den;
            _wyrm = wyrm;
            _lure = lure;
        }

        public EntityPanelModule Get()
        {
            var builder = new EntityPanelModule.Builder();
            builder.AddMiddleFragment(_husk);
            builder.AddMiddleFragment(_den);
            builder.AddMiddleFragment(_wyrm);
            builder.AddMiddleFragment(_lure);
            return builder.Build();
        }
    }
}
