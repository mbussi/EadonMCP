using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 3 — create_patrol_route / get_patrol_routes
    // -----------------------------------------------------------------------

    public class CreatePatrolRouteRequest
    {
        [JsonProperty("name")]              public string       Name           { get; set; }
        [JsonProperty("waypoints")]         public WaypointDef[] Waypoints     { get; set; }

        /// <summary>Loop from last waypoint to first. Default true.</summary>
        [JsonProperty("loop")]              public bool?        Loop           { get; set; }

        /// <summary>Seconds to idle at waypoints that don't specify their own. Default 1.0.</summary>
        [JsonProperty("default_idle_time")] public float        DefaultIdleTime{ get; set; } = 1f;

        /// <summary>
        /// Hierarchy path of the ScaffoldZone to parent this route under.
        /// Takes priority over parent_path when both are provided.
        /// </summary>
        [JsonProperty("zone_path")]         public string       ZonePath       { get; set; }

        [JsonProperty("parent_path")]       public string       ParentPath     { get; set; }
    }

    public class WaypointDef
    {
        [JsonProperty("position")]       public float[] Position      { get; set; }
        [JsonProperty("idle_time")]      public float?  IdleTime      { get; set; }

        /// <summary>Euler angles to face while idling. Null = face path direction.</summary>
        [JsonProperty("look_direction")] public float[] LookDirection { get; set; }

        /// <summary>Optional human-readable label, e.g. "east_gate_post".</summary>
        [JsonProperty("label")]          public string  Label         { get; set; }
    }

    public class CreatePatrolRouteResultData
    {
        [JsonProperty("route_path")]     public string             RoutePath     { get; set; }
        [JsonProperty("waypoint_count")] public int                WaypointCount { get; set; }
        [JsonProperty("loop")]           public bool               Loop          { get; set; }
        [JsonProperty("zone_path",   NullValueHandling = NullValueHandling.Ignore)]
                                         public string             ZonePath      { get; set; }
        [JsonProperty("waypoints")]      public WaypointResultItem[] Waypoints   { get; set; }
    }

    public class WaypointResultItem
    {
        [JsonProperty("path")]  public string Path  { get; set; }
        [JsonProperty("label",  NullValueHandling = NullValueHandling.Ignore)]
                                public string Label { get; set; }
    }

    public class PatrolRouteListData
    {
        [JsonProperty("routes")] public PatrolRouteResultItem[] Routes { get; set; }
    }

    public class PatrolRouteResultItem
    {
        [JsonProperty("route_path")]     public string            RoutePath     { get; set; }
        [JsonProperty("name")]           public string            Name          { get; set; }
        [JsonProperty("zone_path",   NullValueHandling = NullValueHandling.Ignore)]
                                         public string            ZonePath      { get; set; }
        [JsonProperty("loop")]           public bool              Loop          { get; set; }
        [JsonProperty("waypoint_count")] public int               WaypointCount { get; set; }
        [JsonProperty("waypoints")]      public WaypointDataItem[] Waypoints    { get; set; }
    }

    public class WaypointDataItem
    {
        [JsonProperty("path")]           public string   Path         { get; set; }
        [JsonProperty("index")]          public int      Index        { get; set; }
        [JsonProperty("position")]       public float[]  Position     { get; set; }
        [JsonProperty("idle_time")]      public float    IdleTime     { get; set; }
        [JsonProperty("label",        NullValueHandling = NullValueHandling.Ignore)]
                                         public string   Label        { get; set; }
        [JsonProperty("look_direction",NullValueHandling = NullValueHandling.Ignore)]
                                         public float[]  LookDirection{ get; set; }
    }
}