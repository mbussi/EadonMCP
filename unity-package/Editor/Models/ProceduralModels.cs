using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------

    public class PlacePrefabBatchRequest
    {
        [JsonProperty("items")]   public BatchPlacementItem[] Items  { get; set; }
        [JsonProperty("dry_run")] public bool                 DryRun { get; set; }
    }

    public class BatchPlacementItem
    {
        [JsonProperty("prefab_path")] public string  PrefabPath { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("rotation")]    public float[] Rotation   { get; set; }
        [JsonProperty("scale")]       public float[] Scale      { get; set; }
        [JsonProperty("parent_path")] public string  ParentPath { get; set; }
    }

    public class ScatterPrefabsRequest
    {
        [JsonProperty("prefab_path")]      public string  PrefabPath      { get; set; }
        [JsonProperty("center")]           public float[] Center           { get; set; }
        [JsonProperty("radius")]           public float   Radius           { get; set; }
        [JsonProperty("count")]            public int     Count            { get; set; }
        [JsonProperty("min_spacing")]      public float   MinSpacing       { get; set; }
        [JsonProperty("align_to_surface")] public bool    AlignToSurface   { get; set; }
        [JsonProperty("parent_path")]      public string  ParentPath       { get; set; }
        [JsonProperty("seed")]             public int?    Seed             { get; set; }
        [JsonProperty("dry_run")]          public bool    DryRun           { get; set; }
    }

    public class PlaceAlongPathRequest
    {
        [JsonProperty("prefab_path")]   public string    PrefabPath   { get; set; }
        [JsonProperty("waypoints")]     public float[][] Waypoints    { get; set; }
        [JsonProperty("spacing")]       public float     Spacing      { get; set; }
        [JsonProperty("align_to_path")] public bool      AlignToPath  { get; set; }
        [JsonProperty("parent_path")]   public string    ParentPath   { get; set; }
        [JsonProperty("dry_run")]       public bool      DryRun       { get; set; }
    }

    public class FillGridRequest
    {
        [JsonProperty("prefab_path")]           public string  PrefabPath           { get; set; }
        [JsonProperty("origin")]                public float[] Origin               { get; set; }
        [JsonProperty("cell_size")]             public float[] CellSize             { get; set; }
        [JsonProperty("columns")]               public int     Columns              { get; set; }
        [JsonProperty("rows")]                  public int     Rows                 { get; set; }
        [JsonProperty("offset_alternate_rows")] public bool    OffsetAlternateRows  { get; set; }
        [JsonProperty("parent_path")]           public string  ParentPath           { get; set; }
        [JsonProperty("dry_run")]               public bool    DryRun               { get; set; }
    }

    // -----------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------

    public class BatchResultData
    {
        [JsonProperty("placed_count")]   public int            PlacedCount   { get; set; }
        [JsonProperty("failed_count")]   public int            FailedCount   { get; set; }
        [JsonProperty("instance_paths")] public string[]       InstancePaths { get; set; }
        [JsonProperty("failures",   NullValueHandling = NullValueHandling.Ignore)]
                                         public BatchFailure[] Failures      { get; set; }
        [JsonProperty("dry_run")]        public bool           DryRun        { get; set; }
    }

    public class BatchFailure
    {
        [JsonProperty("index")]   public int    Index   { get; set; }
        [JsonProperty("code")]    public string Code    { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    public class ProceduralResultData
    {
        [JsonProperty("placed_count")]   public int      PlacedCount   { get; set; }
        [JsonProperty("path_length",  NullValueHandling = NullValueHandling.Ignore)]
                                         public float?   PathLength    { get; set; }
        [JsonProperty("instance_paths")] public string[] InstancePaths { get; set; }
        [JsonProperty("dry_run")]        public bool     DryRun        { get; set; }
    }
}