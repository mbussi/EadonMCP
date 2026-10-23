using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Core
{
    public static class McpRouter
    {
        /// <summary>Routes an HTTP request.</summary>
        public static Task<McpResponse> RouteAsync(HttpListenerRequest request) =>
            RouteAsync(McpRequest.FromHttp(request));

        /// <summary>
        /// Routes a request, from HTTP or built from a tool's arguments (a proposal
        /// operation, see McpRequest.FromArguments).
        /// </summary>
        public static async Task<McpResponse> RouteAsync(McpRequest request)
        {
            string method = request.HttpMethod;
            string path   = request.Path;

            try
            {
                // Base
                if (method == "GET" && path == "/status")
                    return await GetStatusAsync();

                // Tool catalog (built-in and extension tools)
                if (method == "GET" && path == "/tools/catalog")
                    return McpToolCatalog.GetCatalog(request.QueryString["source"], request.QueryString["detail"]);
                if (method == "GET" && path == "/tools/describe")
                    return McpToolCatalog.Describe(request.QueryString["name"]);
                if (method == "POST" && path == "/tools/call")
                {
                    var call = await ParseBodyAsync<Newtonsoft.Json.Linq.JObject>(request);
                    if (McpJob.Current != null) McpJob.Current.Tool = (string)call["name"];
                    return await McpToolRegistry.CallAsync(
                        (string)call["name"], call["arguments"] as Newtonsoft.Json.Linq.JObject);
                }

                // Jobs (requests handed off after McpHttpListener.HandoffSeconds, light bakes).
                // These never touch the main thread, so they answer while Unity is busy.
                if (method == "GET" && path == "/jobs")
                    return McpJobStore.GetStatus(request.QueryString["job_id"]);
                if (method == "POST" && path == "/jobs/cancel")
                {
                    var cancel = await ParseBodyAsync<Newtonsoft.Json.Linq.JObject>(request);
                    return McpJobStore.Cancel((string)cancel["job_id"]);
                }

                // Scene Query
                if (method == "GET" && path == "/scene/hierarchy")
                    return await SceneQueryHandler.GetSceneHierarchyAsync(
                        ParseInt(request.QueryString["max_depth"], -1));
                if (method == "GET" && path == "/scene/context")
                    return await SceneQueryHandler.GetSceneContextAsync(
                        request.QueryString["zone_name"]);
                if (method == "GET" && path == "/assets/prefabs")
                    return await SceneQueryHandler.GetPrefabCatalogAsync(request.QueryString["search_path"] ??
                                                                         "Assets");

                // Placement
                if (method == "POST" && path == "/tools/placement/place_prefab")
                    return await PlacementHandler.PlacePrefabAsync(await ParseBodyAsync<PlacePrefabRequest>(request));
                if (method == "POST" && path == "/tools/placement/set_transform")
                    return await PlacementHandler.SetTransformAsync(await ParseBodyAsync<SetTransformRequest>(request));
                if (method == "POST" && path == "/tools/placement/delete_object")
                    return await PlacementHandler.DeleteObjectAsync(await ParseBodyAsync<DeleteObjectRequest>(request));
                if (method == "POST" && path == "/tools/placement/create_empty")
                    return await PlacementHandler.CreateEmptyAsync(await ParseBodyAsync<CreateEmptyRequest>(request));

                // Lighting
                if (method == "POST" && path == "/tools/lighting/create_light")
                    return await LightingHandler.CreateLightAsync(await ParseBodyAsync<CreateLightRequest>(request));
                if (method == "POST" && path == "/tools/lighting/set_ambient")
                    return await LightingHandler.SetAmbientLightAsync(
                        await ParseBodyAsync<SetAmbientLightRequest>(request));
                if (method == "POST" && path == "/tools/lighting/set_skybox")
                    return await LightingHandler.SetSkyboxAsync(await ParseBodyAsync<SetSkyboxRequest>(request));
                if (method == "POST" && path == "/tools/lighting/set_fog")
                    return await LightingHandler.SetFogAsync(await ParseBodyAsync<SetFogRequest>(request));

                // Proposal
                if (method == "POST" && path == "/tools/proposal/submit")
                    return await ProposalHandler.SubmitProposalAsync(
                        await ParseBodyAsync<ProposeChangesRequest>(request));
                if (method == "POST" && path == "/tools/proposal/execute")
                    return await ProposalHandler.ExecuteProposalAsync(
                        await ParseBodyAsync<ExecuteProposalRequest>(request));
                if (method == "POST" && path == "/tools/proposal/discard")
                    return await ProposalHandler.DiscardProposalAsync(
                        await ParseBodyAsync<DiscardProposalRequest>(request));
                if (method == "GET" && path == "/tools/proposal/pending")
                    return await ProposalHandler.GetPendingProposalsAsync(request.QueryString["proposal_id"]);

                // Procedural
                if (method == "POST" && path == "/tools/placement/place_batch")
                    return await ProceduralHandler.PlacePrefabBatchAsync(
                        await ParseBodyAsync<PlacePrefabBatchRequest>(request));
                if (method == "POST" && path == "/tools/procedural/scatter")
                    return await ProceduralHandler.ScatterPrefabsAsync(
                        await ParseBodyAsync<ScatterPrefabsRequest>(request));
                if (method == "POST" && path == "/tools/procedural/along_path")
                    return await ProceduralHandler.PlaceAlongPathAsync(
                        await ParseBodyAsync<PlaceAlongPathRequest>(request));
                if (method == "POST" && path == "/tools/procedural/fill_grid")
                    return await ProceduralHandler.FillGridAsync(await ParseBodyAsync<FillGridRequest>(request));

                // PCG tools are served by the PCG extension (extensions/pcg), through McpRouteRegistry.

                // Components
                if (method == "GET" && path == "/assets/components/catalog")
                    return await ComponentHandler.GetComponentCatalogAsync();
                if (method == "POST" && path == "/tools/components/add")
                    return await ComponentHandler.AddComponentAsync(await ParseBodyAsync<AddComponentRequest>(request));

                // Scaffold — queries
                if (method == "GET" && path == "/assets/scaffold/zones")
                    return await ScaffoldQueryHandler.GetZonesAsync();
                if (method == "GET" && path == "/assets/scaffold/anchors")
                    return await ScaffoldQueryHandler.GetAnchorsAsync();
                if (method == "GET" && path == "/assets/scaffold/markers")
                    return await ScaffoldQueryHandler.GetMarkersAsync();

                // Scaffold — mutations
                if (method == "POST" && path == "/tools/scaffold/create_zone")
                    return await ScaffoldHandler.CreateZoneAsync(await ParseBodyAsync<CreateZoneRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/link_zones")
                    return await ScaffoldHandler.LinkZonesAsync(await ParseBodyAsync<LinkZonesRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/create_reference_point")
                    return await ScaffoldHandler.CreateReferencePointAsync(
                        await ParseBodyAsync<CreateReferencePointRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/create_marker")
                    return await ScaffoldHandler.CreateMarkerAsync(await ParseBodyAsync<CreateMarkerRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/update_marker")
                    return await ScaffoldHandler.UpdateMarkerAsync(await ParseBodyAsync<UpdateMarkerRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/hide_scaffold")
                    return await ScaffoldHandler.HideScaffoldAsync();
                if (method == "POST" && path == "/tools/scaffold/show_scaffold")
                    return await ScaffoldHandler.ShowScaffoldAsync();
                if (method == "POST" && path == "/tools/scaffold/resolve_markers")
                    return await BakeHandler.ResolveMarkersAsync(await ParseBodyAsync<ResolveMarkersRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/execute_bake")
                    return await BakeHandler.ExecuteBakeAsync(await ParseBodyAsync<ExecuteBakeRequest>(request));

                // Cutscene
                if (method == "GET" && path == "/assets/cutscene/templates")
                    return await CutsceneQueryHandler.GetTemplatesAsync();
                if (method == "POST" && path == "/tools/cutscene/create_sequence")
                    return await CutsceneHandler.CreateSequenceAsync(await ParseBodyAsync<CreateSequenceRequest>(request));
                if (method == "POST" && path == "/tools/cutscene/preview_sequence")
                    return await CutsceneUpdateHandler.PreviewSequenceAsync(await ParseBodyAsync<PreviewSequenceRequest>(request));
                if (method == "POST" && path == "/tools/cutscene/update_sequence")
                    return await CutsceneUpdateHandler.UpdateSequenceAsync(await ParseBodyAsync<UpdateSequenceRequest>(request));

                // Objects — Foundation
                if (method == "POST" && path == "/tools/scene/find_objects")
                    return await ObjectHandler.FindObjectsAsync(
                        await ParseBodyAsync<FindObjectsRequest>(request));
                if (method == "POST" && path == "/tools/objects/get_properties")
                    return await ObjectHandler.GetObjectPropertiesAsync(
                        await ParseBodyAsync<GetObjectPropertiesRequest>(request));
                if (method == "POST" && path == "/tools/objects/set_properties")
                    return await ObjectHandler.SetObjectPropertiesAsync(
                        await ParseBodyAsync<SetObjectPropertiesRequest>(request));
                if (method == "POST" && path == "/tools/objects/set_metadata")
                    return await ObjectHandler.SetObjectMetadataAsync(
                        await ParseBodyAsync<SetObjectMetadataRequest>(request));
                if (method == "POST" && path == "/tools/objects/reparent")
                    return await ObjectHandler.ReparentObjectsAsync(
                        await ParseBodyAsync<ReparentObjectsRequest>(request));
                if (method == "POST" && path == "/tools/objects/duplicate")
                    return await ObjectHandler.DuplicateObjectsAsync(
                        await ParseBodyAsync<DuplicateObjectsRequest>(request));

                // Dressing
                if (method == "POST" && path == "/tools/placement/snap_to_surface")
                    return await DressingHandler.SnapToSurfaceAsync(
                        await ParseBodyAsync<SnapToSurfaceRequest>(request));
                if (method == "POST" && path == "/tools/placement/align_and_distribute")
                    return await DressingHandler.AlignAndDistributeAsync(
                        await ParseBodyAsync<AlignAndDistributeRequest>(request));
                if (method == "POST" && path == "/tools/placement/randomize_transforms")
                    return await DressingHandler.RandomizeTransformsAsync(
                        await ParseBodyAsync<RandomizeTransformsRequest>(request));
                if (method == "POST" && path == "/tools/placement/group_objects")
                    return await DressingHandler.GroupObjectsAsync(
                        await ParseBodyAsync<GroupObjectsRequest>(request));
                if (method == "POST" && path == "/tools/placement/dress_area")
                    return await DressingHandler.DressAreaAsync(
                        await ParseBodyAsync<DressAreaRequest>(request));
                if (method == "POST" && path == "/tools/placement/create_trigger_volume")
                    return await DressingHandler.CreateTriggerVolumeAsync(
                        await ParseBodyAsync<CreateTriggerVolumeRequest>(request));
                if (method == "POST" && path == "/tools/placement/apply_material")
                    return await DressingHandler.ApplyMaterialAsync(
                        await ParseBodyAsync<ApplyMaterialRequest>(request));
                
                // Open World
                if (method == "POST" && path == "/tools/world/query_terrain")
                    return await TerrainQueryHandler.QueryTerrainAsync(await ParseBodyAsync<QueryTerrainRequest>(request));
                if (method == "POST" && path == "/tools/world/bake_navmesh")
                    return await NavMeshHandler.BakeNavMeshAsync(await ParseBodyAsync<BakeNavMeshRequest>(request));
                if (method == "POST" && path == "/tools/world/set_navmesh_area")
                    return await NavMeshHandler.SetNavMeshAreaAsync(await ParseBodyAsync<SetNavMeshAreaRequest>(request));
                if (method == "GET"  && path == "/scene/scenes")
                    return await SceneManagementHandler.GetLoadedScenesAsync();
                if (method == "POST" && path == "/tools/scene/manage")
                    return await SceneManagementHandler.ManageSceneAsync(await ParseBodyAsync<ManageSceneRequest>(request));
                
                // Production Quality
                if (method == "POST" && path == "/tools/scene/validate")
                    return await SceneValidatorHandler.ValidateSceneAsync(await ParseBodyAsync<ValidateSceneRequest>(request));
                if (method == "POST" && path == "/tools/world/create_audio_zone")
                    return await AudioZoneHandler.CreateAudioZoneAsync(await ParseBodyAsync<CreateAudioZoneRequest>(request));
                
                // Post Processing
                if (method == "POST" && path == "/tools/world/create_postprocessing_volume")
                    return await PostProcessingHandler.CreatePostProcessingVolumeAsync(
                        await ParseBodyAsync<CreatePostProcessingVolumeRequest>(request));
                
                // Metadata Authoring
                if (method == "POST" && path == "/tools/metadata/create_prefab_metadata")
                    return await MetadataAuthorHandler.CreatePrefabMetadataAsync(
                        await ParseBodyAsync<CreatePrefabMetadataRequest>(request));
                if (method == "POST" && path == "/tools/metadata/update_prefab_metadata")
                    return await MetadataAuthorHandler.UpdatePrefabMetadataAsync(
                        await ParseBodyAsync<UpdatePrefabMetadataRequest>(request));
                if (method == "POST" && path == "/tools/metadata/update_scene_context")
                    return await MetadataAuthorHandler.UpdateSceneContextAsync(
                        await ParseBodyAsync<UpdateSceneContextRequest>(request));
                
                //  Write Operations + Replace
                if (method == "POST" && path == "/tools/world/paint_terrain_layer")
                    return await TerrainPaintHandler.PaintTerrainLayerAsync(
                        await ParseBodyAsync<PaintTerrainLayerRequest>(request));
                if (method == "POST" && path == "/tools/world/bake_lighting")
                    return await LightBakeHandler.BakeLightingAsync(
                        await ParseBodyAsync<BakeLightingRequest>(request));
                if (method == "POST" && path == "/tools/placement/replace_prefab")
                    return await PlacementHandler.ReplacePrefabAsync(
                        await ParseBodyAsync<ReplacePrefabRequest>(request));
                
                // Reflection Probes + Patrol Routes
                if (method == "POST" && path == "/tools/world/create_reflection_probe")
                    return await ReflectionProbeHandler.CreateReflectionProbeAsync(
                        await ParseBodyAsync<CreateReflectionProbeRequest>(request));
                if (method == "POST" && path == "/tools/scaffold/create_patrol_route")
                    return await PatrolRouteHandler.CreatePatrolRouteAsync(
                        await ParseBodyAsync<CreatePatrolRouteRequest>(request));
                if (method == "GET"  && path == "/assets/scaffold/patrol_routes")
                    return await PatrolRouteHandler.GetPatrolRoutesAsync();
                
                // Visual Feedback
                if (method == "POST" && path == "/tools/debug/capture_scene_view")
                    return await SceneViewCaptureHandler.CaptureSceneViewAsync(
                        await ParseBodyAsync<CaptureSceneViewRequest>(request));

                // Project File Tools
                if (method == "POST" && path == "/tools/project/refresh_assets")
                    return await ProjectFileHandler.RefreshAssetsAsync();
                if (method == "POST" && path == "/tools/project/write_file")
                    return await ProjectFileHandler.WriteFileAsync(
                        await ParseBodyAsync<WriteProjectFileRequest>(request));
                if (method == "POST" && path == "/tools/project/read_file")
                    return await ProjectFileHandler.ReadFileAsync(
                        await ParseBodyAsync<ReadProjectFileRequest>(request));
                if (method == "POST" && path == "/tools/project/list_files")
                    return await ProjectFileHandler.ListFilesAsync(
                        await ParseBodyAsync<ListProjectFilesRequest>(request));

                // Routes registered by extensions (McpRouteRegistry)
                if (McpRouteRegistry.TryGetHandler(method, path, out var extensionHandler))
                    return await extensionHandler(request);

                return McpResponse.Fail("NOT_FOUND", $"No route for {method} {path}");
            }
            catch (OperationCanceledException)
            {
                return McpResponse.Fail("CANCELLED", "The job was cancelled before Unity started it.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Unhandled error in {method} {path}: {ex}");
                return McpResponse.Fail("INTERNAL_ERROR", ex.Message);
            }
        }

        private static Task<McpResponse> GetStatusAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var scene = SceneManager.GetActiveScene();
                return McpResponse.Ok(new StatusData
                {
                    UnityVersion = Application.unityVersion,
                    SceneName = scene.name,
                    ScenePath = scene.path,
                    IsPlaying = EditorApplication.isPlaying,
                });
            });

        /// <summary>Reads the request's JSON body as T (a new T when the body is empty). For bridge and extension handlers.</summary>
        public static async Task<T> ParseBodyAsync<T>(McpRequest request) where T : new()
        {
            string json = await request.ReadBodyAsync();
            if (string.IsNullOrWhiteSpace(json)) return new T();
            return JsonConvert.DeserializeObject<T>(json) ?? new T();
        }

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, out int result) ? result : fallback;
    }
}