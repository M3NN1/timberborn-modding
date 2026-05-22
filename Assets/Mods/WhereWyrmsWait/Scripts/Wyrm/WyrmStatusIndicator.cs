using Timberborn.BaseComponentSystem;
using Timberborn.Localization;
using Timberborn.StatusSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Adds entity-panel status indicators for the wyrm. Four states,
    /// priority sated &gt; poisoned &gt; hunting &gt; stalking; below
    /// the hunting threshold (digesting) all are off. Renders in the
    /// vanilla <c>StatusListFragment</c> row — we use
    /// <see cref="StatusToggle.CreateNormalStatus"/>, not the
    /// <c>…WithFloatingIcon</c> variants, so no icon shows over the
    /// wyrm's head in the world. Reuses vanilla sprite IDs (placeholder
    /// until mod-owned sprites ship; see Placeholders/README.md).
    /// </summary>
    public class WyrmStatusIndicator : TickableComponent, IAwakableComponent
    {
        private const string SatedLocKey = "WWW.Wyrm.SatedStatus";
        private const string HuntingLocKey = "WWW.Wyrm.HuntingStatus";
        private const string StalkingLocKey = "WWW.Wyrm.StalkingStatus";
        private const string PoisonedLocKey = "WWW.Wyrm.PoisonedStatus";

        // Vanilla sprite IDs (leaf-only; StatusSpriteLoader prefixes
        // "Sprites/StatusIcons/"). Picked for visual fit:
        //   LackOfResources              — yellow, "satisfied" feel
        //   GenericError                 — red, urgent (hunting/stalking)
        //   BuildingBlockedByContamination — badwater theme (poisoned)
        private const string SatedSprite = "LackOfResources";
        private const string HuntingSprite = "GenericError";
        private const string StalkingSprite = "GenericError";
        private const string PoisonedSprite = "BuildingBlockedByContamination";

        private const int RecomputeEveryTicks = 12;

        private readonly ILoc _loc;

        private WyrmComponent _wyrm;
        private WyrmHunter _hunter;
        private StatusSubject _statusSubject;
        private StatusToggle _satedToggle;
        private StatusToggle _huntingToggle;
        private StatusToggle _stalkingToggle;
        private StatusToggle _poisonedToggle;

        private int _ticksSinceRecompute = RecomputeEveryTicks;

        public WyrmStatusIndicator(ILoc loc)
        {
            _loc = loc;
        }

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _hunter = GetComponent<WyrmHunter>();
            _statusSubject = GetComponent<StatusSubject>();
        }

        public override void StartTickable()
        {
            if (_statusSubject == null) return;
            _satedToggle = StatusToggle.CreateNormalStatus(
                SatedSprite, _loc.T(SatedLocKey));
            _huntingToggle = StatusToggle.CreateNormalStatus(
                HuntingSprite, _loc.T(HuntingLocKey));
            _stalkingToggle = StatusToggle.CreateNormalStatus(
                StalkingSprite, _loc.T(StalkingLocKey));
            _poisonedToggle = StatusToggle.CreateNormalStatus(
                PoisonedSprite, _loc.T(PoisonedLocKey));
            _statusSubject.RegisterStatus(_satedToggle);
            _statusSubject.RegisterStatus(_huntingToggle);
            _statusSubject.RegisterStatus(_stalkingToggle);
            _statusSubject.RegisterStatus(_poisonedToggle);
        }

        public override void Tick()
        {
            if (_wyrm == null || _statusSubject == null) return;
            if (_ticksSinceRecompute++ < RecomputeEveryTicks) return;
            _ticksSinceRecompute = 0;

            bool sated = _wyrm.IsSated;
            bool poisoned = !sated && _wyrm.IsAbsorbingContamination;
            bool huntingMode = !sated && !poisoned && _wyrm.IsHunting;
            bool hasPrey = _hunter != null
                && _hunter.CurrentTarget != null
                && _hunter.CurrentTarget.GameObject != null;
            bool hunting = huntingMode && hasPrey;
            bool stalking = huntingMode && !hasPrey;

            _satedToggle?.Toggle(sated);
            _poisonedToggle?.Toggle(poisoned);
            _huntingToggle?.Toggle(hunting);
            _stalkingToggle?.Toggle(stalking);
        }
    }
}
