using Timberborn.ModManagerScene;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Configuration
{
    /// <summary>
    /// Entry point invoked by Timberborn when the mod's DLL is loaded.
    /// Phase 1 just logs that the mod is alive; later phases hook into
    /// <see cref="IModEnvironment"/> for asset paths if needed.
    /// </summary>
    public class WyrmsModStarter : IModStarter
    {
        public void StartMod(IModEnvironment modEnvironment)
        {
            Debug.Log("[WhereWyrmsWait] Loaded. Wyrms are listening.");
        }
    }
}
