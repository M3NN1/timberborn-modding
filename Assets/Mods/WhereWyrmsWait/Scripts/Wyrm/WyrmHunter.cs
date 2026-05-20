using Mods.WhereWyrmsWait.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.Beavers;
using Timberborn.Characters;
using Timberborn.Localization;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Wyrm targeting AI: each tick, picks the nearest live beaver as the
    /// hunt target and feeds the position to <see cref="WyrmMovement"/>.
    /// On contact (target within <see cref="ContactDistance"/>), eats the
    /// beaver: kills the character, posts a notification, and resets the
    /// wyrm's hunger via <see cref="WyrmComponent.Sate"/>.
    /// <para>
    /// Sandbox mode: the hunter still tracks but doesn't eat. Useful for
    /// map authors testing whether wyrms reach the layout they expect.
    /// </para>
    /// <para>
    /// Movement constraints (only reachable beavers, +1 step rule, etc.)
    /// fall out of the navigation service used by <see cref="WyrmMovement"/>;
    /// we don't filter the candidate list here. If the closest beaver is
    /// unreachable, the path planner returns no path and the wyrm just
    /// stands still until the world changes — same behaviour DDD's repair
    /// system uses for unreachable dams.
    /// </para>
    /// </summary>
    public class WyrmHunter : TickableComponent, IAwakableComponent
    {
        // The wyrm "catches" a target when within this world-space distance.
        // Slightly larger than 0.5 so the hit triggers as the wyrm walks
        // into the beaver's tile without requiring sub-pixel accuracy.
        private const float ContactDistance = 0.6f;

        // Don't retarget every tick — feels jittery and burns CPU. Stick
        // with the current target unless it dies, becomes unreachable, or
        // a much closer one appears (handled implicitly by replan delay).
        private const int RetargetEveryTicks = 8;

        private readonly CharacterPopulation _characterPopulation;
        private readonly WyrmSettings _settings;
        private readonly WyrmNotifications _notifications;
        private readonly ILoc _loc;

        private WyrmComponent _wyrm;
        private WyrmMovement _movement;
        private Character _currentTarget;
        private int _ticksSinceRetarget = RetargetEveryTicks;

        public WyrmHunter(
            CharacterPopulation characterPopulation,
            WyrmSettings settings,
            WyrmNotifications notifications,
            ILoc loc)
        {
            _characterPopulation = characterPopulation;
            _settings = settings;
            _notifications = notifications;
            _loc = loc;
        }

        public Character CurrentTarget => _currentTarget;

        public void Awake()
        {
            _wyrm = GetComponent<WyrmComponent>();
            _movement = GetComponent<WyrmMovement>();
        }

        /// <summary>
        /// Drop our current target — the wyrm has stopped hunting (sated,
        /// digesting, sandbox). Only clears the movement target if the
        /// hunter set it; preserves whatever the wanderer (or a future
        /// system) has parked on movement so digesting wyrms keep walking.
        /// </summary>
        private void StopHunting()
        {
            if (_currentTarget != null)
            {
                // Only clear movement if our previous target is the one
                // currently driving it. Cheap check: did SetTarget value
                // match our last target's position? Approximate via
                // distance threshold instead — exact value can drift due
                // to floating point.
                if (_movement.HasTarget &&
                    _currentTarget.GameObject != null &&
                    (_movement.TargetPosition - _currentTarget.Transform.position)
                        .sqrMagnitude < 0.25f)
                {
                    _movement.ClearTarget();
                }
                _currentTarget = null;
            }
        }

        public override void Tick()
        {
            if (_wyrm == null || _movement == null) return;

            // Sated wyrms still walk to beavers (predator instinct), they
            // just don't damage walls in transit. Hunting is unaffected.
            // SandboxMode disables both targeting and eating.
            if (_settings.SandboxMode)
            {
                StopHunting();
                return;
            }

            // While digesting (hunger below the spec's HuntingThreshold),
            // ignore beavers entirely. The wyrm crawls home or stays put.
            // Crossing the threshold flips it back into hunting mode.
            if (!_wyrm.IsHunting)
            {
                StopHunting();
                return;
            }

            RetargetIfDue();
            CheckEatOnContact();
        }

        private void RetargetIfDue()
        {
            // Count up every tick, regardless of branch. This way the cap
            // applies uniformly even when we currently have no target —
            // otherwise the no-target branch retargets every tick, which is
            // both wasteful and inconsistent with the ticked-cooldown intent.
            _ticksSinceRetarget++;

            // If our previous target was eaten or otherwise destroyed,
            // drop it immediately so the retarget pass doesn't see a
            // dead-but-non-null reference and short-circuit the search.
            if (_currentTarget != null && _currentTarget.GameObject == null)
            {
                _currentTarget = null;
                _ticksSinceRetarget = RetargetEveryTicks;
            }

            bool needsRetarget =
                _currentTarget == null
                || _ticksSinceRetarget >= RetargetEveryTicks;
            if (needsRetarget)
            {
                _ticksSinceRetarget = 0;
                _currentTarget = FindNearestBeaver();
            }

            // Always feed the *current* live target position to movement.
            // SetTarget itself ignores tiny jitter and triggers a replan
            // only when the target moved more than a tile, so this is
            // cheap to call every tick — and it stops the wyrm chasing
            // ghost positions where the beaver was 8 ticks ago.
            //
            // If we don't have a target (no beavers around, all
            // unreachable), we deliberately do NOT call ClearTarget on
            // movement: the WyrmWanderer is allowed to drive movement
            // when hunting is on but there's nothing to hunt, and we
            // shouldn't yank the wheel away from it. We only clear our
            // hunter-owned target if we actually had one set.
            if (_currentTarget != null && _currentTarget.GameObject != null)
            {
                _movement.SetTarget(_currentTarget.Transform.position);
            }
        }

        private Character FindNearestBeaver()
        {
            Character best = null;
            float bestSqr = float.PositiveInfinity;
            Vector3 here = Transform.position;

            for (int i = 0; i < _characterPopulation.NumberOfCharacters; i++)
            {
                var character = _characterPopulation.Characters[i];
                if (character == null || character.GameObject == null) continue;
                // Only target beavers — bots and any future custom characters
                // are out of scope. Beaver component is the marker.
                if (!character.HasComponent<Beaver>()) continue;
                float sqr = (character.Transform.position - here).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = character;
                }
            }
            return best;
        }

        private void CheckEatOnContact()
        {
            if (_currentTarget == null || _currentTarget.GameObject == null) return;
            float sqr = (_currentTarget.Transform.position - Transform.position)
                .sqrMagnitude;
            if (sqr > ContactDistance * ContactDistance) return;

            EatBeaver(_currentTarget);
        }

        private void EatBeaver(Character beaver)
        {
            // Kill via the engine's public Mortal.DiePubliclyAsSoonAsPossible.
            // That posts the standard CharacterKilledEvent, which keeps
            // BeaverPopulation, achievements, and any other listeners
            // consistent. Beavers normally die from need-system
            // exhaustion (hunger/thirst); we just route through the same
            // exit door with our own death-message string.
            try
            {
                var mortal = beaver
                    .GetComponent<Timberborn.MortalSystem.Mortal>();
                if (mortal != null)
                {
                    // The argument is the death-message *display string*
                    // shown verbatim in the kill-feed UI (vanilla examples
                    // pass things like "<name> was forced to die by an
                    // evil dev."). We must localize before passing.
                    mortal.DiePubliclyAsSoonAsPossible(
                        _loc.T(WyrmDeathMessageKey));
                }
                _notifications.Post(WyrmNotifications.BeaverEatenKey, beaver);
                Debug.Log(
                    $"[WhereWyrmsWait] Wyrm at {Transform.position} ate {beaver.Name}.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not kill beaver {beaver.Name}: " +
                    $"{ex.Message}");
            }

            _wyrm.Sate();
            _currentTarget = null;
            _movement.ClearTarget();
        }

        // Localization key resolved per-kill into the human-readable
        // death-message displayed in the kill-feed UI.
        private const string WyrmDeathMessageKey = "WWW.Notification.BeaverEaten";
    }
}
