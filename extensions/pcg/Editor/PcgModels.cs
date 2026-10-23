using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------

    public class PcgValidateGraphRequest
    {
        [JsonProperty("pcgg_json")] public string PcggJson { get; set; }
    }

    public class PcgCreateGraphRequest
    {
        [JsonProperty("pcgg_json")]        public string               PcggJson        { get; set; }
        [JsonProperty("asset_path")]       public string               AssetPath       { get; set; }
        [JsonProperty("prefab_bindings")]  public PcgPrefabBindingData[] PrefabBindings { get; set; }
    }

    public class PcgPrefabBindingData
    {
        [JsonProperty("node_guid")] public string             NodeGuid { get; set; }
        [JsonProperty("prefabs")]   public PcgPrefabEntryData[] Prefabs { get; set; }
    }

    public class PcgPrefabEntryData
    {
        [JsonProperty("asset_guid")] public string AssetGuid { get; set; }
        [JsonProperty("weight")]     public float  Weight    { get; set; } = 1.0f;
    }

    // -----------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------

    public class PcgNodeCatalogData
    {
        [JsonProperty("nodes")] public PcgNodeInfoData[] Nodes { get; set; }
    }

    public class PcgNodeInfoData
    {
        [JsonProperty("canonical_name")]  public string             CanonicalName  { get; set; }
        [JsonProperty("unity_type_name")] public string             UnityTypeName  { get; set; }
        [JsonProperty("display_name")]    public string             DisplayName    { get; set; }
        [JsonProperty("category")]        public string             Category       { get; set; }
        [JsonProperty("tooltip")]         public string             Tooltip        { get; set; }
        [JsonProperty("node_class")]      public string             NodeClass      { get; set; }
        [JsonProperty("mutates_input")]   public bool               MutatesInput   { get; set; }
        [JsonProperty("inputs")]          public PcgPortInfoData[]  Inputs         { get; set; }
        [JsonProperty("outputs")]         public PcgPortInfoData[]  Outputs        { get; set; }
        [JsonProperty("parameters")]      public PcgParamInfoData[] Parameters     { get; set; }
    }

    public class PcgPortInfoData
    {
        [JsonProperty("field_name")]   public string FieldName   { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
        [JsonProperty("port_type")]    public string PortType    { get; set; }
    }

    public class PcgParamInfoData
    {
        [JsonProperty("field_name")]    public string FieldName    { get; set; }
        [JsonProperty("display_name")]  public string DisplayName  { get; set; }
        [JsonProperty("type")]          public string Type         { get; set; }
        [JsonProperty("default_value")] public string DefaultValue { get; set; }
        [JsonProperty("range_hint")]    public string RangeHint    { get; set; }
        [JsonProperty("tooltip")]       public string Tooltip      { get; set; }
    }

    public class PcgGraphListData
    {
        [JsonProperty("graphs")] public PcgGraphEntryData[] Graphs { get; set; }
    }

    public class PcgGraphEntryData
    {
        [JsonProperty("name")]            public string Name           { get; set; }
        [JsonProperty("asset_path")]      public string AssetPath      { get; set; }
        [JsonProperty("node_count")]      public int    NodeCount      { get; set; }
        [JsonProperty("edge_count")]      public int    EdgeCount      { get; set; }
        [JsonProperty("parameter_count")] public int    ParameterCount { get; set; }
    }

    public class PcgValidateResultData
    {
        [JsonProperty("valid")]    public bool     Valid    { get; set; }
        [JsonProperty("warnings")] public string[] Warnings { get; set; }
        [JsonProperty("errors")]   public string[] Errors   { get; set; }
    }

    public class PcgCreateResultData
    {
        [JsonProperty("asset_path")]    public string   AssetPath    { get; set; }
        [JsonProperty("nodes_created")] public int      NodesCreated { get; set; }
        [JsonProperty("edges_wired")]   public int      EdgesWired   { get; set; }
        [JsonProperty("warnings")]      public string[] Warnings     { get; set; }
        [JsonProperty("errors")]        public string[] Errors       { get; set; }
    }

    public class PcgPrefabSearchData
    {
        [JsonProperty("prefabs")] public PcgPrefabResultData[] Prefabs { get; set; }
    }

    public class PcgPrefabResultData
    {
        [JsonProperty("asset_guid")]       public string           AssetGuid       { get; set; }
        [JsonProperty("asset_path")]       public string           AssetPath       { get; set; }
        [JsonProperty("display_name")]     public string           DisplayName     { get; set; }
        [JsonProperty("category")]         public string           Category        { get; set; }
        [JsonProperty("tags")]             public string[]         Tags            { get; set; }
        [JsonProperty("description")]      public string           Description     { get; set; }
        [JsonProperty("approximate_size")] public float[]          ApproximateSize { get; set; }
        [JsonProperty("placement")]        public PlacementRulesData Placement     { get; set; }  // reuses existing type
        [JsonProperty("compatible_with")]  public string[]         CompatibleWith  { get; set; }
        [JsonProperty("incompatible_with")]public string[]         IncompatibleWith{ get; set; }
    }
    
    // ====================================================================== //
    //  Node catalog                                                           //
    // ====================================================================== //

    [Serializable]
    public class PcgNodeCatalogResponse
    {
        [JsonProperty("nodes")] public List<PcgNodeInfo> nodes = new List<PcgNodeInfo>();
    }

    [Serializable]
    public class PcgNodeInfo
    {
        [JsonProperty("canonical_name")]  public string          canonicalName;
        [JsonProperty("unity_type_name")] public string          unityTypeName;
        [JsonProperty("display_name")]    public string          displayName;
        [JsonProperty("category")]        public string          category;
        [JsonProperty("tooltip")]         public string          tooltip;
        [JsonProperty("node_class")]      public string          nodeClass;
        [JsonProperty("mutates_input")]   public bool            mutatesInput;
        [JsonProperty("inputs")]          public List<PcgPortInfo>  inputs     = new List<PcgPortInfo>();
        [JsonProperty("outputs")]         public List<PcgPortInfo>  outputs    = new List<PcgPortInfo>();
        [JsonProperty("parameters")]      public List<PcgParamInfo> parameters = new List<PcgParamInfo>();
    }

    [Serializable]
    public class PcgPortInfo
    {
        [JsonProperty("field_name")]   public string fieldName;
        [JsonProperty("display_name")] public string displayName;
        [JsonProperty("port_type")]    public string portType;
    }

    [Serializable]
    public class PcgParamInfo
    {
        [JsonProperty("field_name")]    public string fieldName;
        [JsonProperty("display_name")]  public string displayName;
        [JsonProperty("type")]          public string type;
        [JsonProperty("default_value")] public string defaultValue;
        [JsonProperty("range_hint")]    public string rangeHint;
        [JsonProperty("tooltip")]       public string tooltip;
    }

    // ====================================================================== //
    //  Graph list                                                             //
    // ====================================================================== //

    [Serializable]
    public class PcgGraphListResponse
    {
        [JsonProperty("graphs")] public List<PcgGraphEntry> graphs = new List<PcgGraphEntry>();
    }

    [Serializable]
    public class PcgGraphEntry
    {
        [JsonProperty("name")]            public string name;
        [JsonProperty("asset_path")]      public string assetPath;
        [JsonProperty("node_count")]      public int    nodeCount;
        [JsonProperty("edge_count")]      public int    edgeCount;
        [JsonProperty("parameter_count")] public int    parameterCount;
    }

    // ====================================================================== //
    //  Validate                                                               //
    // ====================================================================== //

    [Serializable]
    public class PcgValidateRequest
    {
        [JsonProperty("pcgg_json")] public string pcggJson;
    }

    [Serializable]
    public class PcgValidateResponse
    {
        [JsonProperty("valid")]    public bool         valid;
        [JsonProperty("warnings")] public List<string> warnings = new List<string>();
        [JsonProperty("errors")]   public List<string> errors   = new List<string>();
    }

    // ====================================================================== //
    //  Create                                                                 //
    // ====================================================================== //

    [Serializable]
    public class PcgCreateRequest
    {
        [JsonProperty("pcgg_json")]       public string               pcggJson;
        [JsonProperty("asset_path")]      public string               assetPath;
        [JsonProperty("prefab_bindings")] public List<PcgPrefabBinding> prefabBindings = new List<PcgPrefabBinding>();
    }

    [Serializable]
    public class PcgPrefabBinding
    {
        [JsonProperty("node_guid")] public string             nodeGuid;
        [JsonProperty("prefabs")]   public List<PcgPrefabEntry> prefabs = new List<PcgPrefabEntry>();

        /// <summary>Older docs said "nodeGuid"; accept it so agents using that spelling still work.</summary>
        [JsonProperty("nodeGuid")]
        private string NodeGuidCamelCase { set { if (string.IsNullOrEmpty(nodeGuid)) nodeGuid = value; } }
    }

    [Serializable]
    public class PcgPrefabEntry
    {
        [JsonProperty("asset_guid")] public string assetGuid;
        [JsonProperty("weight")]     public float  weight = 1.0f;

        /// <summary>Older docs said "assetGuid"; accept it so agents using that spelling still work.</summary>
        [JsonProperty("assetGuid")]
        private string AssetGuidCamelCase { set { if (string.IsNullOrEmpty(assetGuid)) assetGuid = value; } }
    }

    [Serializable]
    public class PcgCreateResponse
    {
        [JsonProperty("asset_path")]    public string       assetPath;
        [JsonProperty("nodes_created")] public int          nodesCreated;
        [JsonProperty("edges_wired")]   public int          edgesWired;
        [JsonProperty("warnings")]      public List<string> warnings = new List<string>();
        [JsonProperty("errors")]        public List<string> errors   = new List<string>();
    }

    // ====================================================================== //
    //  Prefab search                                                          //
    // ====================================================================== //

    [Serializable]
    public class PcgSearchPrefabsRequest
    {
        [JsonProperty("tags")]        public string[] tags;
        [JsonProperty("category")]    public string   category;
        [JsonProperty("max_results")] public int      maxResults = 20;
    }

    [Serializable]
    public class PcgPrefabSearchResponse
    {
        [JsonProperty("prefabs")] public List<PcgPrefabResult> prefabs = new List<PcgPrefabResult>();
    }

    [Serializable]
    public class PcgPrefabResult
    {
        [JsonProperty("asset_guid")]        public string            assetGuid;
        [JsonProperty("asset_path")]        public string            assetPath;
        [JsonProperty("display_name")]      public string            displayName;
        [JsonProperty("category")]          public string            category;
        [JsonProperty("tags")]              public string[]          tags;
        [JsonProperty("description")]       public string            description;
        [JsonProperty("approximate_size")]  public float[]           approximateSize;
        [JsonProperty("placement")]         public PlacementRulesData placement;    // already in Models.cs
        [JsonProperty("compatible_with")]   public string[]          compatibleWith;
        [JsonProperty("incompatible_with")] public string[]          incompatibleWith;
    }
    
}