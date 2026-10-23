// ============================================================
// Append the contents of this file to Models.cs,
// just before the closing brace of the namespace block.
// ============================================================

    // -----------------------------------------------------------------------
    // Project File Tools
    // -----------------------------------------------------------------------

    using Newtonsoft.Json;

    public class WriteProjectFileRequest
    {
        /// <summary>
        /// Path relative to the Assets folder, e.g. "Scripts/MyBehaviour.cs"
        /// or "Scripts/AI/PatrolAgent.cs". A leading "Assets/" is accepted and
        /// stripped automatically. Must not contain ".." segments.
        /// </summary>
        [JsonProperty("path")]                public string Path               { get; set; }

        /// <summary>Full text content to write.</summary>
        [JsonProperty("content")]             public string Content            { get; set; }

        /// <summary>Replace the file if it already exists. Default true.</summary>
        [JsonProperty("overwrite")]           public bool   Overwrite          { get; set; } = true;

        /// <summary>Create intermediate directories if they don't exist. Default true.</summary>
        [JsonProperty("create_directories")]  public bool   CreateDirectories  { get; set; } = true;

        /// <summary>
        /// Call AssetDatabase.ImportAsset immediately after writing.
        /// Default FALSE — leave this false and call refresh_assets separately
        /// after all files are written and the agent's final response has been sent.
        /// Setting this true will trigger a domain reload mid-conversation.
        /// </summary>
        [JsonProperty("import_after_write")]  public bool   ImportAfterWrite   { get; set; } = false;    }

    public class WriteProjectFileResultData
    {
        [JsonProperty("asset_path")]   public string AssetPath    { get; set; }
        [JsonProperty("created")]      public bool   Created      { get; set; }
        [JsonProperty("overwrote")]    public bool   Overwrote    { get; set; }
        [JsonProperty("bytes_written")]public int    BytesWritten { get; set; }
    }

    // -----------------------------------------------------------------------

    public class ReadProjectFileRequest
    {
        [JsonProperty("path")] public string Path { get; set; }
    }

    public class ReadProjectFileResultData
    {
        [JsonProperty("asset_path")]  public string AssetPath  { get; set; }
        [JsonProperty("exists")]      public bool   Exists     { get; set; }
        [JsonProperty("content",   NullValueHandling = NullValueHandling.Ignore)]
                                      public string Content    { get; set; }
        [JsonProperty("size_bytes",NullValueHandling = NullValueHandling.Ignore)]
                                      public int?   SizeBytes  { get; set; }
    }

    // -----------------------------------------------------------------------

    public class ListProjectFilesRequest
    {
        /// <summary>Directory path relative to Assets. Empty string = Assets root.</summary>
        [JsonProperty("path")]      public string Path      { get; set; } = "";

        /// <summary>File glob pattern, e.g. "*.cs", "*.prefab". Default "*".</summary>
        [JsonProperty("pattern")]   public string Pattern   { get; set; } = "*";

        [JsonProperty("recursive")] public bool   Recursive { get; set; }
    }

    public class ListProjectFilesResultData
    {
        [JsonProperty("directory")] public string   Directory { get; set; }
        [JsonProperty("files")]     public string[] Files     { get; set; }
        [JsonProperty("count")]     public int      Count     { get; set; }
    }
