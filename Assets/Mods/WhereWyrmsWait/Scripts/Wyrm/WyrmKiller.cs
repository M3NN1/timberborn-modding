using Timberborn.Debugging;
using Timberborn.EntitySystem;
using Timberborn.InputSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Dev-mode kill for selected wyrms. Mirrors vanilla
    /// <c>CharacterKiller</c>: listens to <c>SelectableObjectSelectedEvent</c>,
    /// remembers the selection, and deletes it on the <c>DeleteObject</c>
    /// keybinding when <see cref="DevModeManager.Enabled"/> is on. Wyrms
    /// don't carry <c>Mortal</c> / <c>Character</c>, so the vanilla
    /// killer skips them — this fills the gap.
    /// </summary>
    public class WyrmKiller : ILoadableSingleton, IInputProcessor
    {
        private const string DeleteObjectKey = "DeleteObject";

        private readonly EventBus _eventBus;
        private readonly InputService _inputService;
        private readonly DevModeManager _devModeManager;
        private readonly EntityService _entityService;

        private WyrmComponent _selectedWyrm;

        public WyrmKiller(
            EventBus eventBus,
            InputService inputService,
            DevModeManager devModeManager,
            EntityService entityService)
        {
            _eventBus = eventBus;
            _inputService = inputService;
            _devModeManager = devModeManager;
            _entityService = entityService;
        }

        public void Load()
        {
            _eventBus.Register(this);
            _inputService.AddInputProcessor(this);
        }

        [OnEvent]
        public void OnSelectableObjectSelected(SelectableObjectSelectedEvent e)
        {
            _selectedWyrm = e.SelectableObject != null
                ? e.SelectableObject.GetComponent<WyrmComponent>()
                : null;
        }

        [OnEvent]
        public void OnSelectableObjectUnselected(SelectableObjectUnselectedEvent e)
        {
            _selectedWyrm = null;
        }

        public bool ProcessInput()
        {
            if (!_devModeManager.Enabled) return false;
            if (_selectedWyrm == null || _selectedWyrm.GameObject == null) return false;
            if (!_inputService.IsKeyDown(DeleteObjectKey)) return false;

            Debug.Log(
                $"[WhereWyrmsWait] Dev-mode kill of wyrm at " +
                $"{_selectedWyrm.Transform.position}.");
            _entityService.Delete(_selectedWyrm);
            _selectedWyrm = null;
            return true;
        }
    }
}
