namespace LevelScaffold
{
    /// <summary>
    /// Gizmo visibility flags read by ScaffoldZone, ScaffoldAnchor, and GameplayMarker
    /// in their OnDrawGizmos methods and written by ScaffoldOverlay in the Editor assembly.
    ///
    /// Kept in the Runtime assembly so that Runtime MonoBehaviours can read them without
    /// creating a dependency on the Editor assembly.
    /// </summary>
    public static class ScaffoldGizmoState
    {
        public static bool ShowZones   = true;
        public static bool ShowAnchors = true;
        public static bool ShowMarkers = true;
        public static bool ShowRadii   = true;
        public static bool ShowLabels  = true;
    }
}
