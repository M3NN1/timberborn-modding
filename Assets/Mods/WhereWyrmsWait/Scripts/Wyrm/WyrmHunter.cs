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
    /// Wyrm targeting AI: each tick, picks the nearest live beaver as
    /// the hunt target and feeds the position to <see cref="WyrmMovement"/>.
    /// On contact (within <see cref="ContactDistance"/>), eats the
    /// beaver via vanilla <c>Mortal.DiePubliclyAsSoonAsPossible</c> and
    /// resets hunger. Sandbox mode disables eating but leaves
    /// targeting on for layout testing.
    /// </summary>
    public class WyrmHunter : TickableComponent, IAwakableComponent
    {
        private const float ContactDistance = 0.6f;
        private const int RetargetEveryTicks = 8;
        private const string WyrmDeathMessageKey = "WWW.Notification.BeaverEaten";

        private readonly CharacterPopulation _characterPopulation;
        private readonly WyrmSettings _settings;
        private readonly WyrmNotifications _notifications;
        private readonly ILoc _loc;

        private WyrmComponent _wyrm;
        private WyrmMovement _movement;
        private Character _currentTarget;
        private int _ticksSinceRetarget = RetargetEveryTicks;

        // True while the current movement target was set by us. Lets us
        // hand movement back to the wanderer cleanly when we stop
        // finding prey — without this flag the wanderer would see a
        // stale HasTarget from our last SetTarget and stay parked.
        private bool _ownsMovement;

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

        public override void Tick()
        {
            if (_wyrm == null || _movement == null) return;
            if (_settings.SandboxMode || !_wyrm.IsHunting)
            {
                StopHunting();
                return;
            }

            RetargetIfDue();
            CheckEatOnContact();
        }

        private void StopHunting()
        {
            _currentTarget = null;
            if (_ownsMovement)
            {
                _movement.ClearTarget();
                _ownsMovement = false;
            }
        }

        private void RetargetIfDue()
        {
            _ticksSinceRetarget++;

            // Drop dead-but-non-null targets immediately so the retarget
            // pass below doesn't short-circuit on a stale reference.
            if (_currentTarget != null && _currentTarget.GameObject == null)
            {
                _currentTarget = null;
                _ticksSinceRetarget = RetargetEveryTicks;
            }

            if (_currentTarget == null || _ticksSinceRetarget >= RetargetEveryTicks)
            {
                _ticksSinceRetarget = 0;
                _currentTarget = FindNearestBeaver();
            }

            // Feed the live target position to movement every tick.
            // SetTarget de-dupes small jitter and only triggers a
            // replan on > 1u jumps, so this stays cheap. With no
            // target, hand movement back to the wanderer.
            if (_currentTarget != null && _currentTarget.GameObject != null)
            {
                _movement.SetTarget(_currentTarget.Transform.position);
                _ownsMovement = true;
            }
            else if (_ownsMovement)
            {
                _movement.ClearTarget();
                _ownsMovement = false;
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
            // Route through Mortal so vanilla CharacterKilledEvent
            // listeners (BeaverPopulation, achievements) stay consistent.
            try
            {
                var mortal = beaver
                    .GetComponent<Timberborn.MortalSystem.Mortal>();
                if (mortal != null)
                {
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
            _ownsMovement = false;
        }
    }
}
