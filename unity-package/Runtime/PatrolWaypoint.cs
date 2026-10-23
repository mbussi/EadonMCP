using UnityEngine;

namespace UnityMcpBridge
{
    /// <summary>
    /// Placed on each waypoint child of a patrol route by create_patrol_route.
    /// See <see cref="PatrolRouteData"/>.
    /// </summary>
    [AddComponentMenu("Eadon/MCP Bridge/Patrol Waypoint")]
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
