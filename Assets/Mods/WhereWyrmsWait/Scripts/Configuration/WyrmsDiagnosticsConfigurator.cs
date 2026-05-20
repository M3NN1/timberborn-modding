using System.Linq;
using Bindito.Core;
using Timberborn.BlockSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Configuration
{
    /// <summary>
    /// Diagnostic-only: at scene load, enumerate every PlaceableBlockObjectSpec
    /// the engine sees and log the ones that belong to this mod (TemplateName
    /// starts with Wyrm/LureStake) and the unique tool-group ids encountered.
    /// Active in both Game and MapEditor scenes so we can compare what's
    /// registered between contexts.
    /// </summary>
    [Context("Game")]
    [Context("MapEditor")]
    public class WyrmsDiagnosticsConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<WyrmsDiagnostics>().AsSingleton();
            MultiBind<ILoadableSingleton>().ToExisting<WyrmsDiagnostics>();
        }
    }

    internal class WyrmsDiagnostics : ILoadableSingleton
    {
        private readonly TemplateService _templateService;

        public WyrmsDiagnostics(TemplateService templateService)
        {
            _templateService = templateService;
        }

        public void Load()
        {
            var placeables = _templateService.GetAll<PlaceableBlockObjectSpec>().ToList();
            Debug.Log("[WhereWyrmsWait/diag] scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                      " Total PlaceableBlockObjectSpec count: " + placeables.Count);
            foreach (var spec in placeables)
            {
                var name = spec.GetSpec<TemplateSpec>().TemplateName;
                if (name != null && (name.StartsWith("Wyrm") || name.StartsWith("LureStake")))
                {
                    Debug.Log(
                        "[WhereWyrmsWait/diag] template=" + name +
                        " group=" + spec.ToolGroupId +
                        " devModeTool=" + spec.DevModeTool);
                }
            }
            var distinctGroups = placeables.Select(s => s.ToolGroupId).Distinct().OrderBy(g => g).ToArray();
            Debug.Log("[WhereWyrmsWait/diag] Distinct ToolGroupIds present: " + string.Join(", ", distinctGroups));
        }
    }
}
