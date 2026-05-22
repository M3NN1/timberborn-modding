using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.BaseComponentSystem;
using Timberborn.Debugging;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Entity-panel fragment for an active Wyrm. Shows hunger,
    /// contamination, and a status label that mirrors
    /// <see cref="WyrmStatusIndicator"/>'s priority chain
    /// (Sated &gt; Poisoned &gt; Hunting &gt; Stalking; digesting = empty).
    /// In dev mode an extra "Kill wyrm" button appears at the bottom —
    /// the vanilla "Kill selected character" button doesn't fire on
    /// wyrms because they have no <c>Mortal</c> component.
    /// </summary>
    public class WyrmFragment : IEntityPanelFragment
    {
        private const string TitleKey = "Creature.Wyrm.DisplayName";
        private const string SatedKey = "WWW.Wyrm.SatedStatus";
        private const string HuntingKey = "WWW.Wyrm.HuntingStatus";
        private const string StalkingKey = "WWW.Wyrm.StalkingStatus";
        private const string PoisonedKey = "WWW.Wyrm.PoisonedStatus";
        private const string HungerLabelKey = "WWW.Wyrm.HungerLabel";
        private const string ContaminationLabelKey = "WWW.Wyrm.ContaminationLabel";
        private const string KillWyrmKey = "WWW.Wyrm.DevKillButton";

        private static readonly Color HungerBarColor = new Color(0.85f, 0.6f, 0.2f);
        private static readonly Color ContaminationIdleColor = new Color(0.3f, 0.5f, 0.2f);
        private static readonly Color ContaminationActiveColor = new Color(0.5f, 0.7f, 0.2f);
        private static readonly Color DangerButtonColor = new Color32(140, 50, 50, 255);

        private readonly ILoc _loc;
        private readonly DevModeManager _devModeManager;
        private readonly EntityService _entityService;

        private VisualElement _root;
        private Label _statusLabel;
        private Label _hungerLabel;
        private VisualElement _hungerBar;
        private Label _contaminationLabel;
        private VisualElement _contaminationBar;
        private Button _killButton;

        private WyrmComponent _current;
        private WyrmHunter _currentHunter;

        public WyrmFragment(
            ILoc loc,
            DevModeManager devModeManager,
            EntityService entityService)
        {
            _loc = loc;
            _devModeManager = devModeManager;
            _entityService = entityService;
        }

        public VisualElement InitializeFragment()
        {
            _root = BuildRoot();
            _root.style.display = DisplayStyle.None;
            return _root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            _current = entity.GetComponent<WyrmComponent>();
            _currentHunter = entity.GetComponent<WyrmHunter>();
            _root.style.display = _current != null ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateFragment();
        }

        public void ClearFragment()
        {
            _current = null;
            _currentHunter = null;
            _root.style.display = DisplayStyle.None;
        }

        public void UpdateFragment()
        {
            if (_current == null) return;
            _statusLabel.text = ResolveStatusText();

            _hungerLabel.text =
                $"{_loc.T(HungerLabelKey)}: {_current.HungerFraction * 100f:F0}%";
            _hungerBar.style.width =
                Length.Percent(_current.HungerFraction * 100f);

            _contaminationLabel.text =
                $"{_loc.T(ContaminationLabelKey)}: " +
                $"{_current.Contamination:F1} / {_current.LethalContamination:F1}";
            _contaminationBar.style.width =
                Length.Percent(_current.ContaminationFraction * 100f);
            _contaminationBar.style.backgroundColor = _current.IsAbsorbingContamination
                ? ContaminationActiveColor
                : ContaminationIdleColor;

            // Dev-only kill button. Hidden when dev mode is off so it
            // can't be triggered accidentally in a normal playthrough.
            _killButton.style.display = _devModeManager.Enabled
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            WyrmPanelStyle.ApplyPanelStyle(root);

            var title = new Label(_loc.T(TitleKey));
            WyrmPanelStyle.ApplyTitleStyle(title);
            root.Add(title);

            _statusLabel = new Label();
            WyrmPanelStyle.ApplyBodyLabelStyle(_statusLabel);
            _statusLabel.style.marginBottom = 6;
            root.Add(_statusLabel);

            (_hungerLabel, _hungerBar) =
                WyrmPanelStyle.AddLabeledBar(root, HungerBarColor);
            (_contaminationLabel, _contaminationBar) =
                WyrmPanelStyle.AddLabeledBar(root, ContaminationIdleColor);

            _killButton = new Button(KillCurrentWyrm) { text = _loc.T(KillWyrmKey) };
            _killButton.style.backgroundColor = DangerButtonColor;
            _killButton.style.color = Color.white;
            _killButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _killButton.style.marginTop = 6;
            _killButton.style.display = DisplayStyle.None;
            root.Add(_killButton);

            return root;
        }

        private void KillCurrentWyrm()
        {
            if (_current == null || _current.GameObject == null) return;
            Debug.Log(
                $"[WhereWyrmsWait] Dev panel-kill of wyrm at " +
                $"{_current.Transform.position}.");
            _entityService.Delete(_current);
            _current = null;
            _currentHunter = null;
            _root.style.display = DisplayStyle.None;
        }

        // Same priority chain WyrmStatusIndicator uses for its icon row.
        // Below the hunting threshold (digesting), no status text shows.
        private string ResolveStatusText()
        {
            if (_current.IsSated) return _loc.T(SatedKey);
            if (_current.IsAbsorbingContamination) return _loc.T(PoisonedKey);
            if (!_current.IsHunting) return string.Empty;
            bool hasPrey = _currentHunter != null
                && _currentHunter.CurrentTarget != null
                && _currentHunter.CurrentTarget.GameObject != null;
            return hasPrey ? _loc.T(HuntingKey) : _loc.T(StalkingKey);
        }
    }
}
