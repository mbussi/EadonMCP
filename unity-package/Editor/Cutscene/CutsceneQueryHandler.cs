using System.Threading.Tasks;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Cutscene;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>Handles GET /assets/cutscene/templates.</summary>
    public static class CutsceneQueryHandler
    {
        public static Task<McpResponse> GetTemplatesAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
#if !CM2 && !CM3
                return McpResponse.Ok(new CutsceneTemplateListData
                {
                    CinemachineVersion = null,
                    TimelineVersion    = null,
                    Templates          = System.Array.Empty<CutsceneTemplateInfo>(),
                    Message =
                        "Cinemachine package not found. " +
                        "Install com.unity.cinemachine via the Package Manager " +
                        "to use cutscene tools.",
                });
#else
                return McpResponse.Ok(new CutsceneTemplateListData
                {
                    CinemachineVersion = CutsceneTemplateBase.GetCinemachineVersion(),
                    TimelineVersion    = CutsceneTemplateBase.GetTimelineVersion(),
                    Templates          = BuildTemplateList(),
                });
#endif
            });

        private static CutsceneTemplateInfo[] BuildTemplateList() => new[]
        {
            new CutsceneTemplateInfo
            {
                Name = "DialogueCamera",
                Description =
                    "Two-shot / over-shoulder camera for NPC dialogue. Blends from the " +
                    "active follow camera, holds while dialogue plays, then blends back. " +
                    "Fires dialogue_start and dialogue_end signals.",
                Parameters = new[]
                {
                    P("player_path",  "string",    required: true,
                      desc: "Hierarchy path of the player root, e.g. /Player"),
                    P("npc_path",     "string",    required: true,
                      desc: "Hierarchy path of the NPC root, e.g. /Village/Merchant_A"),
                    P("camera_side",  "string",    required: false, def: "auto",
                      opts: new[]{"left","right","front","auto"},
                      desc: "'auto' picks the side facing the current Scene view camera."),
                    P("duration",     "float",     required: true,
                      desc: "Total sequence length in seconds"),
                    P("blend_in",     "float",     required: false, def: "0.5",
                      desc: "Seconds to blend from follow camera to dialogue camera"),
                    P("blend_out",    "float",     required: false, def: "0.5",
                      desc: "Seconds to blend back to follow camera"),
                    P("look_offset",  "float[3]",  required: false, def: "[0,1.6,0]",
                      desc: "Local offset from NPC root to look at — [0,1.6,0] = head height"),
                },
            },

            new CutsceneTemplateInfo
            {
                Name = "AreaReveal",
                Description =
                    "Camera sweeps along a designer-specified path to reveal a location, " +
                    "then the follow camera returns. Typical for dungeon entrances, " +
                    "settlement arrivals, or boss arenas. Fires reveal_start and reveal_complete.",
                Parameters = new[]
                {
                    P("waypoints",    "float[3][]", required: true, minCount: 2,
                      desc: "World-space camera path positions, minimum 2. The camera travels " +
                            "from waypoints[0] to waypoints[N-1] over the duration."),
                    P("look_at_path", "string",     required: false,
                      desc: "Optional scene path of what the camera looks toward throughout. " +
                            "If omitted, camera faces along the path direction."),
                    P("duration",     "float",      required: true,
                      desc: "Total sweep length in seconds"),
                    P("ease_in",      "float",      required: false, def: "0.2",
                      desc: "Fraction of duration for ease-in (0–1). Default 0.2."),
                    P("ease_out",     "float",      required: false, def: "0.2",
                      desc: "Fraction of duration for ease-out (0–1). Default 0.2."),
                },
            },

            new CutsceneTemplateInfo
            {
                Name = "FocusCut",
                Description =
                    "Quick dramatic cut to an important object — a discovered item, a " +
                    "mechanism activating, a boss stirring. Blends to a tight angle on the " +
                    "target, holds, then blends back. Fires focus_held when fully on target.",
                Parameters = new[]
                {
                    P("target_path",   "string",   required: true,
                      desc: "Hierarchy path of the object to focus on"),
                    P("camera_offset", "float[3]", required: false, def: "[0,1.2,-3.0]",
                      desc: "World-space offset from the target where the camera is placed"),
                    P("duration",      "float",    required: true,
                      desc: "Hold time at the focus angle in seconds (not including blends)"),
                    P("blend_in",      "float",    required: false, def: "0.3",
                      desc: "Seconds to blend from active camera to focus angle"),
                    P("blend_out",     "float",    required: false, def: "0.5",
                      desc: "Seconds to blend back to active camera"),
                },
            },

            new CutsceneTemplateInfo
            {
                Name = "ArrivalFlythrough",
                Description =
                    "Sweeping exterior or overhead shot introducing a major location — a " +
                    "fortress, a city district, a labyrinth. The camera travels a path while " +
                    "always facing a fixed world-space point. More cinematic than AreaReveal. " +
                    "Fires arrival_start and arrival_complete.",
                Parameters = new[]
                {
                    P("waypoints",        "float[3][]", required: true, minCount: 3,
                      desc: "Camera path — minimum 3 points for a compelling arc"),
                    P("look_at_position", "float[3]",   required: true,
                      desc: "Fixed world-space point the camera faces throughout — " +
                            "the area's visual centre of mass"),
                    P("duration",         "float",      required: true,
                      desc: "Total flythrough length in seconds"),
                    P("ease_in",          "float",      required: false, def: "0.15",
                      desc: "Fraction of duration for ease-in. Default 0.15."),
                    P("ease_out",         "float",      required: false, def: "0.25",
                      desc: "Fraction of duration for ease-out. Default 0.25."),
                },
            },
        };

        private static CutsceneParameterInfo P(
            string name, string type, bool required,
            string def = null, string desc = null,
            string[] opts = null, int? minCount = null) =>
            new CutsceneParameterInfo
            {
                Name        = name,
                Type        = type,
                Required    = required,
                Default     = def,
                Description = desc,
                Options     = opts,
                MinCount    = minCount,
            };
    }
}
