// -----------------------------------------------------------------------
// Dressing Phase 3 — append inside namespace UnityMcpBridge.Editor.Core
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using System.Collections.Generic;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // create_trigger_volume
    // -----------------------------------------------------------------------

    public class CreateTriggerVolumeRequest
    {
        /// <summary>GameObject name, e.g. "DialogueTrigger_Elder".</summary>
        [JsonProperty("name")]       public string   Name     { get; set; }
        /// <summary>"box" | "sphere" | "capsule"</summary>
        [JsonProperty("shape")]      public string   Shape    { get; set; }
        [JsonProperty("position")]   public float[]  Position { get; set; }
        /// <summary>Euler angles. Default [0,0,0].</summary>
        [JsonProperty("rotation")]   public float[]  Rotation { get; set; }
        /// <summary>
        /// Box:     [width, height, depth]
        /// Sphere:  [radius]  (or use radius field)
        /// Capsule: [radius, height]  (or use radius + height fields)
        /// </summary>
        [JsonProperty("size")]    public float[]  Size   { get; set; }
        [JsonProperty("radius")]  public float?   Radius { get; set; }
        [JsonProperty("height")]  public float?   Height { get; set; }
        [JsonProperty("tag")]     public string   Tag    { get; set; }
        [JsonProperty("layer")]   public string   Layer  { get; set; }
        /// <summary>typeName of a MonoBehaviour to add, e.g. "DialogueTrigger".</summary>
        [JsonProperty("script_component")]   public string                       ScriptComponent   { get; set; }
        /// <summary>Properties to set on the added script component.</summary>
        [JsonProperty("script_properties")]  public List<ComponentPropertyValue> ScriptProperties  { get; set; }
        [JsonProperty("parent_path")]        public string                       ParentPath        { get; set; }
    }

    public class CreateTriggerVolumeResultData
    {
        [JsonProperty("object_path")]    public string   ObjectPath    { get; set; }
        [JsonProperty("shape")]          public string   Shape         { get; set; }
        /// <summary>Effective collider dimensions: box=[w,h,d], sphere=[r], capsule=[r,h].</summary>
        [JsonProperty("effective_size")] public float[]  EffectiveSize { get; set; }
        [JsonProperty("tag")]            public string   Tag           { get; set; }
        [JsonProperty("script_added")]   public string   ScriptAdded   { get; set; }
        [JsonProperty("warnings")]       public string[] Warnings      { get; set; }
    }

    // -----------------------------------------------------------------------
    // apply_material
    // -----------------------------------------------------------------------

    public class ApplyMaterialRequest
    {
        [JsonProperty("object_paths")]   public string[] ObjectPaths   { get; set; }
        /// <summary>Exact asset name, e.g. "Wood_Dark_Worn".</summary>
        [JsonProperty("material_name")]  public string   MaterialName  { get; set; }
        /// <summary>Full asset path, e.g. "Assets/Materials/Props/Wood_Dark_Worn.mat".</summary>
        [JsonProperty("material_path")]  public string   MaterialPath  { get; set; }
        /// <summary>Search for materials whose name contains this string (case-insensitive).</summary>
        [JsonProperty("material_tag")]   public string   MaterialTag   { get; set; }
        /// <summary>
        /// When true and multiple candidates found via material_tag, assigns a different
        /// random material from the pool to each object. Default false (all get the same).
        /// </summary>
        [JsonProperty("random_from_tag")] public bool    RandomFromTag { get; set; }
        /// <summary>MeshRenderer submesh slot to replace. Default 0.</summary>
        [JsonProperty("material_slot")]  public int      MaterialSlot  { get; set; }
    }

    public class MaterialAssignment
    {
        [JsonProperty("path")]     public string Path     { get; set; }
        [JsonProperty("material")] public string Material { get; set; }
    }

    public class ApplyMaterialResultData
    {
        [JsonProperty("applied_count")] public int                  AppliedCount { get; set; }
        [JsonProperty("assignments")]   public MaterialAssignment[] Assignments  { get; set; }
        [JsonProperty("warnings")]      public string[]             Warnings     { get; set; }
    }
}
