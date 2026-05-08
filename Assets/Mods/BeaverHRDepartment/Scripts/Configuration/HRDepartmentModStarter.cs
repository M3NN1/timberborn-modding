using Timberborn.ModManagerScene;
using UnityEngine;

namespace Mods.BeaverHRDepartment.Configuration {

  public class HRDepartmentModStarter : IModStarter {
    public void StartMod(IModEnvironment modEnvironment) {
      Debug.Log("[BHR] Beaver HR Department loaded! Your beavers now have a resume.");
    }
  }
}
