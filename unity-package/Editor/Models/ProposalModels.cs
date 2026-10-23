using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------

    public class ProposeChangesRequest
    {
        [JsonProperty("summary")]    public string                   Summary    { get; set; }
        [JsonProperty("operations")] public ProposalOperationData[]  Operations { get; set; }
    }

    /// <summary>Operation descriptor as received in a propose_changes request.</summary>
    public class ProposalOperationData
    {
        [JsonProperty("id")]          public int                        Id          { get; set; }
        [JsonProperty("tool")]        public string                     Tool        { get; set; }
        [JsonProperty("description")] public string                     Description { get; set; }
        [JsonProperty("params")]      public Newtonsoft.Json.Linq.JObject Params    { get; set; }
    }

    public class ExecuteProposalRequest
    {
        [JsonProperty("proposal_id")] public string ProposalId { get; set; }
    }

    public class DiscardProposalRequest
    {
        [JsonProperty("proposal_id")] public string ProposalId { get; set; }
    }

    // -----------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------

    public class ProposalSubmittedData
    {
        [JsonProperty("proposal_id")]     public string ProposalId     { get; set; }
        [JsonProperty("summary")]         public string Summary        { get; set; }
        [JsonProperty("operation_count")] public int    OperationCount { get; set; }
        [JsonProperty("status")]          public string Status         { get; set; }
    }

    public class ExecuteProposalResultData
    {
        [JsonProperty("proposal_id")]    public string                   ProposalId    { get; set; }
        [JsonProperty("executed_count")] public int                      ExecutedCount { get; set; }
        [JsonProperty("failed_count")]   public int                      FailedCount   { get; set; }
        [JsonProperty("results")]        public OperationResultData[]    Results       { get; set; }
    }

    public class OperationResultData
    {
        [JsonProperty("operation_id")] public int    OperationId { get; set; }
        [JsonProperty("tool")]         public string Tool        { get; set; }
        [JsonProperty("success")]      public bool   Success     { get; set; }

        /// <summary>"done", "failed", "undone" (it ran, then the proposal failed and was undone) or "not run".</summary>
        [JsonProperty("outcome")]      public string Outcome     { get; set; }

        [JsonProperty("error",  NullValueHandling = NullValueHandling.Ignore)]
                                       public string Error       { get; set; }
    }

    public class PendingProposalData
    {
        [JsonProperty("proposal_id")]     public string   ProposalId     { get; set; }
        [JsonProperty("summary")]         public string   Summary        { get; set; }

        /// <summary>pending, approved, executing, executed, failed or discarded.</summary>
        [JsonProperty("status")]          public string   Status         { get; set; }
        [JsonProperty("operation_count")] public int      OperationCount { get; set; }
        [JsonProperty("created_at")]      public string   CreatedAt      { get; set; }
        [JsonProperty("finished_at", NullValueHandling = NullValueHandling.Ignore)]
                                          public string   FinishedAt     { get; set; }
        [JsonProperty("operations")]      public PendingOperationData[] Operations { get; set; }

        /// <summary>Once it has run: one entry per operation.</summary>
        [JsonProperty("results", NullValueHandling = NullValueHandling.Ignore)]
                                          public OperationResultData[] Results { get; set; }

        /// <summary>When it failed: the operation and its error.</summary>
        [JsonProperty("failure", NullValueHandling = NullValueHandling.Ignore)]
                                          public string   Failure        { get; set; }
    }

    public class PendingOperationData
    {
        [JsonProperty("id")]          public int    Id          { get; set; }
        [JsonProperty("tool")]        public string Tool        { get; set; }
        [JsonProperty("description")] public string Description { get; set; }

        /// <summary>The tool's arguments; only when one proposal is asked for.</summary>
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
                                      public Newtonsoft.Json.Linq.JObject Params { get; set; }
    }
}