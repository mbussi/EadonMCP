using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    public class SceneHierarchyData
    {
        [JsonProperty("scene_name")]   public string            SceneName   { get; set; }
        [JsonProperty("scene_path")]   public string            ScenePath   { get; set; }
        [JsonProperty("object_count")] public int               ObjectCount { get; set; }
        [JsonProperty("root_objects")] public SceneObjectData[] RootObjects { get; set; }
    }

    public class SceneObjectData
    {
        [JsonProperty("name")]       public string            Name       { get; set; }
        [JsonProperty("path")]       public string            Path       { get; set; }
        [JsonProperty("active")]     public bool              Active     { get; set; }
        [JsonProperty("position")]   public float[]           Position   { get; set; }
        [JsonProperty("rotation")]   public float[]           Rotation   { get; set; }
        [JsonProperty("scale")]      public float[]           Scale      { get; set; }
        [JsonProperty("components")] public string[]          Components { get; set; }
        [JsonProperty("children")]   public SceneObjectData[] Children   { get; set; }
    }

    public class SceneContextData
    {
        [JsonProperty("level_name")]          public string   LevelName          { get; set; }
        [JsonProperty("setting")]             public string   Setting            { get; set; }
        [JsonProperty("architectural_style")] public string   ArchitecturalStyle { get; set; }
        [JsonProperty("era")]                 public string   Era                { get; set; }
        [JsonProperty("active_tags")]         public string[] ActiveTags         { get; set; }
        [JsonProperty("excluded_tags")]       public string[] ExcludedTags       { get; set; }
        [JsonProperty("notes")]               public string   Notes              { get; set; }
    }

    public class PrefabCatalogData
    {
        [JsonProperty("prefabs")]       public PrefabData[] Prefabs      { get; set; }
        [JsonProperty("total_count")]   public int          TotalCount   { get; set; }
        [JsonProperty("with_metadata")] public int          WithMetadata { get; set; }
    }

    public class PrefabData
    {
        [JsonProperty("asset_path")]                                                       public string             AssetPath        { get; set; }
        [JsonProperty("display_name")]                                                     public string             DisplayName      { get; set; }
        [JsonProperty("has_metadata")]                                                     public bool               HasMetadata      { get; set; }
        [JsonProperty("category",          NullValueHandling = NullValueHandling.Ignore)] public string             Category         { get; set; }
        [JsonProperty("tags",              NullValueHandling = NullValueHandling.Ignore)] public string[]           Tags             { get; set; }
        [JsonProperty("description",       NullValueHandling = NullValueHandling.Ignore)] public string             Description      { get; set; }
        [JsonProperty("compatible_with",   NullValueHandling = NullValueHandling.Ignore)] public string[]           CompatibleWith   { get; set; }
        [JsonProperty("incompatible_with", NullValueHandling = NullValueHandling.Ignore)] public string[]           IncompatibleWith { get; set; }
        [JsonProperty("approximate_size",  NullValueHandling = NullValueHandling.Ignore)] public float[]            ApproximateSize  { get; set; }
        [JsonProperty("placement",         NullValueHandling = NullValueHandling.Ignore)] public PlacementRulesData Placement        { get; set; }
    }

    public class PlacementRulesData
    {
        [JsonProperty("requires_flat_ground")] public bool    RequiresFlatGround { get; set; }
        [JsonProperty("can_stack")]            public bool    CanStack           { get; set; }
        [JsonProperty("indoor_only")]          public bool    IndoorOnly         { get; set; }
        [JsonProperty("outdoor_only")]         public bool    OutdoorOnly        { get; set; }
        [JsonProperty("minimum_spacing")]      public float   MinimumSpacing     { get; set; }
        [JsonProperty("recommended_spacing")]  public float[] RecommendedSpacing { get; set; }
    }
}