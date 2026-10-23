// -----------------------------------------------------------------------
// Foundation Phase 1 — append inside namespace UnityMcpBridge.Editor.Core
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // find_objects
    // -----------------------------------------------------------------------

    public class FindObjectsRequest
    {
        /// <summary>Exact Unity tag, e.g. "Interactable". At least one filter required.</summary>
        [JsonProperty("tag")]               public string   Tag             { get; set; }
        /// <summary>Component type name from get_component_catalog, e.g. "Light".</summary>
        [JsonProperty("component_type")]    public string   ComponentType   { get; set; }
        /// <summary>Glob name pattern. Supports * (any chars) and ? (one char), e.g. "Barrel*".</summary>
        [JsonProperty("name_pattern")]      public string   NamePattern     { get; set; }
        /// <summary>Unity layer name, e.g. "Environment".</summary>
        [JsonProperty("layer")]             public string   Layer           { get; set; }
        /// <summary>World-space centre of a sphere query [x, y, z].</summary>
        [JsonProperty("near_position")]     public float[]  NearPosition    { get; set; }
        /// <summary>Sphere radius in metres. Default 20.</summary>
        [JsonProperty("near_radius")]       public float    NearRadius      { get; set; } = 20f;
        /// <summary>Include inactive GameObjects. Default false.</summary>
        [JsonProperty("include_inactive")]  public bool     IncludeInactive { get; set; }
        /// <summary>Maximum results to return. Default 100.</summary>
        [JsonProperty("max_results")]       public int      MaxResults      { get; set; } = 100;
    }

    public class FindObjectsResultData
    {
        [JsonProperty("objects")] public FindObjectData[] Objects { get; set; }
        [JsonProperty("count")]   public int              Count   { get; set; }
    }

    public class FindObjectData
    {
        [JsonProperty("name")]       public string   Name       { get; set; }
        [JsonProperty("path")]       public string   Path       { get; set; }
        [JsonProperty("position")]   public float[]  Position   { get; set; }
        [JsonProperty("rotation")]   public float[]  Rotation   { get; set; }
        [JsonProperty("scale")]      public float[]  Scale      { get; set; }
        [JsonProperty("tag")]        public string   Tag        { get; set; }
        [JsonProperty("layer")]      public string   Layer      { get; set; }
        [JsonProperty("is_active")]  public bool     IsActive   { get; set; }
        [JsonProperty("components")] public string[] Components { get; set; }
    }

    // -----------------------------------------------------------------------
    // get_object_properties
    // -----------------------------------------------------------------------

    public class GetObjectPropertiesRequest
    {
        [JsonProperty("object_path")]    public string   ObjectPath    { get; set; }
        [JsonProperty("component_type")] public string   ComponentType { get; set; }
        /// <summary>Specific property paths to read. If null, returns all top-level properties.</summary>
        [JsonProperty("property_paths")] public string[] PropertyPaths { get; set; }
    }

    public class GetObjectPropertiesResultData
    {
        [JsonProperty("object_path")]    public string               ObjectPath    { get; set; }
        [JsonProperty("component_type")] public string               ComponentType { get; set; }
        [JsonProperty("properties")]     public ObjectPropertyData[] Properties    { get; set; }
    }

    public class ObjectPropertyData
    {
        [JsonProperty("property_path")] public string  PropertyPath { get; set; }
        [JsonProperty("value")]         public JToken  Value        { get; set; }
        /// <summary>Friendly type string, e.g. "Float", "Color", "Enum:LightType[...]".</summary>
        [JsonProperty("type")]          public string  Type         { get; set; }
    }

    // -----------------------------------------------------------------------
    // set_object_properties
    // -----------------------------------------------------------------------

    public class SetObjectPropertiesRequest
    {
        [JsonProperty("object_path")]      public string                        ObjectPath      { get; set; }
        [JsonProperty("component_type")]   public string                        ComponentType   { get; set; }
        [JsonProperty("properties")]       public List<ComponentPropertyValue>  Properties      { get; set; }
        /// <summary>Add the component if it isn't already present. Default false.</summary>
        [JsonProperty("create_if_missing")] public bool                         CreateIfMissing { get; set; }
    }

    public class SetObjectPropertiesResultData
    {
        [JsonProperty("object_path")]          public string   ObjectPath          { get; set; }
        [JsonProperty("component_type")]       public string   ComponentType       { get; set; }
        [JsonProperty("applied_properties")]   public string[] AppliedProperties   { get; set; }
        [JsonProperty("failed_properties")]    public string[] FailedProperties    { get; set; }
        [JsonProperty("warnings")]             public string[] Warnings            { get; set; }
    }

    // -----------------------------------------------------------------------
    // set_object_metadata
    // -----------------------------------------------------------------------

    public class SetObjectMetadataRequest
    {
        /// <summary>One or more hierarchy paths. All fields apply to every object.</summary>
        [JsonProperty("object_paths")] public string[]  ObjectPaths { get; set; }
        /// <summary>New name. For bulk: first object gets the name, subsequent get _02, _03 suffix.</summary>
        [JsonProperty("name")]         public string    Name        { get; set; }
        [JsonProperty("tag")]          public string    Tag         { get; set; }
        [JsonProperty("layer")]        public string    Layer       { get; set; }
        /// <summary>
        /// Static flag names: LightmapStatic, OccluderStatic, BatchingStatic,
        /// NavigationStatic, OccludeeStatic, ReflectionProbeStatic, OffMeshLinkGeneration.
        /// Pass ["All"] to set all flags. Pass ["None"] to clear all flags.
        /// </summary>
        [JsonProperty("static_flags")] public string[]  StaticFlags { get; set; }
        [JsonProperty("is_active")]    public bool?     IsActive    { get; set; }
    }

    public class SetObjectMetadataResultData
    {
        [JsonProperty("updated_count")] public int      UpdatedCount { get; set; }
        [JsonProperty("applied")]       public string[] Applied      { get; set; }
        [JsonProperty("warnings")]      public string[] Warnings     { get; set; }
    }

    // -----------------------------------------------------------------------
    // reparent_objects
    // -----------------------------------------------------------------------

    public class ReparentObjectsRequest
    {
        [JsonProperty("object_paths")]       public string[] ObjectPaths       { get; set; }
        /// <summary>New parent hierarchy path. Null or empty = move to scene root.</summary>
        [JsonProperty("new_parent_path")]    public string   NewParentPath     { get; set; }
        /// <summary>Preserve world-space transform when reparenting. Default true.</summary>
        [JsonProperty("keep_world_position")] public bool   KeepWorldPosition { get; set; } = true;
    }

    public class ReparentObjectsResultData
    {
        [JsonProperty("reparented_count")] public int      ReparentedCount { get; set; }
        [JsonProperty("warnings")]         public string[] Warnings        { get; set; }
    }

    // -----------------------------------------------------------------------
    // duplicate_objects
    // -----------------------------------------------------------------------

    public class DuplicateObjectsRequest
    {
        [JsonProperty("object_paths")]    public string[] ObjectPaths    { get; set; }
        /// <summary>Copies to make per source object. Default 1.</summary>
        [JsonProperty("count")]           public int      Count          { get; set; } = 1;
        /// <summary>World-space offset applied cumulatively to each successive copy.</summary>
        [JsonProperty("position_offset")] public float[]  PositionOffset { get; set; }
        /// <summary>Euler angle offset applied cumulatively to each successive copy.</summary>
        [JsonProperty("rotation_offset")] public float[]  RotationOffset { get; set; }
        /// <summary>Parent for all duplicates. Uses source object's parent if omitted.</summary>
        [JsonProperty("parent_path")]     public string   ParentPath     { get; set; }
        /// <summary>Suffix appended to the source name. Defaults to _Copy, _Copy_2, etc.</summary>
        [JsonProperty("name_suffix")]     public string   NameSuffix     { get; set; }
    }

    public class DuplicateObjectsResultData
    {
        [JsonProperty("created_count")] public int      CreatedCount { get; set; }
        [JsonProperty("paths")]         public string[] Paths        { get; set; }
        [JsonProperty("warnings")]      public string[] Warnings     { get; set; }
    }
}
