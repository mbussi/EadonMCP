using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 2 — replace_prefab
    // -----------------------------------------------------------------------

    public class ReplacePrefabRequest
    {
        [JsonProperty("old_prefab_path")]     public string OldPrefabPath     { get; set; }
        [JsonProperty("new_prefab_path")]     public string NewPrefabPath     { get; set; }

        /// <summary>Hierarchy path to restrict the search. Null = whole active scene.</summary>
        [JsonProperty("scope_path")]          public string ScopePath         { get; set; }

        /// <summary>Keep original position/rotation/scale. Default true.</summary>
        [JsonProperty("preserve_transform")]  public bool?  PreserveTransform { get; set; }

        /// <summary>Keep original GameObject name. Default true.</summary>
        [JsonProperty("preserve_name")]       public bool?  PreserveName      { get; set; }

        /// <summary>Keep original hierarchy parent. Default true.</summary>
        [JsonProperty("preserve_parent")]     public bool?  PreserveParent    { get; set; }

        [JsonProperty("dry_run")]             public bool   DryRun            { get; set; }
    }

    public class ReplacePrefabResultData
    {
        [JsonProperty("replaced_count")] public int               ReplacedCount { get; set; }
        [JsonProperty("dry_run")]        public bool              DryRun        { get; set; }
        [JsonProperty("replaced")]       public ReplaceResultItem[] Replaced    { get; set; }
        [JsonProperty("warnings")]       public string[]           Warnings     { get; set; }
    }

    public class ReplaceResultItem
    {
        [JsonProperty("old_path")] public string OldPath { get; set; }
        [JsonProperty("new_path")] public string NewPath { get; set; }
    }
}