using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------

    public class PlacePrefabRequest
    {
        [JsonProperty("prefab_path")] public string  PrefabPath { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("rotation")]    public float[] Rotation   { get; set; }
        [JsonProperty("scale")]       public float[] Scale      { get; set; }
        [JsonProperty("parent_path")] public string  ParentPath { get; set; }
        [JsonProperty("dry_run")]     public bool    DryRun     { get; set; }
    }

    public class SetTransformRequest
    {
        [JsonProperty("object_path")] public string  ObjectPath { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("rotation")]    public float[] Rotation   { get; set; }
        [JsonProperty("scale")]       public float[] Scale      { get; set; }
        [JsonProperty("dry_run")]     public bool    DryRun     { get; set; }
    }

    public class DeleteObjectRequest
    {
        [JsonProperty("object_path")] public string ObjectPath { get; set; }
        [JsonProperty("dry_run")]     public bool   DryRun     { get; set; }
    }

    public class CreateEmptyRequest
    {
        [JsonProperty("name")]        public string  Name       { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("parent_path")] public string  ParentPath { get; set; }
        [JsonProperty("dry_run")]     public bool    DryRun     { get; set; }
    }

    // -----------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------

    public class PlacedObjectData
    {
        [JsonProperty("instance_path")] public string  InstancePath { get; set; }
        [JsonProperty("display_name")]  public string  DisplayName  { get; set; }
        [JsonProperty("position")]      public float[] Position     { get; set; }
        [JsonProperty("rotation")]      public float[] Rotation     { get; set; }
        [JsonProperty("scale")]         public float[] Scale        { get; set; }
        [JsonProperty("dry_run")]       public bool    DryRun       { get; set; }
    }

    public class TransformResultData
    {
        [JsonProperty("object_path")]  public string  ObjectPath  { get; set; }
        [JsonProperty("display_name")] public string  DisplayName { get; set; }
        [JsonProperty("position")]     public float[] Position    { get; set; }
        [JsonProperty("rotation")]     public float[] Rotation    { get; set; }
        [JsonProperty("scale")]        public float[] Scale       { get; set; }
        [JsonProperty("dry_run")]      public bool    DryRun      { get; set; }
    }

    public class DeleteResultData
    {
        [JsonProperty("object_path")]  public string ObjectPath  { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
        [JsonProperty("dry_run")]      public bool   DryRun      { get; set; }
    }
}