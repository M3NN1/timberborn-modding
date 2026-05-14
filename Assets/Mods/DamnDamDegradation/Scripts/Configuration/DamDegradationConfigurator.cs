using Bindito.Core;
using Mods.DamDegradation.Components;
using Mods.DamDegradation.Core;
using Mods.DamDegradation.Repair;
using Mods.DamDegradation.UI;
using Timberborn.Beavers;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.TemplateInstantiation;

namespace Mods.DamDegradation.Configuration
{
    /// <summary>
    /// Wires up the mod's components into the game.
    /// <list type="bullet">
    /// <item>Decorates every blueprint that has a <see cref="DamDeteriorationSpec"/>
    /// with a <see cref="DamDeterioration"/> + <see cref="DamRepairReservation"/>.</item>
    /// <item>Decorates every adult beaver with the repair Behavior + Executor.</item>
    /// <item>Registers the entity panel fragment + builder job provider.</item>
    /// </list>
    /// </summary>
    [Context("Game")]
    public class DamDegradationConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<DamSettings>().AsSingleton();
            Bind<DamRepairRegistry>().AsSingleton();

            Bind<DamHealthFragment>().AsSingleton();

            // Decorator components are constructed by Bindito per-entity, so they
            // need their own bindings even though they're added by the
            // TemplateModule. AsTransient => one fresh instance per dam/beaver.
            Bind<DamDeterioration>().AsTransient();
            Bind<DamDamageVisuals>().AsTransient();
            Bind<DamRepairReservation>().AsTransient();
            Bind<DamRepairBehavior>().AsTransient();
            Bind<DamRepairExecutor>().AsTransient();

            MultiBind<EntityPanelModule>()
                .ToProvider<DamHealthPanelProvider>()
                .AsSingleton();

            MultiBind<TemplateModule>()
                .ToProvider(ProvideTemplateModule)
                .AsSingleton();

            MultiBind<IBuilderJobProvider>()
                .To<DamRepairJobProvider>()
                .AsSingleton();
        }

        private static TemplateModule ProvideTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            // Dam-side decoration: any blueprint carrying the spec gets the
            // deterioration component plus a reservation slot.
            builder.AddDecorator<DamDeteriorationSpec, DamDeterioration>();
            builder.AddDecorator<DamDeteriorationSpec, DamDamageVisuals>();
            builder.AddDecorator<DamDeteriorationSpec, DamRepairReservation>();
            // Beaver-side decoration: every adult gets the repair behavior +
            // executor so the builder hub can hand them dam-repair jobs.
            builder.AddDecorator<AdultSpec, DamRepairBehavior>();
            builder.AddDecorator<AdultSpec, DamRepairExecutor>();
            return builder.Build();
        }

        private class DamHealthPanelProvider : IProvider<EntityPanelModule>
        {
            private readonly DamHealthFragment _fragment;

            public DamHealthPanelProvider(DamHealthFragment fragment)
            {
                _fragment = fragment;
            }

            public EntityPanelModule Get()
            {
                var builder = new EntityPanelModule.Builder();
                builder.AddMiddleFragment(_fragment);
                return builder.Build();
            }
        }
    }
}
