using System;
using Mods.DamDegradation.Components;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.EntitySystem;
using Timberborn.Persistence;
using Timberborn.WalkingSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.DamDegradation.Repair
{
    /// <summary>
    /// Lives on the beaver. When activated by <see cref="DamRepairJobProvider"/>,
    /// reserves the dam, walks to a position computed by
    /// <see cref="DamApproach"/> (which uses the engine's
    /// <c>BlockObjectAccessGenerator</c>), and runs <see cref="DamRepairExecutor"/>
    /// to perform the work.
    /// </summary>
    public class DamRepairBehavior : Behavior, IAwakableComponent, IStartableComponent,
        IPersistentEntity, IPostInitializableEntity, IDeletableEntity
    {
        private static readonly ComponentKey SaveKey = new ComponentKey("DamRepairBehavior");
        private static readonly PropertyKey<DamDeterioration> ReservedDamKey =
            new PropertyKey<DamDeterioration>("ReservedDam");

        private readonly DamRepairRegistry _registry;
        private readonly ReferenceSerializer _referenceSerializer;

        private BehaviorAgent _behaviorAgent;
        private WalkToPositionExecutor _walkExecutor;
        private DamRepairExecutor _repairExecutor;

        private DamDeterioration _reservedDam;
        private DamRepairReservation _heldReservation;
        private DamDeterioration _loadedDam;

        public DamRepairBehavior(
            DamRepairRegistry registry,
            ReferenceSerializer referenceSerializer)
        {
            _registry = registry;
            _referenceSerializer = referenceSerializer;
        }

        public bool HasReservation => _reservedDam != null;
        public DamDeterioration ReservedDam => _reservedDam;

        public void Awake()
        {
            _behaviorAgent = GetComponent<BehaviorAgent>();
        }

        public void Start()
        {
            _walkExecutor = GetComponent<WalkToPositionExecutor>();
            _repairExecutor = GetComponent<DamRepairExecutor>();
        }

        public Decision StartRepair(DamDeterioration dam, DamRepairReservation reservation)
        {
            if (dam == null || reservation == null)
            {
                return Decision.ReleaseNow();
            }
            if (!reservation.TryReserve(this))
            {
                return Decision.ReleaseNow();
            }
            _reservedDam = dam;
            _heldReservation = reservation;
            return Decide(_behaviorAgent);
        }

        public override Decision Decide(BehaviorAgent agent)
        {
            if (!HasReservation)
            {
                return Decision.ReleaseNow();
            }

            // Sample with the beaver's current position so the access we
            // walk to matches the one the job provider ranked by.
            Vector3 here = Transform != null ? Transform.position : default;
            if (!DamApproach.TryGetApproachWorldPosition(_reservedDam, here, out var approach))
            {
                MarkUnreachableAndRelease();
                return Decision.ReleaseNow();
            }

            try
            {
                switch (_walkExecutor.Launch(approach))
                {
                    case ExecutorStatus.Success:
                        return BeginRepair();
                    case ExecutorStatus.Failure:
                        MarkUnreachableAndRelease();
                        return Decision.ReleaseNextTick();
                    case ExecutorStatus.Running:
                        return Decision.ReturnWhenFinished(_walkExecutor);
                    default:
                        MarkUnreachableAndRelease();
                        return Decision.ReleaseNow();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DamDegradation] Repair walk failed: {ex.Message}");
                MarkUnreachableAndRelease();
                return Decision.ReleaseNow();
            }
        }

        private Decision BeginRepair()
        {
            if (_reservedDam == null || _reservedDam.GameObject == null
                || !_repairExecutor.Launch(_reservedDam))
            {
                ReleaseReservation();
                return Decision.ReleaseNextTick();
            }
            // The executor releases the reservation itself when it finishes.
            return Decision.ReleaseWhenFinished(_repairExecutor);
        }

        private void MarkUnreachableAndRelease()
        {
            if (_reservedDam != null)
            {
                _registry.MarkUnreachable(_reservedDam);
            }
            ReleaseReservation();
        }

        internal void ReleaseReservation()
        {
            _heldReservation?.Release();
            _heldReservation = null;
            _reservedDam = null;
        }

        public void Save(IEntitySaver entitySaver)
        {
            if (_reservedDam == null || _reservedDam.GameObject == null)
            {
                return;
            }
            entitySaver
                .GetComponent(SaveKey)
                .Set(ReservedDamKey, _reservedDam, _referenceSerializer.Of<DamDeterioration>());
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(SaveKey, out var loader)
                && loader.GetObsoletable(
                    ReservedDamKey, _referenceSerializer.Of<DamDeterioration>(), out var dam))
            {
                _loadedDam = dam;
            }
        }

        public void PostInitializeEntity()
        {
            if (_loadedDam == null || _loadedDam.GameObject == null)
            {
                _loadedDam = null;
                return;
            }
            // Recover the reservation we held before the save. If another
            // beaver claimed it in the meantime (shouldn't happen on a clean
            // load, but games are messy), give up on this dam quietly.
            var reservation = _loadedDam.GetComponent<DamRepairReservation>();
            if (reservation != null && reservation.TryReserve(this))
            {
                _reservedDam = _loadedDam;
                _heldReservation = reservation;
                _repairExecutor?.InitializeAfterLoad(_reservedDam);
            }
            _loadedDam = null;
        }

        public void DeleteEntity()
        {
            ReleaseReservation();
        }
    }
}
