using System.Collections.Generic;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Common surface for things that warm up under irrigated soil and
    /// then spawn wyrms — currently <see cref="WyrmHusk"/> and
    /// <see cref="WyrmDen"/>. Lets the procedural surface-mound visual
    /// share one implementation across both.
    /// </summary>
    public interface IWyrmHazard
    {
        /// <summary>0..1 fraction of the way to the next wake/spawn.</summary>
        float WarmupFraction { get; }

        /// <summary>True iff the column above is currently irrigated.</summary>
        bool SurfaceIsGreen { get; }

        /// <summary>How many natural-ground blocks cover the hazard.</summary>
        int CoverDepth { get; }

        /// <summary>
        /// Hazard-owned cells the emergence picker uses as probe
        /// origins. For a 1×1×1 hazard, the single cell. For a
        /// multi-block hazard (2×2×2 den), the topmost own-cell of
        /// each footprint column. Never empty for a placed hazard.
        /// </summary>
        IReadOnlyList<Vector3Int> EmergenceProbeCells { get; }
    }
}
