// Replaces the original ComponentMcpModels.cs.
// Two changes from the original:
//   1. Namespace changed to UnityMcpBridge.Editor.Core
//   2. [JsonProperty("snake_case")] added to every field

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // ====================================================================== //
    //  Catalog                                                                //
    // ====================================================================== //

    [Serializable]
    public class ComponentCatalogResponse
    {
        [JsonProperty("components")] public List<ComponentInfo> components = new List<ComponentInfo>();
    }

    [Serializable]
    public class ComponentInfo
    {
        [JsonProperty("type_name")]         public string                    typeName;
        [JsonProperty("display_name")]      public string                    displayName;
        [JsonProperty("category")]          public string                    category;
        [JsonProperty("source")]            public string                    source;
        [JsonProperty("script_path")]       public string                    scriptPath;
        [JsonProperty("disallow_multiple")] public bool                      disallowMultiple;
        [JsonProperty("properties")]        public List<ComponentPropertyInfo> properties = new List<ComponentPropertyInfo>();
    }

    [Serializable]
    public class ComponentPropertyInfo
    {
        [JsonProperty("property_path")]  public string   propertyPath;
        [JsonProperty("display_name")]   public string   displayName;
        [JsonProperty("type")]           public string   type;
        [JsonProperty("default_value")]  public string   defaultValue;
        [JsonProperty("enum_names")]     public string[] enumNames;
        [JsonProperty("depth")]          public int      depth;
    }

    // ====================================================================== //
    //  Add component                                                          //
    // ====================================================================== //

    [Serializable]
    public class AddComponentRequest
    {
        [JsonProperty("object_path")]         public string                      objectPath;
        [JsonProperty("component_type_name")] public string                      componentTypeName;
        [JsonProperty("properties")]          public List<ComponentPropertyValue> properties = new List<ComponentPropertyValue>();
        [JsonProperty("update_if_exists")]    public bool                        updateIfExists;
    }

    [Serializable]
    public class ComponentPropertyValue
    {
        [JsonProperty("property_path")] public string                       propertyPath;
        [JsonProperty("value")]         public Newtonsoft.Json.Linq.JToken  value;

        /// <summary>Older docs said "propertyPath"; accept it so agents using that spelling still work.</summary>
        [JsonProperty("propertyPath")]
        private string PropertyPathCamelCase { set { if (string.IsNullOrEmpty(propertyPath)) propertyPath = value; } }
    }

    [Serializable]
    public class AddComponentResponse
    {
        [JsonProperty("object_path")]          public string       objectPath;
        [JsonProperty("component_type_name")]  public string       componentTypeName;
        [JsonProperty("updated_existing")]     public bool         updatedExisting;
        [JsonProperty("applied_properties")]   public List<string> appliedProperties = new List<string>();
        [JsonProperty("failed_properties")]    public List<string> failedProperties  = new List<string>();
        [JsonProperty("warnings")]             public List<string> warnings          = new List<string>();
        [JsonProperty("errors")]               public List<string> errors            = new List<string>();
    }
    
    public class ComponentPropertyValueData
    {
        [JsonProperty("property_path")] public string PropertyPath { get; set; }
        // JToken accepts any JSON value: number, bool, string, array, object
        [JsonProperty("value")]         public JToken Value        { get; set; }
    }

    // -----------------------------------------------------------------------
    // Component — responses
    // -----------------------------------------------------------------------

    public class ComponentCatalogData
    {
        [JsonProperty("components")] public ComponentInfoData[] Components { get; set; }
    }

    public class ComponentInfoData
    {
        [JsonProperty("type_name")]         public string                   TypeName         { get; set; }
        [JsonProperty("display_name")]      public string                   DisplayName      { get; set; }
        [JsonProperty("category")]          public string                   Category         { get; set; }
        [JsonProperty("source")]            public string                   Source           { get; set; }
        [JsonProperty("script_path",        NullValueHandling = NullValueHandling.Ignore)]
                                            public string                   ScriptPath       { get; set; }
        [JsonProperty("disallow_multiple")] public bool                     DisallowMultiple { get; set; }
        [JsonProperty("properties")]        public ComponentPropertyInfoData[] Properties    { get; set; }
    }

    public class ComponentPropertyInfoData
    {
        [JsonProperty("property_path")]  public string   PropertyPath  { get; set; }
        [JsonProperty("display_name")]   public string   DisplayName   { get; set; }
        [JsonProperty("type")]           public string   Type          { get; set; }
        [JsonProperty("default_value")]  public string   DefaultValue  { get; set; }
        [JsonProperty("enum_names",      NullValueHandling = NullValueHandling.Ignore)]
                                         public string[] EnumNames     { get; set; }
        [JsonProperty("depth")]          public int      Depth         { get; set; }
    }

    public class AddComponentResultData
    {
        [JsonProperty("object_path")]         public string   ObjectPath        { get; set; }
        [JsonProperty("component_type_name")] public string   ComponentTypeName { get; set; }
        [JsonProperty("updated_existing")]    public bool     UpdatedExisting   { get; set; }
        [JsonProperty("applied_properties")] public string[] AppliedProperties { get; set; }
        [JsonProperty("failed_properties")]  public string[] FailedProperties  { get; set; }
        [JsonProperty("warnings")]           public string[] Warnings          { get; set; }
        [JsonProperty("errors")]             public string[] Errors            { get; set; }
    }
}
