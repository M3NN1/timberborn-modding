using Mods.DamDegradation.Components;
using Mods.DamDegradation.Core;
using Mods.DamDegradation.Repair;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.DamDegradation.UI
{
    /// <summary>
    /// Entity panel fragment that shows the dam's current health and a Repair
    /// button. The button text/behaviour adapts to the mod settings:
    /// <list type="bullet">
    /// <item>If <see cref="DamSettings.AllowInstantRepair"/> is true, clicking
    /// repairs instantly (debug mode).</item>
    /// <item>Otherwise the button toggles whether this dam is in the repair
    /// queue picked up by builder beavers via <see cref="DamRepairJobProvider"/>.</item>
    /// </list>
    /// Built with UI Toolkit using external stylesheet (USS) and template (UXML).
    /// </summary>
    public class DamHealthFragment : IEntityPanelFragment
    {
        private const string RepairKey = "DDD.Repair";
        private const string RepairingKey = "DDD.Repair.Pending";
        private const string CancelRepairKey = "DDD.Repair.Cancel";
        private const string ReservedKey = "DDD.Repair.Reserved";

        private readonly DamSettings _settings;
        private readonly DamRepairRegistry _registry;
        private readonly ILoc _loc;
        private readonly VisualElementLoader _visualElementLoader;

        private VisualElement _root;
        private Label _statusLabel;
        private VisualElement _barFill;
        private Button _repairButton;

        private DamDeterioration _current;
        private DamRepairReservation _currentReservation;

        public DamHealthFragment(
            DamSettings settings,
            DamRepairRegistry registry,
            ILoc loc,
            VisualElementLoader visualElementLoader)
        {
            _settings = settings;
            _registry = registry;
            _loc = loc;
            _visualElementLoader = visualElementLoader;
        }

        public VisualElement InitializeFragment()
        {
            _root = _visualElementLoader.LoadVisualElement("DamHealthFragment");
            _root.style.display = DisplayStyle.None;
            _statusLabel = _root.Q<Label>("DamHealthStatus");
            _barFill = _root.Q<VisualElement>("DamHealthBarFill");
            _repairButton = _root.Q<Button>("DamRepairButton");
            _repairButton.clickable.clicked += OnRepairClicked;
            return _root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            _current = entity.GetComponent<DamDeterioration>();
            _currentReservation = entity.GetComponent<DamRepairReservation>();
            if (_current == null)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }
            _root.style.display = DisplayStyle.Flex;
            UpdateFragment();
        }

        public void ClearFragment()
        {
            _current = null;
            _currentReservation = null;
            _root.style.display = DisplayStyle.None;
        }

        public void UpdateFragment()
        {
            if (_current == null)
            {
                return;
            }
            float fraction = _current.HealthFraction;
            _statusLabel.text =
                $"{_current.Health:F0} / {_current.MaxHealth:F0} HP ({fraction * 100f:F0}%)";
            _barFill.style.width = Length.Percent(fraction * 100f);
            UpdateHealthBarClass(_current);
            UpdateRepairButton();
        }

        private void UpdateRepairButton()
        {
            bool needsRepair = _current.NeedsRepair;
            _repairButton.SetEnabled(needsRepair);

            if (!needsRepair)
            {
                _repairButton.text = _loc.T(RepairKey);
                return;
            }

            if (_settings.AllowInstantRepair)
            {
                _repairButton.text = _loc.T(RepairKey);
                return;
            }

            if (_currentReservation != null && _currentReservation.IsReserved)
            {
                _repairButton.text = _loc.T(ReservedKey);
                _repairButton.SetEnabled(false);
                return;
            }

            bool queued = _registry.IsPending(_current);
            _repairButton.text = _loc.T(queued ? CancelRepairKey : RepairingKey);
        }

        private void UpdateHealthBarClass(DamDeterioration deterioration)
        {
            float fraction = deterioration.HealthFraction;
            var spec = deterioration.Spec;

            _barFill.RemoveFromClassList("warning");
            _barFill.RemoveFromClassList("critical");

            if (spec != null)
            {
                if (fraction <= spec.CriticalHealthFraction)
                {
                    _barFill.AddToClassList("critical");
                    return;
                }
                if (fraction <= spec.WarningHealthFraction)
                {
                    _barFill.AddToClassList("warning");
                    return;
                }
            }
        }

        private void OnRepairClicked()
        {
            if (_current == null)
            {
                return;
            }
            if (_settings.AllowInstantRepair)
            {
                _current.Repair();
                UpdateFragment();
                return;
            }
            if (_currentReservation != null && _currentReservation.IsReserved)
            {
                return;
            }
            if (_registry.IsPending(_current))
            {
                _registry.Unregister(_current);
            }
            else
            {
                _registry.Register(_current);
            }
            UpdateFragment();
        }
    }
}
