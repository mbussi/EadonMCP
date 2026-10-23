using UnityEngine;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Editor-only MonoBehaviour placed on patrol route container GameObjects.
    ///
    /// This component lives in the UnityMcpBridge.Editor assembly and is stripped
    /// from player builds. Its purpose is to carry design intent data (loop flag,
    /// default idle time, zone association) in a way that is visible and editable
    /// in the Inspector during level design sessions.
    ///
    /// ── RUNTIME INTEGRATION ─────────────────────────────────────────────────
    /// Your game's AI system cannot reference this component at runtime since it
    /// doesn't exist in builds. Two approaches for connecting patrol routes to your
    /// AI agents:
    ///
    ///   Option A — Naming convention (no extra work):
    ///     Your AI agent discovers routes at runtime by finding GameObjects named
    ///     "PatrolRoute_*" and reading their child WP_XX transform positions.
    ///     No coupling to this component required.
    ///
    ///   Option B — Replace with your own component:
    ///     Before building, run a tool (or write an Editor script) that replaces
    ///     PatrolRouteData with your game's own PatrolRoute component, copying the
    ///     relevant field values. The bridge's component is the design-time source
    ///     of truth; your runtime component is the build-time output.
    ///
    /// The naming convention used by PatrolRouteHandler:
    ///   Container: "PatrolRoute_{name}"
    ///   Waypoints: "WP_{index:D2}" or "WP_{index:D2}_{label}"
    /// </summary>
    [AddComponentMenu("MCP Bridge/Patrol Route Data")]
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

    /// <summary>
    /// Editor-only MonoBehaviour placed on each waypoint child of a patrol route.
    /// See <see cref="PatrolRouteData"/> for runtime integration notes.
    /// </summary>
    [AddComponentMenu("MCP Bridge/Patrol Waypoint")]
    public class PatrolWaypoint : MonoBehaviour
    {
        [Tooltip("Zero-based index of this waypoint along the route.")]
        public int index;

        [Tooltip("Seconds to idle at this waypoint. " +
                 "-1 means use the parent PatrolRouteData.defaultIdleTime.")]
        public float idleTime = -1f;

        [Tooltip("If true, the character faces lookDirection while idling here. " +
                 "If false, it faces the direction of travel.")]
        public bool hasLookDirection;

        [Tooltip("Euler angles to face while idling. Only used when hasLookDirection is true.")]
        public Vector3 lookDirection;

        [Tooltip("Optional human-readable label for this waypoint, " +
                 "e.g. 'east_gate_post', 'patrol_far_end'. " +
                 "Becomes part of the waypoint's GameObject name when set.")]
        public string label = string.Empty;
    }
}
