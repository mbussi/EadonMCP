// -----------------------------------------------------------------------
// Cutscene Phase 1 — append inside namespace UnityMcpBridge.Editor.Core
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // GET /assets/cutscene/templates
    // -----------------------------------------------------------------------

    public class CutsceneTemplateListData
    {
        [JsonProperty("cinemachine_version")] public string                 CinemachineVersion { get; set; }
        [JsonProperty("timeline_version")]    public string                 TimelineVersion    { get; set; }
        [JsonProperty("templates")]           public CutsceneTemplateInfo[] Templates          { get; set; }
        /// <summary>Populated only when Cinemachine is not installed.</summary>
        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
                                              public string                 Message            { get; set; }
    }

    public class CutsceneTemplateInfo
    {
        [JsonProperty("name")]        public string                    Name        { get; set; }
        [JsonProperty("description")] public string                    Description { get; set; }
        [JsonProperty("parameters")]  public CutsceneParameterInfo[]   Parameters  { get; set; }
    }

    public class CutsceneParameterInfo
    {
        [JsonProperty("name")]        public string   Name        { get; set; }
        [JsonProperty("type")]        public string   Type        { get; set; }
        [JsonProperty("required")]    public bool     Required    { get; set; }
        [JsonProperty("default",      NullValueHandling = NullValueHandling.Ignore)]
                                      public string   Default     { get; set; }
        [JsonProperty("description",  NullValueHandling = NullValueHandling.Ignore)]
                                      public string   Description { get; set; }
        [JsonProperty("options",      NullValueHandling = NullValueHandling.Ignore)]
                                      public string[] Options     { get; set; }
        [JsonProperty("min_count",    NullValueHandling = NullValueHandling.Ignore)]
                                      public int?     MinCount    { get; set; }
    }

    // -----------------------------------------------------------------------
    // POST /tools/cutscene/create_sequence
    // -----------------------------------------------------------------------

    public class CreateSequenceRequest
    {
        /// <summary>Template name, e.g. "DialogueCamera".</summary>
        [JsonProperty("template")]      public string  Template     { get; set; }

        /// <summary>Name for the scene container and asset file, e.g. "DialogueCamera_MerchantA".</summary>
        [JsonProperty("sequence_name")] public string  SequenceName { get; set; }

        /// <summary>
        /// Full asset path for the .playable file.
        /// Defaults to "Assets/Cutscenes/{sequence_name}.playable" if omitted.
        /// </summary>
        [JsonProperty("timeline_path")] public string  TimelinePath { get; set; }

        /// <summary>
        /// Template-specific parameters as a JSON object.
        /// Shape varies by template — see get_cutscene_templates for the schema.
        /// </summary>
        [JsonProperty("params")]        public JObject Params       { get; set; }
    }

    public class CreateSequenceResultData
    {
        [JsonProperty("sequence_name")]           public string   SequenceName          { get; set; }
        [JsonProperty("container_path")]          public string   ContainerPath         { get; set; }
        [JsonProperty("timeline_path")]           public string   TimelinePath          { get; set; }
        [JsonProperty("vcam_paths")]              public string[] VcamPaths             { get; set; }
        [JsonProperty("signals_created")]         public string[] SignalsCreated        { get; set; }
        /// <summary>
        /// False when no CinemachineBrain was found on any camera in the scene.
        /// The agent should warn the developer to add one to their main camera.
        /// </summary>
        [JsonProperty("cinemachine_brain_found")] public bool     CinemachineBrainFound { get; set; }
        [JsonProperty("warnings")]                public string[] Warnings              { get; set; }
    }
    
    // -----------------------------------------------------------------------
    // preview_sequence
    // -----------------------------------------------------------------------

    public class PreviewSequenceRequest
    {
        /// <summary>Full hierarchy path of the sequence container, e.g.
        /// "/Cutscenes/DialogueCamera_MerchantA".</summary>
        [JsonProperty("container_path")] public string ContainerPath { get; set; }
    }

    public class PreviewSequenceResultData
    {
        [JsonProperty("container_path")] public string ContainerPath { get; set; }
        [JsonProperty("duration")]       public float  Duration      { get; set; }
        [JsonProperty("message")]        public string Message       { get; set; }
    }

    // -----------------------------------------------------------------------
    // update_sequence
    // -----------------------------------------------------------------------

    public class UpdateSequenceRequest
    {
        /// <summary>Full hierarchy path of the sequence container to modify.</summary>
        [JsonProperty("container_path")] public string  ContainerPath { get; set; }

        /// <summary>
        /// Template name — tells the handler which child objects and signal names
        /// to look for. Must match the template the sequence was created with.
        /// </summary>
        [JsonProperty("template")]       public string  Template      { get; set; }

        /// <summary>
        /// Parameter overrides in the same format as create_cutscene_sequence.params.
        /// Only the fields you include are changed; all others are left untouched.
        /// </summary>
        [JsonProperty("params")]         public JObject Params        { get; set; }
    }

    public class UpdateSequenceResultData
    {
        [JsonProperty("container_path")] public string   ContainerPath { get; set; }
        [JsonProperty("updated_fields")] public string[] UpdatedFields { get; set; }
        [JsonProperty("warnings")]       public string[] Warnings      { get; set; }
    }
}
