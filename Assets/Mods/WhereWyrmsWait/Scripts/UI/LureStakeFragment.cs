using Mods.WhereWyrmsWait.Lure;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Entity-panel fragment for a Lure Stake. Shows current Soothesop
    /// stock and exposes a Restock debug button (sets stock to capacity).
    /// In v0.4 the button will be replaced with hauler-delivery integration.
    /// </summary>
    public class LureStakeFragment : IEntityPanelFragment
    {
        private const string TitleKey = "Building.LureStake.DisplayName";

        private readonly ILoc _loc;

        private VisualElement _root;
        private Label _stockLabel;
        private VisualElement _stockBar;

        private LureStake _current;

        public LureStakeFragment(ILoc loc)
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
            _current = entity.GetComponent<LureStake>();
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
            int capacity = _current.Capacity;
            _stockLabel.text = $"Soothesop: {_current.Stock:F1} / {capacity}";
            _stockBar.style.width = capacity > 0
                ? Length.Percent((_current.Stock / capacity) * 100f)
                : Length.Percent(0f);
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

            _stockLabel = new Label();
            _stockLabel.style.marginBottom = 4;
            root.Add(_stockLabel);

            var bg = new VisualElement();
            bg.style.height = 8;
            bg.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            bg.style.marginBottom = 6;
            _stockBar = new VisualElement();
            _stockBar.style.height = 8;
            _stockBar.style.backgroundColor = new Color(0.4f, 0.6f, 0.3f);
            bg.Add(_stockBar);
            root.Add(bg);

            return root;
        }
    }
}
