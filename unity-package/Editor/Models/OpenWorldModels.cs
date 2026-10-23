// ============================================================
// Append the contents of this file to the bottom of Models.cs
// (just before the closing brace of the namespace block).
// ============================================================

    // -----------------------------------------------------------------------
    // Group 3 — Terrain
    // -----------------------------------------------------------------------

    using Newtonsoft.Json;

    public class QueryTerrainRequest
    {
        [JsonProperty("positions")]             public float[][]  Positions           { get; set; }
        [JsonProperty("include_layer_weights")] public bool       IncludeLayerWeights { get; set; } = true;
    }

    public class TerrainQueryResultData
    {
        [JsonProperty("results")] public TerrainSampleData[] Results { get; set; }
    }

    public class TerrainSampleData
    {
        [JsonProperty("position")]       public float[]                Position      { get; set; }
        [JsonProperty("surface_height",  NullValueHandling = NullValueHandling.Include)]
                                         public float?                 SurfaceHeight { get; set; }
        [JsonProperty("normal",          NullValueHandling = NullValueHandling.Include)]
                                         public float[]                Normal        { get; set; }
        [JsonProperty("slope_degrees",   NullValueHandling = NullValueHandling.Include)]
                                         public float?                 SlopeDegrees  { get; set; }
        [JsonProperty("terrain_name",    NullValueHandling = NullValueHandling.Include)]
                                         public string                 TerrainName   { get; set; }
        [JsonProperty("layer_weights",   NullValueHandling = NullValueHandling.Include)]
                                         public TerrainLayerWeightData[] LayerWeights { get; set; }
        [JsonProperty("error",           NullValueHandling = NullValueHandling.Include)]
                                         public string                 Error         { get; set; }
    }

    public class TerrainLayerWeightData
    {
        [JsonProperty("layer_name")] public string LayerName { get; set; }
        [JsonProperty("weight")]     public float  Weight    { get; set; }
    }

    // -----------------------------------------------------------------------
    // Group 3 — NavMesh
    // -----------------------------------------------------------------------

    public class BakeNavMeshRequest
    {
        [JsonProperty("surface_paths")]   public string[] SurfacePaths  { get; set; }
        [JsonProperty("all")]             public bool     All           { get; set; }
        [JsonProperty("agent_type_name")] public string   AgentTypeName { get; set; }
    }

    public class BakeNavMeshResultData
    {
        [JsonProperty("baked_count")] public int                    BakedCount { get; set; }
        [JsonProperty("surfaces")]    public NavMeshBakeResultItem[] Surfaces   { get; set; }
        [JsonProperty("warnings")]    public string[]               Warnings   { get; set; }
    }

    public class NavMeshBakeResultItem
    {
        [JsonProperty("object_path")] public string ObjectPath { get; set; }
        [JsonProperty("agent_type")]  public string AgentType  { get; set; }
        [JsonProperty("elapsed_ms")]  public long   ElapsedMs  { get; set; }
    }

    public class SetNavMeshAreaRequest
    {
        [JsonProperty("object_paths")]  public string[]          ObjectPaths  { get; set; }
        [JsonProperty("area_name")]     public string            AreaName     { get; set; }
        [JsonProperty("override_area")] public bool              OverrideArea { get; set; } = true;
        [JsonProperty("volume")]        public NavMeshVolumeData Volume       { get; set; }
    }

    public class NavMeshVolumeData
    {
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("size")]        public float[] Size       { get; set; }
        [JsonProperty("rotation")]    public float[] Rotation   { get; set; }
        [JsonProperty("name")]        public string  Name       { get; set; }
        [JsonProperty("parent_path")] public string  ParentPath { get; set; }
    }

    public class SetNavMeshAreaResultData
    {
        [JsonProperty("updated_count")]   public int                      UpdatedCount   { get; set; }
        [JsonProperty("applied")]         public NavMeshAreaAppliedData[] Applied        { get; set; }
        [JsonProperty("volumes_created")] public NavMeshAreaAppliedData[] VolumesCreated { get; set; }
        [JsonProperty("warnings")]        public string[]                 Warnings       { get; set; }
    }

    public class NavMeshAreaAppliedData
    {
        [JsonProperty("path")]       public string Path      { get; set; }
        [JsonProperty("mode")]       public string Mode      { get; set; }
        [JsonProperty("area")]       public string Area      { get; set; }
        [JsonProperty("area_index")] public int    AreaIndex { get; set; }
    }

    // -----------------------------------------------------------------------
    // Group 3 — Scene Management
    // -----------------------------------------------------------------------

    public class ManageSceneRequest
    {
        [JsonProperty("operation")]         public string   Operation        { get; set; }
        [JsonProperty("scene_path")]        public string   ScenePath        { get; set; }
        [JsonProperty("set_active")]        public bool?    SetActive        { get; set; }
        [JsonProperty("object_paths")]      public string[] ObjectPaths      { get; set; }
        [JsonProperty("target_scene_name")] public string   TargetSceneName  { get; set; }
        [JsonProperty("save_after_create")] public bool?    SaveAfterCreate  { get; set; }
    }

    public class SceneListData
    {
        [JsonProperty("scenes")] public SceneInfoData[] Scenes { get; set; }
    }

    public class SceneInfoData
    {
        [JsonProperty("name")]      public string Name     { get; set; }
        [JsonProperty("path")]      public string Path     { get; set; }
        [JsonProperty("is_active")] public bool   IsActive { get; set; }
        [JsonProperty("is_dirty")]  public bool   IsDirty  { get; set; }
    }

    public class SceneLoadData
    {
        [JsonProperty("scene_name")] public string SceneName { get; set; }
        [JsonProperty("scene_path")] public string ScenePath { get; set; }
        [JsonProperty("loaded")]     public bool   Loaded    { get; set; }
        [JsonProperty("is_active")]  public bool   IsActive  { get; set; }
    }

    public class SceneMoveObjectsData
    {
        [JsonProperty("moved_count")] public int               MovedCount { get; set; }
        [JsonProperty("moved")]       public MovedObjectData[] Moved      { get; set; }
        [JsonProperty("warnings")]    public string[]          Warnings   { get; set; }
    }

    public class MovedObjectData
    {
        [JsonProperty("path")]       public string Path      { get; set; }
        [JsonProperty("from_scene")] public string FromScene { get; set; }
        [JsonProperty("to_scene")]   public string ToScene   { get; set; }
    }
