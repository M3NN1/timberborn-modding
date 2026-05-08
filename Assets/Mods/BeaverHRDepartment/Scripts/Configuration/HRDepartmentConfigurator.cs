using Bindito.Core;
using Mods.BeaverHRDepartment.Assignment;
using Mods.BeaverHRDepartment.UI;
using Timberborn.EntityPanelSystem;

namespace Mods.BeaverHRDepartment.Configuration {

  [Context("Game")]
  public class HRDepartmentConfigurator : Configurator {

    protected override void Configure() {
      Bind<AssignmentService>().AsSingleton();
      Bind<WorkplaceAssignmentFragment>().AsSingleton();
      MultiBind<EntityPanelModule>()
        .ToProvider<AssignmentPanelProvider>().AsSingleton();
    }

    private class AssignmentPanelProvider : IProvider<EntityPanelModule> {
      private readonly WorkplaceAssignmentFragment _fragment;

      public AssignmentPanelProvider(WorkplaceAssignmentFragment fragment) {
        _fragment = fragment;
      }

      public EntityPanelModule Get() {
        var builder = new EntityPanelModule.Builder();
        builder.AddMiddleFragment(_fragment);
        return builder.Build();
      }
    }
  }
}
