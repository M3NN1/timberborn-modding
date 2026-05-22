using Mods.WhereWyrmsWait.Core;
using Mods.WhereWyrmsWait.Hazards;
using System;
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
    /// Per-wyrm hunger / contamination state. Movement and target
    /// selection live in <c>WyrmMovement</c> and <c>WyrmHunter</c>.
    /// Contamination is the wyrm's only damage pool — every "got hurt"
    /// path funnels through <see cref="AbsorbContamination"/>; above
    /// the lethal threshold, the wyrm dies of poisoning.
    /// </summary>
    public class WyrmComponent : TickableComponent,
        IPersistentEntity, IInitializableEntity, IPostLoadableEntity, IDeletableEntity
    {
        private static readonly ComponentKey SaveKey = new ComponentKey("Wyrm");
        private static readonly PropertyKey<float> HungerKey =
            new PropertyKey<float>("Hunger");
        private static readonly PropertyKey<float> ContaminationKey =
            new PropertyKey<float>("Contamination");
        private static readonly PropertyKey<bool> SatedKey =
            new PropertyKey<bool>("Sated");
        // Position/rotation owned here because the wyrm template doesn't
        // include vanilla Character (which would persist them for free).
        private static readonly PropertyKey<Vector3> PositionKey =
            new PropertyKey<Vector3>("Position");
        private static readonly PropertyKey<Quaternion> RotationKey =
            new PropertyKey<Quaternion>("Rotation");
        // Den that birthed this wyrm, persisted as the den's EntityId
        // so the den can re-attribute its live offspring after load.
        // Husk-born wyrms have Guid.Empty here.
        private static readonly PropertyKey<Guid> OwningDenIdKey =
            new PropertyKey<Guid>("OwningDenId");

        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly EntityRegistry _entityRegistry;
        private readonly WyrmRegistry _registry;
        private readonly WyrmSettings _settings;
        private readonly WyrmNotifications _notifications;

        private WyrmSpec _spec;

        private float _hunger;
        private float _contamination;
        private bool _sated;
        // True iff the sampler called AbsorbContamination this tick.
        // Drives the "Poisoned" status icon and contamination-bar tint.
        private bool _isAbsorbing;
        private bool _initialized;

        // _owningDenId is the persisted GUID; _owningDen is the
        // resolved instance. Set together by SetOwningDen at spawn,
        // re-resolved from the GUID in PostLoadEntity after load.
        private Guid _owningDenId;
        private WyrmDen _owningDen;

        public WyrmComponent(
            IDayNightCycle dayNightCycle,
            EntityService entityService,
            EntityRegistry entityRegistry,
            WyrmRegistry registry,
            WyrmSettings settings,
            WyrmNotifications notifications)
        {
            _dayNightCycle = dayNightCycle;
            _entityService = entityService;
            _entityRegistry = entityRegistry;
            _registry = registry;
            _settings = settings;
            _notifications = notifications;
        }

        public WyrmSpec Spec => _spec;

        public float Hunger => _hunger;

        /// <summary>Hunger fraction in 0..1 of <c>HungerMax</c>.</summary>
        public float HungerFraction
        {
            get
            {
                float max = _spec?.HungerMax ?? 1f;
                return max <= 0f ? 0f : Mathf.Clamp01(_hunger / max);
            }
        }

        /// <summary>
        /// True when the wyrm pursues prey: hunger past
        /// <see cref="WyrmSpec.HuntingThreshold"/> and not sated. Below
        /// the threshold the wyrm digests and ignores beavers.
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
        /// Walk speed scaled by hunger and Soothesop-sated state. Lerps
        /// linearly between <c>WalkSpeed × MinSpeedMultiplier</c> at
        /// hunger 0 / sated and full <c>WalkSpeed</c> at <c>HungerMax</c>.
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

        public float Contamination => _contamination;

        public float LethalContamination =>
            (_spec?.LethalContamination ?? 5f)
            * (_settings?.ContaminationResistanceMultiplier ?? 1f);

        public float ContaminationFraction =>
            LethalContamination > 0f
                ? Mathf.Clamp01(_contamination / LethalContamination)
                : 0f;

        public bool IsSated => _sated;

        /// <summary>The den that spawned this wyrm, or null for husk-born wyrms.</summary>
        public WyrmDen OwningDen => _owningDen;

        /// <summary>
        /// True while badwater is being absorbed. Distinct from "is on
        /// a contaminated tile" — a wyrm sitting on a tile that *was*
        /// contaminated but is now drained is not absorbing.
        /// </summary>
        public bool IsAbsorbingContamination => _isAbsorbing;

        public void SetSated(bool value) => _sated = value;

        /// <summary>
        /// Stamp this wyrm with its owning den. Called by
        /// <see cref="WyrmFactory"/> right after spawn; pass null for
        /// husk-born wyrms.
        /// </summary>
        public void SetOwningDen(WyrmDen den)
        {
            _owningDen = den;
            _owningDenId = den != null
                ? den.GetComponent<EntityComponent>().EntityId
                : Guid.Empty;
        }

        /// <summary>
        /// True iff this wyrm's saved owner-id matches the given den's
        /// EntityId. Used by <see cref="WyrmDen.InitializeEntity"/> to
        /// reclaim offspring after load — at Initialize time
        /// <see cref="OwningDen"/> isn't resolved yet, so dens have to
        /// match through the loaded GUID.
        /// </summary>
        public bool HasOwningDenId(WyrmDen candidate)
        {
            if (candidate == null) return false;
            if (_owningDenId == Guid.Empty) return false;
            return _owningDenId
                == candidate.GetComponent<EntityComponent>().EntityId;
        }

        /// <summary>Reset hunger to zero — call when the wyrm eats.</summary>
        public void Sate() => _hunger = 0f;

        public void AbsorbContamination(float amount)
        {
            if (amount <= 0f) return;
            _contamination += amount;
            _isAbsorbing = true;
        }

        public void RegenContamination(float deltaDays)
        {
            _isAbsorbing = false;
            if (deltaDays <= 0f) return;
            float regen = (_spec?.ContaminationRegenPerDay ?? 0.2f) * deltaDays;
            _contamination = Mathf.Max(0f, _contamination - regen);
        }

        public void InitializeEntity()
        {
            _spec = GetComponent<WyrmSpec>();
            _registry.Register(this);
            _initialized = true;
        }

        public override void Tick()
        {
            if (!_initialized || _spec == null) return;
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
            if (Transform != null)
            {
                saver.Set(PositionKey, Transform.position);
                saver.Set(RotationKey, Transform.rotation);
            }
            // Skip empty owner-id to keep husk-born wyrms tidy in the save.
            if (_owningDenId != Guid.Empty)
            {
                saver.Set(OwningDenIdKey, _owningDenId);
            }
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (!entityLoader.TryGetComponent(SaveKey, out var loader)) return;

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
            // Resolving the den instance here would race against the
            // den's own load — entity load order isn't guaranteed.
            // PostLoadEntity does the lookup once everyone's loaded.
            if (loader.Has(OwningDenIdKey))
            {
                _owningDenId = loader.Get(OwningDenIdKey);
            }
        }

        public void PostLoadEntity()
        {
            if (_owningDenId == Guid.Empty || _owningDen != null) return;
            // Den may have been deleted in this save (e.g. dynamited)
            // — null result is fine, the wyrm is just orphaned and
            // doesn't count toward any cap.
            var entity = _entityRegistry.GetEntity(_owningDenId);
            if (entity == null) return;
            _owningDen = entity.GetComponent<WyrmDen>();
        }

        public void DeleteEntity()
        {
            _registry.Unregister(this);
            // Gate Tick() against any queued frame between now and
            // GameObject destruction.
            _initialized = false;
        }

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
