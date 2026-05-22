using Mods.WhereWyrmsWait.Hazards;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Entity-panel fragment shown while a Wyrm Husk is selected. Displays
    /// cover depth, warmup progress, and whether the surface is currently
    /// moist (i.e. the timer is ticking up). Built with UI Toolkit and
    /// styled inline via <see cref="WyrmPanelStyle"/>, so the fragment
    /// stays UXML-free even though the mod ships an AssetBundle for
    /// other resources.
    /// </summary>
    public class WyrmHuskFragment : IEntityPanelFragment
    {
        private const string TitleKey = "Building.WyrmHusk.DisplayName";
        private const string WarmingKey = "WWW.Husk.WarmingStatus";
        private const string DormantKey = "WWW.Husk.DormantStatus";

        private readonly ILoc _loc;

        private VisualElement _root;
        private Label _statusLabel;
        private VisualElement _barFill;

        private WyrmHusk _current;

        public WyrmHuskFragment(ILoc loc)
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
            _current = entity.GetComponent<WyrmHusk>();
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
            string state = _loc.T(_current.SurfaceIsGreen ? WarmingKey : DormantKey);
            _statusLabel.text =
                $"{state} — depth {_current.CoverDepth}, " +
                $"{_current.WarmupDays:F1}/{_current.WarmupTargetDays:F1} days";
            _barFill.style.width = Length.Percent(_current.WarmupFraction * 100f);
            _barFill.style.backgroundColor = _current.SurfaceIsGreen
                ? new Color(0.85f, 0.55f, 0.2f)
                : new Color(0.4f, 0.4f, 0.4f);
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
            root.Add(_statusLabel);

            var barBg = new VisualElement();
            WyrmPanelStyle.ApplyBarTrackStyle(barBg, height: 8);
            _barFill = new VisualElement();
            WyrmPanelStyle.ApplyBarFillStyle(_barFill, Color.gray, height: 8);
            barBg.Add(_barFill);
            root.Add(barBg);

            return root;
        }
    }
}
