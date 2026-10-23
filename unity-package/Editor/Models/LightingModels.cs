using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------

    public class CreateLightRequest
    {
        [JsonProperty("light_type")]  public string  LightType  { get; set; }
        [JsonProperty("position")]    public float[] Position   { get; set; }
        [JsonProperty("rotation")]    public float[] Rotation   { get; set; }
        [JsonProperty("color")]       public float[] Color      { get; set; }
        [JsonProperty("intensity")]   public float?  Intensity  { get; set; }
        [JsonProperty("range")]       public float?  Range      { get; set; }
        [JsonProperty("spot_angle")]  public float?  SpotAngle  { get; set; }
        [JsonProperty("shadows")]     public string  Shadows    { get; set; }
        [JsonProperty("name")]        public string  Name       { get; set; }
        [JsonProperty("parent_path")] public string  ParentPath { get; set; }
        [JsonProperty("dry_run")]     public bool    DryRun     { get; set; }
    }

    public class SetAmbientLightRequest
    {
        [JsonProperty("mode")]          public string  Mode         { get; set; }
        [JsonProperty("color")]         public float[] Color        { get; set; }
        [JsonProperty("sky_color")]     public float[] SkyColor     { get; set; }
        [JsonProperty("equator_color")] public float[] EquatorColor { get; set; }
        [JsonProperty("ground_color")]  public float[] GroundColor  { get; set; }
        [JsonProperty("intensity")]     public float?  Intensity    { get; set; }
        [JsonProperty("dry_run")]       public bool    DryRun       { get; set; }
    }

    public class SetSkyboxRequest
    {
        [JsonProperty("material_path")] public string MaterialPath { get; set; }
        [JsonProperty("dry_run")]       public bool   DryRun       { get; set; }
    }

    public class SetFogRequest
    {
        [JsonProperty("enabled")] public bool    Enabled { get; set; }
        [JsonProperty("color")]   public float[] Color   { get; set; }
        [JsonProperty("mode")]    public string  Mode    { get; set; }
        [JsonProperty("density")] public float?  Density { get; set; }
        [JsonProperty("start")]   public float?  Start   { get; set; }
        [JsonProperty("end")]     public float?  End     { get; set; }
        [JsonProperty("dry_run")] public bool    DryRun  { get; set; }
    }

    // -----------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------

    public class CreatedLightData
    {
        [JsonProperty("instance_path")] public string  InstancePath { get; set; }
        [JsonProperty("display_name")]  public string  DisplayName  { get; set; }
        [JsonProperty("light_type")]    public string  LightType    { get; set; }
        [JsonProperty("position")]      public float[] Position     { get; set; }
        [JsonProperty("rotation")]      public float[] Rotation     { get; set; }
        [JsonProperty("color")]         public float[] Color        { get; set; }
        [JsonProperty("intensity")]     public float   Intensity    { get; set; }
        [JsonProperty("dry_run")]       public bool    DryRun       { get; set; }
    }
}