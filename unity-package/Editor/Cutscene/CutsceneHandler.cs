using System.Threading.Tasks;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Cutscene.Templates;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>Handles POST /tools/cutscene/create_sequence.</summary>
    public static class CutsceneHandler
    {
        public static Task<McpResponse> CreateSequenceAsync(CreateSequenceRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
#if !CM2 && !CM3
                return McpResponse.Fail("CINEMACHINE_NOT_FOUND",
                    "Cinemachine package is not installed. " +
                    "Install com.unity.cinemachine via the Package Manager.");
#else
                if (string.IsNullOrWhiteSpace(req?.Template))
                    return McpResponse.Fail("INVALID_PARAMS", "'template' is required.");
                if (string.IsNullOrWhiteSpace(req.SequenceName))
                    return McpResponse.Fail("INVALID_PARAMS", "'sequence_name' is required.");

                return req.Template switch
                {
                    "DialogueCamera"    => DialogueCameraTemplate.Execute(req),
                    // "AreaReveal"        => AreaRevealTemplate.Execute(req),
                    // "FocusCut"          => FocusCutTemplate.Execute(req),
                    // "ArrivalFlythrough" => ArrivalFlythroughTemplate.Execute(req),
                    _ => McpResponse.Fail("UNKNOWN_TEMPLATE",
                        $"Unknown template '{req.Template}'. " +
                        "Call get_cutscene_templates for the list of available templates."),
                };
#endif
            });
    }
}
