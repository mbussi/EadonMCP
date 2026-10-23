// -----------------------------------------------------------------------
// Dressing Phase 2 — append inside namespace UnityMcpBridge.Editor.Core
// -----------------------------------------------------------------------

using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // snap_to_surface
    // -----------------------------------------------------------------------

    public class SnapToSurfaceRequest
    {
        [JsonProperty("object_paths")]    public string[] ObjectPaths   { get; set; }
        /// <summary>Raycast direction. Default [0,-1,0] (straight down).</summary>
        [JsonProperty("cast_direction")]  public float[]  CastDirection { get; set; }
        /// <summary>Max raycast distance in metres. Default 100.</summary>
        [JsonProperty("max_distance")]    public float    MaxDistance   { get; set; } = 100f;
        /// <summary>Gap to leave above the hit surface. Default 0.</summary>
        [JsonProperty("surface_offset")]  public float    SurfaceOffset { get; set; }
        /// <summary>Rotate object's up-axis to match surface normal. Default false.</summary>
        [JsonProperty("align_to_normal")] public bool     AlignToNormal { get; set; }
        /// <summary>Unity layer bitmask to hit. Null = all layers.</summary>
        [JsonProperty("layer_mask")]      public int?     LayerMask     { get; set; }
    }

    public class SnapToSurfaceResultData
    {
        [JsonProperty("snapped_count")] public int               SnappedCount { get; set; }
        [JsonProperty("skipped")]       public SnapSkippedItem[] Skipped      { get; set; }
    }

    public class SnapSkippedItem
    {
        [JsonProperty("path")]   public string Path   { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
    }

    // -----------------------------------------------------------------------
    // align_and_distribute
    // -----------------------------------------------------------------------

    public class AlignAndDistributeRequest
    {
        [JsonProperty("object_paths")] public string[] ObjectPaths { get; set; }
        /// <summary>distribute_between | align_to_axis | stack | circle</summary>
        [JsonProperty("mode")]         public string   Mode        { get; set; }

        // ── distribute_between ──
        [JsonProperty("start_point")]  public float[]  StartPoint    { get; set; }
        [JsonProperty("end_point")]    public float[]  EndPoint      { get; set; }
        /// <summary>Rotate objects to face along the line. Default true.</summary>
        [JsonProperty("face_direction")] public bool   FaceDirection { get; set; } = true;

        // ── align_to_axis ──
        /// <summary>"x" | "y" | "z"</summary>
        [JsonProperty("axis")]         public string   Axis     { get; set; }
        [JsonProperty("value")]        public float?   Value    { get; set; }
        /// <summary>"min" | "center" | "max" — which bounds edge to align. Default "center".</summary>
        [JsonProperty("use_pivot")]    public string   UsePivot { get; set; } = "center";

        // ── stack ──
        /// <summary>"x" | "y" | "z". Default "y".</summary>
        [JsonProperty("stack_axis")]     public string  StackAxis    { get; set; } = "y";
        /// <summary>Bottom of the stack in world space. Uses first object's position if null.</summary>
        [JsonProperty("base_position")]  public float[] BasePosition { get; set; }

        // ── circle ──
        [JsonProperty("center")]              public float[] Center           { get; set; }
        [JsonProperty("radius")]              public float   Radius           { get; set; }
        [JsonProperty("facing_outward")]      public bool    FacingOutward    { get; set; } = true;
        [JsonProperty("start_angle_degrees")] public float   StartAngleDegrees{ get; set; }
    }

    public class AlignAndDistributeResultData
    {
        [JsonProperty("repositioned_count")] public int      RepositionedCount { get; set; }
        [JsonProperty("warnings")]           public string[] Warnings          { get; set; }
    }

    // -----------------------------------------------------------------------
    // randomize_transforms
    // -----------------------------------------------------------------------

    public class RandomizeTransformsRequest
    {
        [JsonProperty("object_paths")]  public string[] ObjectPaths   { get; set; }
        /// <summary>Random seed for reproducibility. If null a random seed is chosen.</summary>
        [JsonProperty("seed")]          public int?     Seed          { get; set; }
        /// <summary>Max position offset per axis [x,y,z] (symmetric ±). e.g. [0.3,0,0.3]</summary>
        [JsonProperty("position_range")] public float[] PositionRange { get; set; }
        /// <summary>Max rotation offset per axis in degrees. e.g. [0,180,0] = full random Y.</summary>
        [JsonProperty("rotation_range")] public float[] RotationRange { get; set; }
        /// <summary>[min,max] uniform scale multiplier. e.g. [0.85,1.15] for ±15%.</summary>
        [JsonProperty("scale_range")]   public float[]  ScaleRange    { get; set; }
        /// <summary>Snap each object to the surface below after randomizing. Default false.</summary>
        [JsonProperty("snap_after")]    public bool     SnapAfter     { get; set; }
    }

    public class RandomizeTransformsResultData
    {
        [JsonProperty("randomized_count")] public int      RandomizedCount { get; set; }
        [JsonProperty("seed_used")]        public int      SeedUsed        { get; set; }
        [JsonProperty("warnings")]         public string[] Warnings        { get; set; }
    }

    // -----------------------------------------------------------------------
    // group_objects
    // -----------------------------------------------------------------------

    public class GroupObjectsRequest
    {
        [JsonProperty("object_paths")] public string[] ObjectPaths { get; set; }
        [JsonProperty("group_name")]   public string   GroupName   { get; set; }
        /// <summary>Parent for the new container. Uses common ancestor if null.</summary>
        [JsonProperty("parent_path")]  public string   ParentPath  { get; set; }
        /// <summary>"center" (centroid) | "first" (first object's position) | "world_origin"</summary>
        [JsonProperty("pivot")]        public string   Pivot       { get; set; } = "center";
        [JsonProperty("tag")]          public string   Tag         { get; set; }
        [JsonProperty("layer")]        public string   Layer       { get; set; }
    }

    public class GroupObjectsResultData
    {
        [JsonProperty("container_path")] public string ContainerPath { get; set; }
        [JsonProperty("grouped_count")]  public int    GroupedCount  { get; set; }
        [JsonProperty("warnings")]       public string[] Warnings    { get; set; }
    }
}
