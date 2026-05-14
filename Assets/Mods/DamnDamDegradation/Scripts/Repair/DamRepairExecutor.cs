using Mods.DamDegradation.Components;
using Mods.DamDegradation.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.CharacterModelSystem;
using Timberborn.Persistence;
using Timberborn.TimeSystem;
using Timberborn.WorkSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Lives on the beaver. Counts down the repair time, plays the building
    /// animation, and on success calls <see cref="DamDeterioration.Repair()"/>.
    /// Mirrors the vanilla <c>BuildExecutor</c> pattern.
    /// </summary>
    public class DamRepairExecutor : BaseComponent, IAwakableComponent, IExecutor
    {
        private static readonly ComponentKey SaveKey = new ComponentKey("DamRepairExecutor");
        private static readonly PropertyKey<float> FinishTimestampKey =
            new PropertyKey<float>("FinishTimestamp");

        private readonly IDayNightCycle _dayNightCycle;
        private readonly DamSettings _settings;

        private CharacterAnimator _animator;
        private DamRepairBehavior _behavior;
        private Worker _worker;
        private DamDeterioration _target;
        private float _finishTimestamp;

        public DamRepairExecutor(IDayNightCycle dayNightCycle, DamSettings settings)
        {
            _dayNightCycle = dayNightCycle;
            _settings = settings;
        }

        public void Awake()
        {
            _animator = GetComponent<CharacterAnimator>();
            _behavior = GetComponent<DamRepairBehavior>();
            _worker = GetComponent<Worker>();
        }

        public ExecutorStatus Tick(float deltaTimeInHours)
        {
            if (_target == null || _target.GameObject == null)
            {
                FinishWith(ExecutorStatus.Failure);
                return ExecutorStatus.Failure;
            }
            if (_dayNightCycle.PartialDayNumber >= _finishTimestamp)
            {
                _target.Repair();
                FinishWith(ExecutorStatus.Success);
                return ExecutorStatus.Success;
            }
            return ExecutorStatus.Running;
        }

        public bool Launch(DamDeterioration target)
        {
            if (target == null || target.GameObject == null)
            {
                return false;
            }
            _target = target;
            // Productivity bonuses (Inventor, Innovator's Outlet, etc.) speed
            // the worker up by dividing the required hours upfront. Vanilla
            // WorkAtReservableExecutor uses the same trick.
            float speed = _worker != null ? Mathf.Max(0.01f, _worker.WorkingSpeedMultiplier) : 1f;
            float hours = _settings.RepairTimeInHours / speed;
            _finishTimestamp = _dayNightCycle.DayNumberHoursFromNow(hours);
            _animator?.SetBool("Building", true);
            return true;
        }

        /// <summary>
        /// Called by <see cref="DamRepairBehavior.PostInitializeEntity"/>
        /// after a save load to re-attach the executor to the dam it was
        /// already working on. Re-uses the persisted finish timestamp so the
        /// repair finishes when it would have without the save/load.
        /// </summary>
        internal void InitializeAfterLoad(DamDeterioration target)
        {
            if (target == null || target.GameObject == null || _finishTimestamp <= 0f)
            {
                return;
            }
            _target = target;
            _animator?.SetBool("Building", true);
        }

        public void Save(IEntitySaver entitySaver)
        {
            entitySaver.GetComponent(SaveKey).Set(FinishTimestampKey, _finishTimestamp);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(SaveKey, out var loader)
                && loader.Has(FinishTimestampKey))
            {
                _finishTimestamp = loader.Get(FinishTimestampKey);
            }
        }

        private void StopAnimation()
        {
            _animator?.SetBool("Building", false);
        }

        private void FinishWith(ExecutorStatus status)
        {
            StopAnimation();
            _target = null;
            _behavior?.ReleaseReservation();
        }
    }
}
