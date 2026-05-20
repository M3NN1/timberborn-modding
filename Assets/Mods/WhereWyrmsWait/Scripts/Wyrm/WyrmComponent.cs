using Mods.WhereWyrmsWait.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using Timberborn.Persistence;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Per-wyrm hunger / contamination state. Sits on the entity
    /// instantiated by <see cref="WyrmFactory"/>. Movement and target
    /// selection live in <c>WyrmMovement</c> and <c>WyrmHunter</c>;
    /// this component owns state that's interesting to UI and to the
    /// kill-threshold check.
    /// <para>
    /// Kill mechanic: contamination is the only damage pool. The wyrm
    /// has no separate HP — every "the wyrm got hurt" path funnels
    /// through <see cref="AbsorbContamination"/>. The bucket fills as
    /// the wyrm passively drinks badwater out of the water column
    /// (driven by <c>WyrmContaminationSampler</c>) and drains while
    /// the wyrm isn't drinking. Above the lethal threshold, the wyrm
    /// dies of poisoning.
    /// </para>
    /// <para>
    /// The class is named <c>WyrmComponent</c> rather than just <c>Wyrm</c>
    /// to avoid colliding with the namespace name and any future top-level
    /// "Wyrm" coordinator. Keeps the type unambiguous from any file.
    /// </para>
    /// </summary>
    public class WyrmComponent : TickableComponent,
        IPersistentEntity, IInitializableEntity, IDeletableEntity
    {
        // Save keys.
        private static readonly ComponentKey SaveKey = new ComponentKey("Wyrm");
        private static readonly PropertyKey<float> HungerKey =
            new PropertyKey<float>("Hunger");
        private static readonly PropertyKey<float> ContaminationKey =
            new PropertyKey<float>("Contamination");
        private static readonly PropertyKey<bool> SatedKey =
            new PropertyKey<bool>("Sated");
        // Position/rotation are saved here because the wyrm's blueprint
        // doesn't include vanilla Character (which would persist them
        // for free). Without this, a saved-and-reloaded wyrm respawns
        // at the world origin.
        private static readonly PropertyKey<Vector3> PositionKey =
            new PropertyKey<Vector3>("Position");
        private static readonly PropertyKey<Quaternion> RotationKey =
            new PropertyKey<Quaternion>("Rotation");

        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly WyrmRegistry _registry;
        private readonly WyrmSettings _settings;
        private readonly WyrmNotifications _notifications;

        private WyrmSpec _spec;

        // Live state.
        private float _hunger;
        private float _contamination;
        private bool _sated;
        // True iff the sampler called AbsorbContamination this tick. Drives
        // the "Poisoned" status icon and the contamination-bar tint. Reset
        // by the sampler each sample cycle, so it accurately reflects "is
        // the wyrm currently drinking badwater" rather than "did it ever."
        private bool _isAbsorbing;
        private bool _initialized;

        public WyrmComponent(
            IDayNightCycle dayNightCycle,
            EntityService entityService,
            WyrmRegistry registry,
            WyrmSettings settings,
            WyrmNotifications notifications)
        {
            _dayNightCycle = dayNightCycle;
            _entityService = entityService;
            _registry = registry;
            _settings = settings;
            _notifications = notifications;
        }

        // -------- public API --------

        public WyrmSpec Spec => _spec;

        public float Hunger => _hunger;
        public bool IsHungry => !_sated && _hunger >= (_spec?.HungerMax ?? 1f);

        /// <summary>
        /// Hunger fraction in 0..1 of <c>HungerMax</c>. UI uses this for
        /// the hunger bar.
        /// </summary>
        public float HungerFraction
        {
            get
            {
                float max = _spec?.HungerMax ?? 1f;
                return max <= 0f ? 0f : Mathf.Clamp01(_hunger / max);
            }
        }

        /// <summary>
        /// True when the wyrm is in hunting mode: hunger has crossed the
        /// per-spec threshold and the wyrm isn't Soothesop-sated. Below
        /// the threshold (or while sated), the wyrm crawls and ignores
        /// beavers — it's still digesting its last kill.
        /// </summary>
        public bool IsHunting
        {
            get
            {
                if (_sated) return false;
                if (_spec == null) return true;
                float threshold = _spec.HuntingThreshold * _spec.HungerMax;
                return _hunger >= threshold;
            }
        }

        /// <summary>
        /// Walk speed scaled by hunger and Soothesop-sated state. At
        /// hunger 0 or while sated, returns
        /// <c>WalkSpeed × MinSpeedMultiplier</c>; at <c>HungerMax</c>,
        /// returns full <c>WalkSpeed</c>. Linear in between. Used by
        /// <c>WyrmMovement</c> in place of <c>WyrmSpec.WalkSpeed</c>.
        /// </summary>
        public float EffectiveWalkSpeed
        {
            get
            {
                if (_spec == null) return 1f;
                float min = Mathf.Clamp01(_spec.MinSpeedMultiplier);
                if (_sated) return _spec.WalkSpeed * min;
                float t = HungerFraction;
                float multiplier = Mathf.Lerp(min, 1f, t);
                return _spec.WalkSpeed * multiplier;
            }
        }

        /// <summary>
        /// Total badwater absorbed (in column-depth units), minus regen.
        /// Range 0 .. <see cref="LethalContamination"/>; at the cap the
        /// wyrm dies.
        /// </summary>
        public float Contamination => _contamination;

        public float LethalContamination =>
            (_spec?.LethalContamination ?? 5f)
            * (_settings?.ContaminationResistanceMultiplier ?? 1f);

        public float ContaminationFraction =>
            LethalContamination > 0f
                ? Mathf.Clamp01(_contamination / LethalContamination)
                : 0f;

        public bool IsSated => _sated;

        /// <summary>
        /// True while badwater is actively being absorbed. Used by the UI
        /// (status icon, contamination-bar tint). Distinct from "is over a
        /// contaminated tile" — a wyrm sitting on dry land or on an empty
        /// tile that *was* contaminated is not absorbing.
        /// </summary>
        public bool IsAbsorbingContamination => _isAbsorbing;

        /// <summary>Set by <c>WyrmSatiationDetector</c> each tick.</summary>
        public void SetSated(bool value) => _sated = value;

        /// <summary>Reset hunger to zero — call when the wyrm eats a beaver/wall.</summary>
        public void Sate() => _hunger = 0f;

        /// <summary>
        /// Add to the wyrm's contamination bucket. Called by
        /// <c>WyrmContaminationSampler</c> whenever a slice of badwater
        /// has been drunk out of the surrounding water column.
        /// <paramref name="amount"/> is the depth-units of contaminated
        /// water actually consumed this tick.
        /// </summary>
        public void AbsorbContamination(float amount)
        {
            if (amount <= 0f) return;
            _contamination += amount;
            _isAbsorbing = true;
        }

        /// <summary>
        /// Drain the contamination bucket at the spec-configured regen
        /// rate. Called by the sampler on ticks when no badwater was
        /// drunk. Clamps at 0.
        /// </summary>
        public void RegenContamination(float deltaDays)
        {
            _isAbsorbing = false;
            if (deltaDays <= 0f) return;
            float regen = (_spec?.ContaminationRegenPerDay ?? 0.2f) * deltaDays;
            _contamination = Mathf.Max(0f, _contamination - regen);
        }

        // -------- lifecycle --------

        public void InitializeEntity()
        {
            _spec = GetComponent<WyrmSpec>();
            _registry.Register(this);
            _initialized = true;
        }

        public override void Tick()
        {
            if (!_initialized || _spec == null)
            {
                return;
            }
            if (_settings == null)
            {
                WyrmDiagnostics.LogOnce(
                    "WyrmComponent.SettingsNull",
                    "WyrmSettings was null in WyrmComponent.Tick — DI binding issue. "
                    + "Wyrm will idle until injection succeeds.");
                return;
            }

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;

            TickHunger(deltaDays);

            if (_contamination >= LethalContamination)
            {
                Die("contamination");
            }
        }

        public void Save(IEntitySaver entitySaver)
        {
            var saver = entitySaver.GetComponent(SaveKey);
            saver.Set(HungerKey, _hunger);
            saver.Set(ContaminationKey, _contamination);
            saver.Set(SatedKey, _sated);
            // Persist position so the wyrm reloads where it died, not at
            // the world origin. The wyrm template doesn't include vanilla
            // Character (which would do this for free), so we own it.
            if (Transform != null)
            {
                saver.Set(PositionKey, Transform.position);
                saver.Set(RotationKey, Transform.rotation);
            }
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (!entityLoader.TryGetComponent(SaveKey, out var loader))
            {
                return;
            }
            if (loader.Has(HungerKey)) _hunger = loader.Get(HungerKey);
            if (loader.Has(ContaminationKey)) _contamination = loader.Get(ContaminationKey);
            if (loader.Has(SatedKey)) _sated = loader.Get(SatedKey);
            if (Transform != null && loader.Has(PositionKey))
            {
                var pos = loader.Get(PositionKey);
                var rot = loader.Has(RotationKey)
                    ? loader.Get(RotationKey)
                    : Transform.rotation;
                Transform.SetPositionAndRotation(pos, rot);
            }
        }

        public void DeleteEntity()
        {
            _registry.Unregister(this);
            // Belt-and-braces: if anything tries to tick us between now and
            // GameObject destruction (e.g. a queued frame), the _initialized
            // gate makes Tick() bail cleanly.
            _initialized = false;
        }

        // -------- internals --------

        private void TickHunger(float deltaDays)
        {
            float rate = (_spec?.HungerPerDay ?? 1f) * _settings.HungerRateMultiplier;
            _hunger = Mathf.Min(_spec?.HungerMax ?? 1f, _hunger + rate * deltaDays);
        }

        private void Die(string cause)
        {
            Debug.Log(
                $"[WhereWyrmsWait] Wyrm dies via {cause} at " +
                $"{(Transform != null ? Transform.position : Vector3.zero)}.");
            _notifications.Post(WyrmNotifications.WyrmDiedKey, this);
            _entityService.Delete(this);
        }
    }
}
