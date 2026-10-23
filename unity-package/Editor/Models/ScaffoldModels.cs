using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Shared dimension DTO — mirrors LevelScaffold.ZoneDimensions
    // -----------------------------------------------------------------------

    public class ScaffoldDimensionsRequest
    {
        [JsonProperty("width")]        public float Width       { get; set; }
        [JsonProperty("depth")]        public float Depth       { get; set; }
        [JsonProperty("height")]       public float Height      { get; set; }
        [JsonProperty("diameter")]     public float Diameter    { get; set; }
        [JsonProperty("step_count")]   public int   StepCount   { get; set; }
        [JsonProperty("riser_height")] public float RiserHeight { get; set; }
        [JsonProperty("turns")]        public float Turns       { get; set; }
        [JsonProperty("radius")]       public float Radius      { get; set; }
    }

    // -----------------------------------------------------------------------
    // Query responses
    // -----------------------------------------------------------------------

    public class ScaffoldZoneListData
    {
        [JsonProperty("zones")] public ScaffoldZoneData[] Zones { get; set; }
        [JsonProperty("count")] public int                Count { get; set; }
    }

    public class ScaffoldZoneData
    {
        [JsonProperty("zone_name")]     public string   ZoneName     { get; set; }
        [JsonProperty("zone_path")]     public string   ZonePath     { get; set; }
        [JsonProperty("zone_type")]     public string   ZoneType     { get; set; }
        [JsonProperty("design_note")]   public string   DesignNote   { get; set; }
        [JsonProperty("dressing_tags")] public string[] DressingTags { get; set; }
        [JsonProperty("anchor_count")]  public int      AnchorCount  { get; set; }
        [JsonProperty("marker_count")]  public int      MarkerCount  { get; set; }
        [JsonProperty("position")]      public float[]  Position     { get; set; }
        [JsonProperty("rotation")]      public float[]  Rotation     { get; set; }
    }

    public class ScaffoldAnchorListData
    {
        [JsonProperty("anchors")] public ScaffoldAnchorData[] Anchors { get; set; }
        [JsonProperty("count")]   public int                  Count   { get; set; }
    }

    public class ScaffoldAnchorData
    {
        [JsonProperty("label")]       public string  Label      { get; set; }
        [JsonProperty("anchor_path")] public string  AnchorPath { get; set; }
        [JsonProperty("anchor_type")] public string  AnchorType { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("zone_path")]   public string  ZonePath   { get; set; }
    }

    public class ScaffoldMarkerListData
    {
        [JsonProperty("markers")] public ScaffoldMarkerData[] Markers { get; set; }
        [JsonProperty("count")]   public int                  Count   { get; set; }
    }

    public class ScaffoldMarkerData
    {
        [JsonProperty("label")]            public string   Label           { get; set; }
        [JsonProperty("marker_path")]      public string   MarkerPath      { get; set; }
        [JsonProperty("marker_type")]      public string   MarkerType      { get; set; }
        [JsonProperty("semantic_tags")]    public string[] SemanticTags    { get; set; }
        [JsonProperty("detection_radius")] public float    DetectionRadius { get; set; }
        [JsonProperty("bake_state")]       public string   BakeState       { get; set; }
        [JsonProperty("position")]         public float[]  Position        { get; set; }
        [JsonProperty("zone_path")]        public string   ZonePath        { get; set; }
    }

    // -----------------------------------------------------------------------
    // create_zone requests / responses
    // -----------------------------------------------------------------------

    public class CreateZoneRequest
    {
        [JsonProperty("zone_name")]      public string                    ZoneName      { get; set; }
        [JsonProperty("zone_type")]      public string                    ZoneType      { get; set; }
        [JsonProperty("dimensions")]     public ScaffoldDimensionsRequest Dimensions    { get; set; }
        [JsonProperty("entry_position")] public float[]                   EntryPosition { get; set; }
        [JsonProperty("entry_rotation")] public float[]                   EntryRotation { get; set; }
        [JsonProperty("parent_path")]    public string                    ParentPath    { get; set; }
        [JsonProperty("design_note")]    public string                    DesignNote    { get; set; }
        [JsonProperty("dressing_tags")]  public string[]                  DressingTags  { get; set; }
    }

    public class CreateZoneResultData
    {
        [JsonProperty("zone_path")]       public string   ZonePath      { get; set; }
        [JsonProperty("zone_type")]       public string   ZoneType      { get; set; }
        [JsonProperty("anchors_created")] public int      AnchorsCreated{ get; set; }
        [JsonProperty("anchor_paths")]    public string[] AnchorPaths   { get; set; }
    }

    // -----------------------------------------------------------------------
    // link_zones requests / responses
    // -----------------------------------------------------------------------

    public class LinkZonesRequest
    {
        [JsonProperty("from_zone_path")]    public string                    FromZonePath    { get; set; }
        [JsonProperty("to_zone_path")]      public string                    ToZonePath      { get; set; }
        [JsonProperty("transition_type")]   public string                    TransitionType  { get; set; }
        [JsonProperty("dimensions")]        public ScaffoldDimensionsRequest Dimensions      { get; set; }
        [JsonProperty("connection_label")]  public string                    ConnectionLabel { get; set; }
    }

    public class LinkZonesResultData
    {
        [JsonProperty("connection_path")] public string ConnectionPath { get; set; }
        [JsonProperty("from_anchor")]     public string FromAnchor     { get; set; }
        [JsonProperty("to_anchor")]       public string ToAnchor       { get; set; }
    }

    // -----------------------------------------------------------------------
    // create_reference_point requests / responses
    // -----------------------------------------------------------------------

    public class CreateReferencePointRequest
    {
        [JsonProperty("zone_path")]       public string  ZonePath       { get; set; }
        [JsonProperty("label")]           public string  Label          { get; set; }
        [JsonProperty("anchor_type")]     public string  AnchorType     { get; set; }
        // Provide world_position OR anchor_path + position_offset
        [JsonProperty("world_position")]  public float[] WorldPosition  { get; set; }
        [JsonProperty("anchor_path")]     public string  AnchorPath     { get; set; }
        [JsonProperty("position_offset")] public float[] PositionOffset { get; set; }
    }

    public class CreateReferencePointResultData
    {
        [JsonProperty("anchor_path")] public string  AnchorPath { get; set; }
        [JsonProperty("anchor_type")] public string  AnchorType { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
    }

    // -----------------------------------------------------------------------
    // create_marker requests / responses
    // -----------------------------------------------------------------------

    public class CreateMarkerRequest
    {
        [JsonProperty("marker_label")]     public string  MarkerLabel     { get; set; }
        [JsonProperty("marker_type")]      public string  MarkerType      { get; set; }
        [JsonProperty("semantic_tags")]    public string[] SemanticTags   { get; set; }
        // Provide anchor_path OR world_position
        [JsonProperty("anchor_path")]      public string  AnchorPath      { get; set; }
        [JsonProperty("world_position")]   public float[] WorldPosition   { get; set; }
        [JsonProperty("position_offset")]  public float[] PositionOffset  { get; set; }
        [JsonProperty("detection_radius")] public float   DetectionRadius { get; set; }
        // Deserialized as JObject → serialised back to pretty JSON for GameplayMarker.ConfigJson
        [JsonProperty("config")]           public JObject Config          { get; set; }
    }

    public class CreateMarkerResultData
    {
        [JsonProperty("marker_path")] public string  MarkerPath { get; set; }
        [JsonProperty("marker_type")] public string  MarkerType { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
    }

    // -----------------------------------------------------------------------
    // update_marker requests / responses
    // -----------------------------------------------------------------------

    public class UpdateMarkerRequest
    {
        [JsonProperty("marker_path")]      public string   MarkerPath      { get; set; }
        [JsonProperty("semantic_tags")]    public string[] SemanticTags    { get; set; }
        [JsonProperty("config")]           public JObject  Config          { get; set; }
        [JsonProperty("detection_radius")] public float?   DetectionRadius { get; set; }
    }

    public class UpdateMarkerResultData
    {
        [JsonProperty("marker_path")]     public string   MarkerPath     { get; set; }
        [JsonProperty("updated_fields")]  public string[] UpdatedFields  { get; set; }
    }
    
    // -----------------------------------------------------------------------
    // resolve_markers
    // -----------------------------------------------------------------------

    public class ResolveMarkersRequest
    {
        /// <summary>
        /// Optional. Restrict resolution to markers inside this zone.
        /// Must be a full hierarchy path, e.g. "/ForgottenTunnels/Zone1_Descent".
        /// If omitted, all Pending markers in the scene are resolved.
        /// </summary>
        [JsonProperty("zone_path")]     public string   ZonePath    { get; set; }

        /// <summary>
        /// Optional. Filter by GameplayMarkerType name(s).
        /// E.g. ["EnemySpawn", "LootContainer"]. If omitted, all types are included.
        /// </summary>
        [JsonProperty("marker_types")]  public string[] MarkerTypes { get; set; }

        /// <summary>Maximum ranked candidates to return per marker. Default 3.</summary>
        [JsonProperty("max_candidates_per_marker")] public int MaxCandidatesPerMarker { get; set; } = 3;
    }

    public class ResolveMarkersResultData
    {
        /// <summary>Markers for which at least one prefab candidate was found.</summary>
        [JsonProperty("proposals")]    public BakeProposal[]      Proposals    { get; set; }

        /// <summary>Markers for which no PrefabMetadata matched any semantic tag.</summary>
        [JsonProperty("unresolvable")] public UnresolvableMarker[] Unresolvable { get; set; }
    }

    public class BakeProposal
    {
        [JsonProperty("marker_path")]   public string        MarkerPath   { get; set; }
        [JsonProperty("marker_label")]  public string        MarkerLabel  { get; set; }
        [JsonProperty("marker_type")]   public string        MarkerType   { get; set; }
        [JsonProperty("semantic_tags")] public string[]      SemanticTags { get; set; }
        [JsonProperty("candidates")]    public BakeCandidate[] Candidates  { get; set; }
    }

    public class BakeCandidate
    {
        [JsonProperty("asset_guid")]          public string AssetGuid         { get; set; }
        [JsonProperty("asset_path")]          public string AssetPath         { get; set; }
        [JsonProperty("display_name")]        public string DisplayName       { get; set; }
        [JsonProperty("tag_overlap_count")]   public int    TagOverlapCount   { get; set; }
        [JsonProperty("scene_context_bonus")] public int    SceneContextBonus { get; set; }
        [JsonProperty("total_score")]         public int    TotalScore        { get; set; }
    }

    public class UnresolvableMarker
    {
        [JsonProperty("marker_path")]   public string   MarkerPath   { get; set; }
        [JsonProperty("marker_label")]  public string   MarkerLabel  { get; set; }
        [JsonProperty("semantic_tags")] public string[] SemanticTags { get; set; }
    }

    // -----------------------------------------------------------------------
    // execute_bake
    // -----------------------------------------------------------------------

    public class ExecuteBakeRequest
    {
        /// <summary>
        /// One entry per marker to bake. Each entry maps a marker (by path)
        /// to the prefab GUID chosen from the resolve_markers candidates.
        /// </summary>
        [JsonProperty("confirmations")]      public BakeConfirmation[] Confirmations     { get; set; }

        /// <summary>
        /// If true, set all zones containing a baked marker to inactive after
        /// instantiation. The scaffold data is preserved and can be restored
        /// with show_scaffold. Default false.
        /// </summary>
        [JsonProperty("hide_scaffold_after")] public bool HideScaffoldAfter             { get; set; }
    }

    public class BakeConfirmation
    {
        /// <summary>Full hierarchy path of the GameplayMarker, from get_scaffold_markers.</summary>
        [JsonProperty("marker_path")] public string MarkerPath { get; set; }

        /// <summary>AssetDatabase GUID of the chosen prefab, from resolve_markers candidates.</summary>
        [JsonProperty("asset_guid")]  public string AssetGuid  { get; set; }
    }

    public class ExecuteBakeResultData
    {
        [JsonProperty("baked_count")] public int            BakedCount { get; set; }
        [JsonProperty("instances")]   public BakedInstance[] Instances  { get; set; }
        [JsonProperty("warnings")]    public string[]        Warnings   { get; set; }
    }

    public class BakedInstance
    {
        [JsonProperty("marker_label")]  public string MarkerLabel  { get; set; }
        [JsonProperty("instance_path")] public string InstancePath { get; set; }
    }
}