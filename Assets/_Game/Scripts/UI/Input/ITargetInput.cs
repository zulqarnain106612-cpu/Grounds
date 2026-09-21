namespace JetFighter.UI.Input
{
    /// <summary>
    /// The right hand's seam, declared and deliberately unimplemented.
    ///
    /// Phase 2 owns target reticle and missile lock (ADR-002). Naming the
    /// interface now costs nothing and keeps Phase 1 from growing a
    /// right-hand code path it would then have to unpick -- the joystick
    /// router depends on this type, not on whatever implements it.
    /// </summary>
    public interface ITargetInput
    {
        /// <summary>Reticle position in screen space, or null when untouched.</summary>
        UnityEngine.Vector2? ScreenTarget { get; }
    }
}
