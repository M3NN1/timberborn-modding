using Timberborn.BaseComponentSystem;
using Timberborn.Localization;
using Timberborn.NotificationSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Core
{
    /// <summary>
    /// Tiny wrapper around <see cref="NotificationBus"/> with mod-specific
    /// localization keys. Centralizing here means UI translation work is
    /// in one place and posters don't need to remember the keys.
    /// <para>
    /// Each notification is anchored to a <see cref="BaseComponent"/> with
    /// a vanilla <c>EntityComponent</c>; clicking the notification in the
    /// in-game feed pans the camera to that entity. Subjects that have
    /// already been deleted gracefully no-op.
    /// </para>
    /// </summary>
    public class WyrmNotifications
    {
        public const string WyrmEmergedKey = "WWW.Notification.WyrmEmerged";
        public const string WyrmDiedKey = "WWW.Notification.WyrmDied";
        public const string BeaverEatenKey = "WWW.Notification.BeaverEaten";
        public const string DenDestroyedKey = "WWW.Notification.DenDestroyed";

        private readonly NotificationBus _bus;
        private readonly ILoc _loc;

        public WyrmNotifications(NotificationBus bus, ILoc loc)
        {
            _bus = bus;
            _loc = loc;
        }

        public void Post(string locKey, BaseComponent subject)
        {
            if (subject == null || subject.GameObject == null) return;
            try
            {
                _bus.Post(_loc.T(locKey), subject);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not post notification {locKey}: " +
                    $"{ex.Message}");
            }
        }
    }
}
