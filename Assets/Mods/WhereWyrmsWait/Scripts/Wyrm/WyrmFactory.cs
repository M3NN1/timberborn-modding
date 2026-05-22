using System.Linq;
using Mods.WhereWyrmsWait.Hazards;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Spawns Wyrm entities at a world position. Looks up the single
    /// blueprint marked with <see cref="WyrmSpec"/> at load time and
    /// caches its <see cref="Blueprint"/> for cheap re-instantiation.
    /// <para>
    /// Mirrors <c>BotFactory</c> from <c>Timberborn.Bots</c> exactly:
    /// </para>
    /// <list type="bullet">
    /// <item>Resolve template via <see cref="TemplateService.GetSingle"/></item>
    /// <item>Cache via <see cref="TemplateInstantiator.CacheInstance"/></item>
    /// <item>Instantiate, position, return</item>
    /// </list>
    /// </summary>
    public class WyrmFactory : ILoadableSingleton
    {
        private readonly TemplateService _templateService;
        private readonly EntityService _entityService;
        private readonly TemplateInstantiator _templateInstantiator;

        private Blueprint _wyrmTemplate;

        public WyrmFactory(
            TemplateService templateService,
            EntityService entityService,
            TemplateInstantiator templateInstantiator)
        {
            _templateService = templateService;
            _entityService = entityService;
            _templateInstantiator = templateInstantiator;
        }

        public void Load()
        {
            // Falls back gracefully if the wyrm template isn't shipped
            // yet (e.g. running with husks but no wyrm blueprint).
            // GetSingle throws when the count is not exactly 1, so we
            // sample the full list ourselves and pick the first hit.
            var matches = _templateService.GetAll<WyrmSpec>().ToList();
            if (matches.Count == 0)
            {
                Debug.LogWarning(
                    "[WhereWyrmsWait] No blueprint with WyrmSpec found. " +
                    "Husks will wake but no wyrm will spawn until a Wyrm " +
                    "template is added under Data/Buildings/.");
                return;
            }
            if (matches.Count > 1)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] {matches.Count} blueprints carry " +
                    "WyrmSpec; using the first one. Multi-variant wyrms are " +
                    "a future feature.");
            }
            _wyrmTemplate = matches[0].Blueprint;
            _templateInstantiator.CacheInstance(_wyrmTemplate);
        }

        /// <summary>
        /// Instantiate a Wyrm at a world position. Returns null if the
        /// template wasn't found at load time.
        /// </summary>
        public WyrmComponent Spawn(Vector3 position)
        {
            return Spawn(position, Quaternion.identity, owner: null);
        }

        public WyrmComponent Spawn(Vector3 position, Quaternion rotation)
        {
            return Spawn(position, rotation, owner: null);
        }

        /// <summary>
        /// Instantiate a Wyrm and stamp it with its owning den so the
        /// den's spawn cap can re-attribute it after save/load. Pass
        /// <c>null</c> for husk-spawned wyrms.
        /// </summary>
        public WyrmComponent Spawn(Vector3 position, Quaternion rotation, WyrmDen owner)
        {
            if (_wyrmTemplate == null)
            {
                return null;
            }
            var entity = _entityService.Instantiate(_wyrmTemplate);
            entity.Transform.SetPositionAndRotation(position, rotation);
            var wyrm = entity.GetComponent<WyrmComponent>();
            if (wyrm != null && owner != null)
            {
                wyrm.SetOwningDen(owner);
            }
            return wyrm;
        }
    }
}
