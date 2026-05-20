using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Entity-panel fragment for an active Wyrm. Shows hunger,
    /// contamination, and the Sated/Hunting status flag. All UI Toolkit,
    /// no asset dependencies.
    /// <para>
    /// Contamination is the wyrm's only damage pool — no separate HP
    /// bar. The bar tints brighter while the wyrm is actively absorbing
    /// badwater so the player can see the kill in progress.
    /// </para>
    /// </summary>
    public class WyrmFragment : IEntityPanelFragment
    {
        private const string TitleKey = "Creature.Wyrm.DisplayName";
        private const string SatedKey = "WWW.Wyrm.SatedStatus";
        private const string HuntingKey = "WWW.Wyrm.HuntingStatus";
        private const string HungerLabelKey = "WWW.Wyrm.HungerLabel";
        private const string ContaminationLabelKey = "WWW.Wyrm.ContaminationLabel";

        private static readonly Color HungerBarColor = new Color(0.85f, 0.6f, 0.2f);
        private static readonly Color ContaminationIdleColor = new Color(0.3f, 0.5f, 0.2f);
        private static readonly Color ContaminationActiveColor = new Color(0.5f, 0.7f, 0.2f);

        private readonly ILoc _loc;

        private VisualElement _root;
        private Label _statusLabel;
        private Label _hungerLabel;
        private VisualElement _hungerBar;
        private Label _contaminationLabel;
        private VisualElement _contaminationBar;

        private WyrmComponent _current;

        public WyrmFragment(ILoc loc)
        {
            _loc = loc;
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
            _root.style.display = _current != null ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateFragment();
        }

        public void ClearFragment()
        {
            _current = null;
            _root.style.display = DisplayStyle.None;
        }

        public void UpdateFragment()
        {
            if (_current == null) return;
            _statusLabel.text = _current.IsSated ? _loc.T(SatedKey) : _loc.T(HuntingKey);

            _hungerLabel.text =
                $"{_loc.T(HungerLabelKey)}: {_current.Hunger * 100f:F0}%";
            _hungerBar.style.width = Length.Percent(Mathf.Clamp01(_current.Hunger) * 100f);

            _contaminationLabel.text =
                $"{_loc.T(ContaminationLabelKey)}: " +
                $"{_current.Contamination:F1} / {_current.LethalContamination:F1}";
            _contaminationBar.style.width =
                Length.Percent(_current.ContaminationFraction * 100f);
            _contaminationBar.style.backgroundColor = _current.IsAbsorbingContamination
                ? ContaminationActiveColor
                : ContaminationIdleColor;
        }

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            root.style.paddingTop = 6;
            root.style.paddingBottom = 6;

            var title = new Label(_loc.T(TitleKey));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            root.Add(title);

            _statusLabel = new Label();
            _statusLabel.style.marginBottom = 6;
            root.Add(_statusLabel);

            (_hungerLabel, _hungerBar) = AddBar(root, HungerBarColor);
            (_contaminationLabel, _contaminationBar) = AddBar(root, ContaminationIdleColor);

            return root;
        }

        private static (Label, VisualElement) AddBar(VisualElement parent, Color color)
        {
            var label = new Label();
            label.style.marginBottom = 2;
            parent.Add(label);

            var bg = new VisualElement();
            bg.style.height = 6;
            bg.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            bg.style.marginBottom = 4;
            var fill = new VisualElement();
            fill.style.height = 6;
            fill.style.backgroundColor = color;
            bg.Add(fill);
            parent.Add(bg);

            return (label, fill);
        }
    }
}
