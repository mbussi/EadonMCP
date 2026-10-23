// -----------------------------------------------------------------------
// Dressing Phase 4 — append inside namespace UnityMcpBridge.Editor.Core
// -----------------------------------------------------------------------

using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // dress_area
    // -----------------------------------------------------------------------

    public class DressAreaRequest
    {
        /// <summary>World-space centre of the area to dress [x,y,z].</summary>
        [JsonProperty("center")]            public float[]  Center          { get; set; }
        /// <summary>Radius in metres.</summary>
        [JsonProperty("radius")]            public float    Radius          { get; set; }
        /// <summary>
        /// Natural-language description of desired dressing, e.g.
        /// "neglected market stall with crates, barrels, and a broken lantern".
        /// Tokens are matched against PrefabMetadata.tags, category, and displayName.
        /// </summary>
        [JsonProperty("intent")]            public string   Intent          { get; set; }
        /// <summary>"sparse" | "medium" | "dense". Default "medium".</summary>
        [JsonProperty("density")]           public string   Density         { get; set; } = "medium";
        /// <summary>Prefab tags to exclude from consideration.</summary>
        [JsonProperty("exclude_tags")]      public string[] ExcludeTags     { get; set; }
        /// <summary>If provided, only use prefabs that match at least one of these tags.</summary>
        [JsonProperty("include_tags")]      public string[] IncludeTags     { get; set; }
        /// <summary>Snap each candidate position to the surface below. Default true.</summary>
        [JsonProperty("snap_to_surfaces")]  public bool     SnapToSurfaces  { get; set; } = true;
        /// <summary>Skip positions that overlap existing scene colliders. Default true.</summary>
        [JsonProperty("avoid_existing")]    public bool     AvoidExisting   { get; set; } = true;
        /// <summary>Minimum clearance from existing objects in metres. Default 0.5.</summary>
        [JsonProperty("avoidance_radius")]  public float    AvoidanceRadius { get; set; } = 0.5f;
        /// <summary>Parent path for placed objects. If null, the proposal uses no parent.</summary>
        [JsonProperty("parent_path")]       public string   ParentPath      { get; set; }
        /// <summary>Name used in the proposal summary. Defaults to a position-derived name.</summary>
        [JsonProperty("group_name")]        public string   GroupName       { get; set; }
    }

    public class DressAreaResultData
    {
        /// <summary>Proposal ID — pass to execute_proposal when ready.</summary>
        [JsonProperty("proposal_id")]    public string   ProposalId    { get; set; }
        /// <summary>Human-readable summary of what was proposed.</summary>
        [JsonProperty("summary")]        public string   Summary       { get; set; }
        /// <summary>Number of placement operations in the proposal.</summary>
        [JsonProperty("proposed_count")] public int      ProposedCount { get; set; }
        /// <summary>Positions discarded due to avoidance checks.</summary>
        [JsonProperty("discarded_count")] public int     DiscardedCount{ get; set; }
        /// <summary>The group_name used in proposal descriptions.</summary>
        [JsonProperty("group_name")]     public string   GroupName     { get; set; }
        [JsonProperty("warnings")]       public string[] Warnings      { get; set; }
    }
}
