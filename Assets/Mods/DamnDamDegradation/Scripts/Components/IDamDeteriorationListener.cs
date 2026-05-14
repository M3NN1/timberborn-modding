using UnityEngine;

namespace Mods.DamDegradation.Components
{
    /// <summary>
    /// Extension point for visual / audio reactions to dam state changes.
    /// Components on the same dam entity that implement this are notified.
    /// Implement and decorate to drive model swaps, particles, sounds — without
    /// modifying the core <see cref="DamDeterioration"/> logic.
    /// </summary>
    public interface IDamDeteriorationListener
    {
        void OnEnterWarning(DamDeterioration dam);

        void OnExitWarning(DamDeterioration dam);

        void OnEnterCritical(DamDeterioration dam);

        void OnExitCritical(DamDeterioration dam);

        void OnRepaired(DamDeterioration dam, float amount);

        void OnBreached(DamDeterioration dam, Vector3Int coordinates);
    }
}
