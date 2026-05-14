using System;
using System.Collections.Generic;
using Mods.DamDegradation.Core;
using Mods.DamDegradation.Repair;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using Timberborn.NotificationSystem;
using Timberborn.Persistence;
using Timberborn.StatusSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.DamDegradation.Components
{
    /// <summary>
    /// Per-block deterioration component attached by the
    /// <see cref="DamDeteriorationSpec"/> decorator. Ticks once per simulation
    /// tick while finished, applies hydro-stress damage based on water depth at
    /// the block's coordinate, scaled by the global multiplier and reduced when
    /// a deteriorating block sits directly downstream. On zero HP the entity is
    /// deleted so the vanilla water simulator can breach the wall, and one hop
    /// of cascade damage is applied to neighbours.
    /// </summary>
    public class DamDeterioration
        : TickableComponent, IFinishedStateListener, IPersistentEntity, IDeletableEntity
    {
        // --- save keys ---
        private static readonly ComponentKey SaveKey = new ComponentKey("DamDeterioration");
        private static readonly PropertyKey<float> HealthKey = new PropertyKey<float>("Health");

        // --- localization keys (English fallbacks live in the CSV) ---
        private const string WarningStatusKey = "DDD.Status.Warning";
        private const string CriticalStatusKey = "DDD.Status.Critical";
        private const string CriticalAlertKey = "DDD.Status.Critical.Short";
        private const string BreachNotificationKey = "DDD.Notification.Breach";

        // --- status sprites: vanilla sprites used by similar mechanics ---
        // Sprite names are leaf-only; StatusSpriteLoader prefixes "Sprites/StatusIcons/".
        private const string WarningSprite = "LackOfResources";
        private const string CriticalSprite = "GenericError";

        // --- support / head recompute cadence ---
        // Water levels and downstream support change slowly (water sim runs at
        // a fixed rate, players add/remove blocks at human pace). Sampling
        // every tick wastes work on hundreds of dam blocks; ~once per ~3s of
        // game time at default speed is plenty.
        private const int HeadRecomputeEveryTicks = 16;
        private const int SupportRecomputeEveryTicks = 32;

        // --- injected ---
        private readonly IThreadSafeWaterMap _waterMap;
        private readonly IWaterService _waterService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly EntityService _entityService;
        private readonly IBlockService _blockService;
        private readonly DamSettings _settings;
        private readonly DamRepairRegistry _repairRegistry;
        private readonly NotificationBus _notificationBus;
        private readonly ILoc _loc;

        // --- runtime state ---
        private DamDeteriorationSpec _spec;
        private BlockObject _blockObject;
        private StatusSubject _statusSubject;
        private StatusToggle _warningToggle;
        private StatusToggle _criticalToggle;

        private List<IDamDeteriorationListener> _listenersCache;

        private float _health;
        private float _supportFactor = 1f;
        private float _cachedHead;
        private float _appliedInflowLimit = -1f;
        private int _ticksSinceSupportRecompute = SupportRecomputeEveryTicks;
        private int _ticksSinceHeadRecompute = HeadRecomputeEveryTicks;
        private bool _isFinished;
        private bool _inWarning;
        private bool _inCritical;
        private bool _registeredForRepair;
        private bool _hasInflowLimit;
        // True while we have actively removed the dam's full obstacle to
        // simulate leakage. Tracked separately from _hasInflowLimit so we
        // can restore the obstacle even if the inflow-limit call has
        // already cleared.
        private bool _isLeaking;

        public DamDeterioration(
            IThreadSafeWaterMap waterMap,
            IWaterService waterService,
            IDayNightCycle dayNightCycle,
            EntityService entityService,
            IBlockService blockService,
            DamSettings settings,
            DamRepairRegistry repairRegistry,
            NotificationBus notificationBus,
            ILoc loc)
        {
            _waterMap = waterMap;
            _waterService = waterService;
            _dayNightCycle = dayNightCycle;
            _entityService = entityService;
            _blockService = blockService;
            _settings = settings;
            _repairRegistry = repairRegistry;
            _notificationBus = notificationBus;
            _loc = loc;
        }

        // --- public API ---

        public float Health => _health;
        public float MaxHealth => _spec?.MaxHealth ?? 100f;
        public float HealthFraction =>
            MaxHealth > 0f ? Mathf.Clamp01(_health / MaxHealth) : 0f;
        public DamDeteriorationSpec Spec => _spec;
        public bool NeedsRepair => _spec != null && _health < MaxHealth;
        public Vector3Int Coordinates =>
            _blockObject != null ? _blockObject.Coordinates : Vector3Int.zero;

        /// <summary>Restores up to <paramref name="amount"/> HP. Capped at MaxHealth.</summary>
        public void Repair(float amount)
        {
            if (_spec == null || amount <= 0f)
            {
                return;
            }
            _health = Mathf.Min(MaxHealth, _health + amount);
            NotifyListeners(l => l.OnRepaired(this, amount));
            ReevaluateThresholds();
            ReevaluateLeakage();
        }

        /// <summary>Repairs by the spec's default <c>RepairAmount</c>.</summary>
        public void Repair() => Repair(_spec?.RepairAmount ?? 0f);

        /// <summary>Cascade hit applied by a neighbour that just breached. Capped at zero.</summary>
        internal void ApplyCascadeDamage(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            _health = Mathf.Max(0f, _health - amount);
            ReevaluateThresholds();
            ReevaluateLeakage();
            if (_health <= 0f)
            {
                // No further horizontal chain (we were *part of* a chain), but
                // the vertical column above us still drops with us.
                Breach(triggerHorizontalCascade: false);
            }
        }

        // --- lifecycle ---

        public override void StartTickable()
        {
            _spec = GetComponent<DamDeteriorationSpec>();
            _blockObject = GetComponent<BlockObject>();
            _statusSubject = GetComponent<StatusSubject>();

            if (_health <= 0f && _spec != null)
            {
                _health = _spec.MaxHealth;
            }

            RegisterStatusToggles();
            ReevaluateThresholds();
        }

        public override void Tick()
        {
            if (!_isFinished || _spec == null || _blockObject == null)
            {
                return;
            }
            if (!_settings.DegradationEnabled)
            {
                return;
            }

            RecomputeSupportIfDue();

            float deltaDays = _dayNightCycle.FixedDeltaTimeInHours / 24f;
            float head = HeadIfDueOtherwiseCached();
            float wearPerDay = ComputeWearPerDay(head);
            _health = Mathf.Max(0f, _health - wearPerDay * deltaDays);

            ReevaluateThresholds();
            ReevaluateLeakage();

            if (_health <= 0f)
            {
                Breach(triggerHorizontalCascade: true);
            }
        }

        public void OnEnterFinishedState()
        {
            _isFinished = true;
            EnableComponent();
            ReevaluateThresholds();
        }

        public void OnExitFinishedState()
        {
            _isFinished = false;
            // Don't restore the obstacle here: the engine's own
            // WaterObstacle.RemoveFromWaterService runs on the same
            // OnExitFinishedState event, and call order isn't guaranteed.
            // Just clear the meter and our internal flag — the cell will be
            // empty after the engine's removal, which is the correct end
            // state for a dam that's leaving the world.
            ClearInflowLimitIfApplied();
            _isLeaking = false;
            UnregisterFromRepairs();
            DisableComponent();
        }

        public void DeleteEntity()
        {
            // Same reasoning as OnExitFinishedState: the engine tears down
            // its own obstacle slot when the entity is deleted. We only need
            // to clear our metered limit and forget the leaking flag.
            ClearInflowLimitIfApplied();
            _isLeaking = false;
            UnregisterFromRepairs();
        }

        public void Save(IEntitySaver entitySaver)
        {
            entitySaver.GetComponent(SaveKey).Set(HealthKey, _health);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(SaveKey, out var loader) && loader.Has(HealthKey))
            {
                _health = loader.Get(HealthKey);
            }
        }

        // --- internals ---

        private float ComputeWearPerDay(float head)
        {
            float baseWear = _spec.BaseWearPerDay;
            float stress = head > 0f
                ? _spec.StressWearCoefficient * Mathf.Pow(head, _spec.StressExponent)
                : 0f;
            float raw = (baseWear + stress) * _supportFactor * _settings.GlobalWearMultiplier;
            float variance = _settings.RandomVarianceFraction;
            if (variance > 0f)
            {
                raw *= 1f + UnityEngine.Random.Range(-variance, variance);
            }
            return Mathf.Max(0f, raw);
        }

        /// <summary>
        /// Returns the maximum hydrostatic head pressing on the block, taken
        /// across the four horizontal neighbours. <c>head</c> is the height of
        /// the water surface above the block's own z-level — i.e. how much
        /// water column actually pushes on this specific block. The dam's own
        /// cell is itself an obstacle so we sample the neighbours instead.
        /// </summary>
        private float SafeUpstreamHead(Vector3Int blockCoords)
        {
            float maxHead = 0f;
            maxHead = Mathf.Max(maxHead, HeadAtNeighbour(blockCoords, Vector3Int.right));
            maxHead = Mathf.Max(maxHead, HeadAtNeighbour(blockCoords, Vector3Int.left));
            maxHead = Mathf.Max(maxHead, HeadAtNeighbour(blockCoords, Vector3Int.up));
            maxHead = Mathf.Max(maxHead, HeadAtNeighbour(blockCoords, Vector3Int.down));
            return maxHead;
        }

        private float HeadAtNeighbour(Vector3Int blockCoords, Vector3Int offset)
        {
            try
            {
                var probe = blockCoords + offset;
                float surface = _waterMap.WaterHeightOrFloor(probe);
                return Mathf.Max(0f, surface - blockCoords.z);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DamDegradation] Could not query head at {blockCoords + offset}: {ex.Message}");
                return 0f;
            }
        }

        private void RecomputeSupportIfDue()
        {
            if (_ticksSinceSupportRecompute++ < SupportRecomputeEveryTicks)
            {
                return;
            }
            _ticksSinceSupportRecompute = 0;
            _supportFactor = ComputeSupportFactor();
        }

        /// <summary>
        /// Returns the cached upstream head, recomputing it once every
        /// <see cref="HeadRecomputeEveryTicks"/> ticks. Wear is then applied
        /// every tick using the cached value scaled by the current per-tick
        /// time delta, so coarser sampling doesn't change long-term damage —
        /// it just smooths out short-term noise from the water sim.
        /// </summary>
        private float HeadIfDueOtherwiseCached()
        {
            if (_ticksSinceHeadRecompute++ < HeadRecomputeEveryTicks)
            {
                return _cachedHead;
            }
            _ticksSinceHeadRecompute = 0;
            _cachedHead = SafeUpstreamHead(_blockObject.Coordinates);
            return _cachedHead;
        }

        private float ComputeSupportFactor()
        {
            if (_blockObject == null || _spec == null)
            {
                return 1f;
            }

            // Pick the neighbour with the highest head — that's the upstream side.
            var origin = _blockObject.Coordinates;
            Vector3Int[] dirs =
            {
                Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            };

            float bestHead = 0f;
            Vector3Int upstream = Vector3Int.zero;
            foreach (var d in dirs)
            {
                float head = HeadAtNeighbour(origin, d);
                if (head > bestHead)
                {
                    bestHead = head;
                    upstream = d;
                }
            }

            if (bestHead <= 0f)
            {
                // No water pressing from any side, no concept of "downstream".
                return 1f;
            }

            // Downstream is the opposite cell.
            var downstream = origin - upstream;
            bool hasSupport = _blockService
                .GetFirstObjectWithComponentAt<DamDeterioration>(downstream) != null;

            return hasSupport
                ? Mathf.Clamp(_spec.DownstreamSupportFactor, 0.1f, 1f)
                : 1f;
        }

        private void ReevaluateThresholds()
        {
            if (_spec == null)
            {
                return;
            }
            float fraction = HealthFraction;
            bool shouldWarn = fraction <= _spec.WarningHealthFraction && _health < MaxHealth;
            bool shouldCrit = fraction <= _spec.CriticalHealthFraction && _health < MaxHealth;

            ToggleWarning(shouldWarn);
            ToggleCritical(shouldCrit);
            UpdateRepairRegistration(shouldWarn);
        }

        private void ToggleWarning(bool active)
        {
            if (active == _inWarning)
            {
                return;
            }
            _inWarning = active;
            if (_warningToggle != null)
            {
                _warningToggle.Toggle(active);
            }
            NotifyListeners(l =>
            {
                if (active) l.OnEnterWarning(this); else l.OnExitWarning(this);
            });
        }

        private void ToggleCritical(bool active)
        {
            if (active == _inCritical)
            {
                return;
            }
            _inCritical = active;
            if (_criticalToggle != null)
            {
                _criticalToggle.Toggle(active);
            }
            NotifyListeners(l =>
            {
                if (active) l.OnEnterCritical(this); else l.OnExitCritical(this);
            });
        }

        private void RegisterStatusToggles()
        {
            if (_statusSubject == null)
            {
                return;
            }
            _warningToggle = StatusToggle.CreateNormalStatusWithFloatingIcon(
                WarningSprite, _loc.T(WarningStatusKey));
            _criticalToggle = StatusToggle.CreateNormalStatusWithAlertAndFloatingIcon(
                CriticalSprite, _loc.T(CriticalStatusKey), _loc.T(CriticalAlertKey));
            _statusSubject.RegisterStatus(_warningToggle);
            _statusSubject.RegisterStatus(_criticalToggle);
        }

        private void UpdateRepairRegistration(bool needsRepair)
        {
            if (needsRepair && !_registeredForRepair)
            {
                _repairRegistry.Register(this);
                _registeredForRepair = true;
            }
            else if (!needsRepair && _registeredForRepair)
            {
                _repairRegistry.Unregister(this);
                _registeredForRepair = false;
            }
        }

        private void UnregisterFromRepairs()
        {
            if (_registeredForRepair)
            {
                _repairRegistry.Unregister(this);
                _registeredForRepair = false;
            }
        }

        private void ReevaluateLeakage()
        {
            // Below the critical threshold the dam leaks: we mirror the
            // Throttling Valve's pattern (see Valve.ApplyCurrentOutflowLimit
            // in Timberborn.WaterBuildings):
            //   1. RemoveFullObstacle on the dam's coordinate, so the cell
            //      stops being a wall.
            //   2. SetInflowLimit to a metered rate, so the engine pushes
            //      water through at the throttled flow.
            // When health recovers we reverse: AddFullObstacle restores the
            // dam, RemoveInflowLimit drops the meter. The dam's own
            // WaterObstacle (added by FinishableWaterObstacle when the dam
            // entered the finished state) lives in the same map slot, so
            // toggling it via IWaterService is the same operation the engine
            // would do on deconstruct/finish.
            //
            // Enabled by default per spec (EnableLeakage=true). Set to false
            // on a per-blueprint basis to opt out. When enabled, leakage ramps
            // up as health drops: 0 at the critical threshold, full at 0 HP.
            if (_spec == null || _blockObject == null || !_spec.EnableLeakage)
            {
                StopLeaking();
                return;
            }
            if (!_inCritical)
            {
                StopLeaking();
                return;
            }
            float t = 1f - Mathf.Clamp01(HealthFraction / _spec.CriticalHealthFraction);
            float leakRate = _spec.MaxLeakRatePerTick * t;
            StartOrUpdateLeaking(leakRate);
        }

        private void StartOrUpdateLeaking(float limit)
        {
            // Step 1: open the wall the first time we cross into leaking.
            if (!_isLeaking)
            {
                try
                {
                    _waterService.RemoveFullObstacle(_blockObject.Coordinates);
                    _isLeaking = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[DamDegradation] Could not open obstacle at {Coordinates}: {ex.Message}");
                    return;
                }
            }
            // Step 2: meter the flow (skip if unchanged to avoid churn).
            if (Mathf.Approximately(limit, _appliedInflowLimit))
            {
                return;
            }
            try
            {
                _waterService.SetInflowLimit(_blockObject.Coordinates, limit);
                _appliedInflowLimit = limit;
                _hasInflowLimit = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DamDegradation] Could not set inflow limit at {Coordinates}: {ex.Message}");
            }
        }

        private void StopLeaking()
        {
            ClearInflowLimitIfApplied();
            // Restore the wall: re-add the full obstacle on the dam's cell.
            // The dam's WaterObstacle component already considers itself
            // "added" so it won't try to manage this slot — we own the
            // toggle while leakage is the active driver.
            if (_isLeaking)
            {
                try
                {
                    _waterService.AddFullObstacle(_blockObject.Coordinates);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[DamDegradation] Could not restore obstacle at {Coordinates}: {ex.Message}");
                }
                _isLeaking = false;
            }
        }

        private void ClearInflowLimitIfApplied()
        {
            if (!_hasInflowLimit)
            {
                return;
            }
            try
            {
                _waterService.RemoveInflowLimit(_blockObject.Coordinates);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DamDegradation] Could not clear inflow limit at {Coordinates}: {ex.Message}");
            }
            _hasInflowLimit = false;
            _appliedInflowLimit = -1f;
        }

        private void Breach(bool triggerHorizontalCascade)
        {
            Debug.Log($"[DamDegradation] Structural failure at {Coordinates}");
            NotifyListeners(l => l.OnBreached(this, Coordinates));
            PostBreachNotification();
            CascadeToNeighbours(triggerHorizontalCascade);
            UnregisterFromRepairs();
            // Deleting the entity unregisters the WaterObstacle automatically and
            // lets the vanilla water sim breach the wall (see WaterObstacleController).
            _entityService.Delete(this);
        }

        private void PostBreachNotification()
        {
            try
            {
                _notificationBus.Post(_loc.T(BreachNotificationKey), this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DamDegradation] Could not post breach notification: {ex.Message}");
            }
        }

        private void CascadeToNeighbours(bool triggerHorizontalCascade)
        {
            if (_blockObject == null || _spec == null)
            {
                return;
            }
            var origin = _blockObject.Coordinates;

            // Horizontal cascade: water pressure damages the side neighbours.
            // They may breach in turn (with their own column collapses), but
            // we don't recurse into more horizontal cascades, otherwise a single
            // breach could chain across the entire wall in one tick.
            if (triggerHorizontalCascade && _spec.CascadeDamage > 0f)
            {
                Vector3Int[] horizontals =
                {
                    Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
                };
                foreach (var offset in horizontals)
                {
                    var neighbour = _blockService
                        .GetFirstObjectWithComponentAt<DamDeterioration>(origin + offset);
                    if (neighbour != null && neighbour != this)
                    {
                        neighbour.ApplyCascadeDamage(_spec.CascadeDamage);
                    }
                }
            }

            // Vertical column collapse: the dam blocks directly above this one
            // have just lost their support and the water beneath them. They
            // come down as a unit. Always runs, even when this breach was itself
            // caused by a horizontal cascade — a falling block always drops
            // whatever it was holding up.
            for (int dz = 1; ; dz++)
            {
                var probe = origin + new Vector3Int(0, 0, dz);
                var above = _blockService
                    .GetFirstObjectWithComponentAt<DamDeterioration>(probe);
                if (above == null || above == this)
                {
                    break;
                }
                above.CollapseImmediately();
            }
        }

        /// <summary>
        /// Force-deletes this dam block without applying further cascade or
        /// posting another breach notification. Used by the column-collapse
        /// path when the support below has already failed.
        /// </summary>
        internal void CollapseImmediately()
        {
            Debug.Log($"[DamDegradation] Column collapse at {Coordinates}");
            NotifyListeners(l => l.OnBreached(this, Coordinates));
            UnregisterFromRepairs();
            _entityService.Delete(this);
        }

        private void NotifyListeners(Action<IDamDeteriorationListener> action)
        {
            if (_listenersCache == null)
            {
                _listenersCache = GetComponentsAllocating<IDamDeteriorationListener>();
            }
            foreach (var listener in _listenersCache)
            {
                try { action(listener); }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[DamDegradation] Listener {listener.GetType().Name} threw: {ex.Message}");
                }
            }
        }
    }
}

