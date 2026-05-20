namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Common surface for things that warm up under irrigated soil and
    /// then spawn wyrms — currently <see cref="WyrmHusk"/> and
    /// <see cref="WyrmDen"/>. Lets the procedural surface-mound visual
    /// share one implementation across both. Public, but explicitly not
    /// an extension point — callers should still type-check before
    /// assuming behaviour beyond this interface.
    /// </summary>
    public interface IWyrmHazard
    {
        /// <summary>0–1 fraction of the way to the next wake/spawn.</summary>
        float WarmupFraction { get; }

        /// <summary>True iff the column above is currently irrigated.</summary>
        bool SurfaceIsGreen { get; }

        /// <summary>How many natural-ground blocks cover the hazard.</summary>
        int CoverDepth { get; }
    }
}
