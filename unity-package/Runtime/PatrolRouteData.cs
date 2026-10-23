using UnityEngine;

namespace UnityMcpBridge
{
    /// <summary>
    /// Placed on patrol route container GameObjects by create_patrol_route. Carries the
    /// design intent (loop flag, default idle time, zone association) and is visible and
    /// editable in the Inspector.
    ///
    /// It lives in the runtime assembly (UnityMcpBridge.Runtime), so it is included in
    /// player builds and your AI can read it directly. Each waypoint child carries a
    /// <see cref="PatrolWaypoint"/>.
    ///
    /// The naming convention used by PatrolRouteHandler:
    ///   Container: "PatrolRoute_{name}"
    ///   Waypoints: "WP_{index:D2}" or "WP_{index:D2}_{label}"
    /// </summary>
    [AddComponentMenu("Eadon/MCP Bridge/Patrol Route Data")]
    public class PatrolRouteData : MonoBehaviour
    {
        [Tooltip("Whether the route loops from the last waypoint back to the first.")]
        public bool loop = true;

        [Tooltip("Default idle time in seconds at each waypoint. " +
                 "Individual waypoints can override this via PatrolWaypoint.idleTime.")]
        public float defaultIdleTime = 1f;

        [Tooltip("Hierarchy path of the ScaffoldZone this route belongs to. " +
                 "Set automatically when zone_path is provided to create_patrol_route.")]
        public string zonePath = string.Empty;
    }
}
