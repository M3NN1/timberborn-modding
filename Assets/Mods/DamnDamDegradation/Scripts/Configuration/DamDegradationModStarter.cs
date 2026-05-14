using Timberborn.ModManagerScene;
using UnityEngine;

namespace Mods.DamDegradation.Configuration
{
    /// <summary>
    /// Entry point invoked by Timberborn when the mod's DLL is loaded.
    /// </summary>
    public class DamDegradationModStarter : IModStarter
    {
        public void StartMod(IModEnvironment modEnvironment)
        {
            Debug.Log("[DamDegradation] Loaded. Hydro-Stress is now active.");
        }
    }
}
