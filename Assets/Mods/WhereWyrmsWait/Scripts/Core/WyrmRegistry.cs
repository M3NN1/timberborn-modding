using System;
using System.Collections.Generic;
using Mods.WhereWyrmsWait.Wyrm;

namespace Mods.WhereWyrmsWait.Core
{
    /// <summary>
    /// Tracks all live Wyrms in the game. Used by other systems to ask
    /// "is there a wyrm near this beaver?" without scanning the entire
    /// entity list. Kept deliberately simple — register on Wyrm spawn,
    /// unregister on Wyrm delete.
    /// <para>
    /// Not persisted: the population is reconstructed on PostInitialize
    /// when each <see cref="WyrmComponent"/> registers itself after load.
    /// </para>
    /// <para>
    /// Raises <see cref="WyrmRegistered"/> and <see cref="WyrmUnregistered"/>
    /// so subscribers (e.g. a den that wants to count its own wyrms) can
    /// maintain a derived counter incrementally rather than re-scanning
    /// the whole list per tick.
    /// </para>
    /// </summary>
    public class WyrmRegistry
    {
        private readonly HashSet<WyrmComponent> _liveWyrms = new HashSet<WyrmComponent>();

        public IReadOnlyCollection<WyrmComponent> LiveWyrms => _liveWyrms;
        public int LiveCount => _liveWyrms.Count;

        public event EventHandler<WyrmComponent> WyrmRegistered;
        public event EventHandler<WyrmComponent> WyrmUnregistered;

        public void Register(WyrmComponent wyrm)
        {
            if (wyrm == null) return;
            if (_liveWyrms.Add(wyrm))
            {
                WyrmRegistered?.Invoke(this, wyrm);
            }
        }

        public void Unregister(WyrmComponent wyrm)
        {
            if (wyrm == null) return;
            if (_liveWyrms.Remove(wyrm))
            {
                WyrmUnregistered?.Invoke(this, wyrm);
            }
        }
    }
}
