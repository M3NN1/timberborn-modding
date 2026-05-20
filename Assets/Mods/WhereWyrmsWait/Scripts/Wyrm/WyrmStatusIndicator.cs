using Timberborn.BaseComponentSystem;
using Timberborn.Localization;
using Timberborn.StatusSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Adds floating-icon status indicators above the wyrm so players can
    /// read its mood from camera height without clicking. Four states:
    /// <list type="bullet">
    /// <item>Sated (Soothesop in range)</item>
    /// <item>Poisoned (standing on contaminated water)</item>
    /// <item>Hunting (hunger above threshold and an actual beaver target)</item>
    /// <item>Stalking (hungry, threshold passed, but no beaver to chase)</item>
    /// </list>
    /// <para>
    /// Below the hunting threshold (digesting), no icon shows — that's
    /// the "neutral" state where the wyrm wanders harmlessly.
    /// </para>
    /// <para>
    /// Reuses vanilla sprite IDs to avoid an AssetBundle dependency, the
    /// same trick DDD's dam status uses. Priority: sated > poisoned >
    /// hunting > stalking — only one icon at a time.
    /// </para>
    /// </summary>
    public class WyrmStatusIndicator : TickableComponent, IAwakableComponent
    {
        // Localization keys for the floating-icon hover text.
        private const string SatedLocKey = "WWW.Wyrm.SatedStatus";
        private const string HuntingLocKey = "WWW.Wyrm.HuntingStatus";
        private const string StalkingLocKey = "WWW.Wyrm.StalkingStatus";
        private const string PoisonedLocKey = "WWW.Wyrm.PoisonedStatus";

        // Reused vanilla sprites (verified against TimberbornRef status sprite
        // catalog). Names are leaf-only; StatusSpriteLoader prefixes
        // "Sprites/StatusIcons/".
        // - LackOfResources: yellow icon, used by workshops out-of-resources
        //   for the "happily fed" feel of a sated wyrm.
        // - GenericError: red icon for the hunting state — vanilla
        //   uses it for high-priority alerts; reads as "danger here".
        // - GenericError reused with different loc text for "stalking" —
        //   "I want prey but found none" — same red urgency without an
        //   extra sprite slot.
        // - BuildingBlockedByContamination: badwater-themed icon used
        //   elsewhere for buildings that can't operate due to contamination.
        //   Best fit for a poisoned-by-badwater wyrm.
        private const string SatedSprite = "LackOfResources";
        private const string HuntingSprite = "GenericError";
        private const string StalkingSprite = "GenericError";
        private const string PoisonedSprite = "BuildingBlockedByContamination";

        // We don't need to check every tick — moods change at human pace.
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
            _satedToggle = StatusToggle.CreateNormalStatusWithFloatingIcon(
                SatedSprite, _loc.T(SatedLocKey));
            _huntingToggle = StatusToggle.CreateNormalStatusWithFloatingIcon(
                HuntingSprite, _loc.T(HuntingLocKey));
            _stalkingToggle = StatusToggle.CreateNormalStatusWithFloatingIcon(
                StalkingSprite, _loc.T(StalkingLocKey));
            _poisonedToggle = StatusToggle.CreateNormalStatusWithFloatingIcon(
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

            // Priority: sated > poisoned > hunting > stalking.
            // Below the hunting threshold (digesting), all four are off.
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
