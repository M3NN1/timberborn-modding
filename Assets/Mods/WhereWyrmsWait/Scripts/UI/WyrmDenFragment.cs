using Mods.WhereWyrmsWait.Hazards;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Entity-panel fragment shown while a Wyrm Den is selected.
    /// Mirrors <see cref="WyrmHuskFragment"/>'s warmup/cover readout
    /// and adds a second progress bar for the spawn cooldown plus a
    /// live spawn count. Same UI Toolkit + inline-style approach so
    /// the mod stays UXML-free.
    /// </summary>
    public class WyrmDenFragment : IEntityPanelFragment
    {
        private const string TitleKey = "Building.WyrmDen.DisplayName";
        private const string WarmingKey = "WWW.Husk.WarmingStatus";
        private const string DormantKey = "WWW.Husk.DormantStatus";
        private const string ActiveKey = "WWW.Den.ActiveStatus";
        private const string CooldownLabelKey = "WWW.Den.CooldownLabel";
        private const string LiveCountLabelKey = "WWW.Den.LiveCountLabel";

        private static readonly Color WarmupBarColor =
            new Color(0.85f, 0.55f, 0.20f);
        private static readonly Color CooldownBarColor =
            new Color(0.55f, 0.75f, 0.45f);
        private static readonly Color InertBarColor =
            new Color(0.40f, 0.40f, 0.40f);

        private readonly ILoc _loc;

        private VisualElement _root;
        private Label _statusLabel;
        private VisualElement _warmupFill;
        private Label _cooldownLabel;
        private VisualElement _cooldownFill;
        private Label _liveCountLabel;

        private WyrmDen _current;

        public WyrmDenFragment(ILoc loc)
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
            _current = entity.GetComponent<WyrmDen>();
            _root.style.display =
                _current != null ? DisplayStyle.Flex : DisplayStyle.None;
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

            string statusKey = ResolveStatusKey(_current);
            _statusLabel.text =
                $"{_loc.T(statusKey)} — depth {_current.CoverDepth}, " +
                $"{_current.WarmupDays:F1}/{_current.WarmupTargetDays:F1} days";
            _warmupFill.style.width =
                Length.Percent(_current.WarmupFraction * 100f);
            _warmupFill.style.backgroundColor = _current.SurfaceIsGreen
                ? WarmupBarColor
                : InertBarColor;

            float cooldownTotal = _current.Spec != null
                ? _current.Spec.DaysBetweenSpawns
                : 0f;
            float cooldownRemaining = Mathf.Max(0f, _current.SpawnCooldownDays);
            float cooldownFraction = cooldownTotal > 0f
                ? Mathf.Clamp01(1f - cooldownRemaining / cooldownTotal)
                : (_current.WarmedUp ? 1f : 0f);

            _cooldownLabel.text = string.Format(
                "{0}: {1:F1}/{2:F1} days",
                _loc.T(CooldownLabelKey),
                Mathf.Max(0f, cooldownTotal - cooldownRemaining),
                cooldownTotal);
            _cooldownFill.style.width = Length.Percent(cooldownFraction * 100f);
            _cooldownFill.style.backgroundColor = _current.WarmedUp
                ? CooldownBarColor
                : InertBarColor;

            int max = _current.Spec != null ? _current.Spec.MaxLiveWyrms : 0;
            _liveCountLabel.text = string.Format(
                "{0}: {1}/{2}",
                _loc.T(LiveCountLabelKey),
                _current.LiveSpawnCount,
                max);
        }

        private static string ResolveStatusKey(WyrmDen den)
        {
            if (!den.SurfaceIsGreen) return DormantKey;
            return den.WarmedUp ? ActiveKey : WarmingKey;
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

            // Warmup bar (gates first wake; thereafter stays full).
            var warmupTrack = new VisualElement();
            WyrmPanelStyle.ApplyBarTrackStyle(warmupTrack, height: 8);
            _warmupFill = new VisualElement();
            WyrmPanelStyle.ApplyBarFillStyle(_warmupFill, InertBarColor, height: 8);
            warmupTrack.Add(_warmupFill);
            root.Add(warmupTrack);

            // Cooldown bar (drives subsequent spawns).
            _cooldownLabel = new Label();
            WyrmPanelStyle.ApplyBodyLabelStyle(_cooldownLabel);
            _cooldownLabel.style.marginTop = 2;
            root.Add(_cooldownLabel);

            var cooldownTrack = new VisualElement();
            WyrmPanelStyle.ApplyBarTrackStyle(cooldownTrack, height: 6);
            _cooldownFill = new VisualElement();
            WyrmPanelStyle.ApplyBarFillStyle(_cooldownFill, InertBarColor, height: 6);
            cooldownTrack.Add(_cooldownFill);
            root.Add(cooldownTrack);

            _liveCountLabel = new Label();
            WyrmPanelStyle.ApplyBodyLabelStyle(_liveCountLabel);
            root.Add(_liveCountLabel);

            return root;
        }
    }
}
