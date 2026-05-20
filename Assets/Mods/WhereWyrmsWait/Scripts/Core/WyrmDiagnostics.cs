using System.Collections.Generic;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Core
{
    /// <summary>
    /// Tiny diagnostics helper for "log this warning the first time
    /// anyone hits this path, then stay quiet so the console doesn't
    /// drown in spam." Backed by a process-static set of seen tags.
    /// <para>
    /// Used to surface defensive null-guards (e.g. <c>_settings == null</c>)
    /// without flooding the log every tick — if Bindito ever fails to
    /// inject WyrmSettings the developer wants to see one warning, not
    /// thousands.
    /// </para>
    /// </summary>
    internal static class WyrmDiagnostics
    {
        private static readonly HashSet<string> SeenTags = new HashSet<string>();

        public static void LogOnce(string tag, string message)
        {
            if (!SeenTags.Add(tag)) return;
            Debug.LogWarning($"[WhereWyrmsWait] {message}");
        }
    }
}
