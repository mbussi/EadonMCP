using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles light creation and scene-wide lighting settings (ambient, skybox, fog).
    ///
    /// create_light registers its GameObject with Unity's Undo system.
    /// Ambient, skybox and fog changes are recorded on the active scene's render
    /// settings object (as Unity's Lighting window does), so Ctrl+Z reverts them,
    /// and mark the scene dirty.
    /// </summary>
    public static class LightingHandler
    {
        // RenderSettings.GetRenderSettings() is internal: it returns the active scene's
        // render settings object, the one Unity's Lighting window records for Undo.
        private static readonly MethodInfo GetRenderSettingsMethod =
            typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>Records the render settings for Undo before a change. Does nothing if Unity hides the object.</summary>
        private static void RecordRenderSettings(string undoName)
        {
            if (Application.isPlaying) return;
            if (GetRenderSettingsMethod?.Invoke(null, null) is Object settings && settings != null)
                Undo.RecordObject(settings, undoName);
        }

        // -----------------------------------------------------------------------
        // POST /tools/lighting/create_light
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateLightAsync(CreateLightRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (!TryParseLightType(req?.LightType, out LightType lightType))
                    return McpResponse.Fail("INVALID_LIGHT_TYPE",
                        $"Unknown light type '{req?.LightType}'. Valid values: Point, Spot, Directional.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at path '{req.ParentPath}'.");
                }

                var position  = ToVector3(req.Position);
                var rotation  = Quaternion.Euler(ToVector3(req.Rotation));
                var color     = ToColor(req.Color);
                float intensity = req.Intensity ?? 1f;
                float range     = req.Range     ?? 10f;
                float spotAngle = req.SpotAngle ?? 30f;
                string goName   = string.IsNullOrWhiteSpace(req.Name) ? $"{req.LightType} Light" : req.Name;

                if (req.DryRun)
                    return McpResponse.Ok(new CreatedLightData
                    {
                        InstancePath = parent == null ? goName : PlacementHandler.GetPath(parent) + "/" + goName,
                        DisplayName  = goName,
                        LightType    = req.LightType,
                        Position     = Vec3(position),
                        Rotation     = Vec3(rotation.eulerAngles),
                        Color        = new[] { color.r, color.g, color.b },
                        Intensity    = intensity,
                        DryRun       = true
                    });

                var go = new GameObject(goName);
                go.transform.position = position;
                go.transform.rotation = rotation;

                if (parent != null)
                    go.transform.SetParent(parent.transform, worldPositionStays: true);

                var light       = go.AddComponent<Light>();
                light.type      = lightType;
                light.color     = color;
                light.intensity = intensity;
                light.shadows   = ParseShadows(req.Shadows);

                if (lightType == LightType.Point || lightType == LightType.Spot)
                    light.range = range;
                if (lightType == LightType.Spot)
                    light.spotAngle = spotAngle;

                Undo.RegisterCreatedObjectUndo(go, $"MCP Create {goName}");

                return McpResponse.Ok(new CreatedLightData
                {
                    InstancePath = PlacementHandler.GetPath(go),
                    DisplayName  = go.name,
                    LightType    = req.LightType,
                    Position     = Vec3(go.transform.position),
                    Rotation     = Vec3(go.transform.eulerAngles),
                    Color        = new[] { color.r, color.g, color.b },
                    Intensity    = light.intensity,
                    DryRun       = false
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/lighting/set_ambient
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetAmbientLightAsync(SetAmbientLightRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (!TryParseAmbientMode(req?.Mode, out AmbientMode mode))
                    return McpResponse.Fail("INVALID_MODE",
                        $"Unknown ambient mode '{req?.Mode}'. Valid values: Flat, Trilight, Skybox.");

                if (req.DryRun)
                    return McpResponse.Ok(new { mode = req.Mode, dry_run = true });

                RecordRenderSettings("MCP Set Ambient Light");
                RenderSettings.ambientMode = mode;

                switch (mode)
                {
                    case AmbientMode.Flat:
                        if (req.Color != null)
                            RenderSettings.ambientLight = ToColor(req.Color);
                        break;
                    case AmbientMode.Trilight:
                        if (req.SkyColor     != null) RenderSettings.ambientSkyColor     = ToColor(req.SkyColor);
                        if (req.EquatorColor != null) RenderSettings.ambientEquatorColor = ToColor(req.EquatorColor);
                        if (req.GroundColor  != null) RenderSettings.ambientGroundColor  = ToColor(req.GroundColor);
                        break;
                    // AmbientMode.Skybox: driven by the skybox, no colour input needed
                }

                if (req.Intensity.HasValue)
                    RenderSettings.ambientIntensity = req.Intensity.Value;

                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                return McpResponse.Ok(new { mode = req.Mode, dry_run = false });
            });

        // -----------------------------------------------------------------------
        // POST /tools/lighting/set_skybox
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetSkyboxAsync(SetSkyboxRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                Material skybox = null;

                if (!string.IsNullOrEmpty(req?.MaterialPath))
                {
                    skybox = AssetDatabase.LoadAssetAtPath<Material>(req.MaterialPath);
                    if (skybox == null)
                        return McpResponse.Fail("MATERIAL_NOT_FOUND",
                            $"No material at '{req.MaterialPath}'.");
                }

                if (req.DryRun)
                    return McpResponse.Ok(new { material_path = req.MaterialPath, dry_run = true });

                RecordRenderSettings("MCP Set Skybox");
                RenderSettings.skybox = skybox;
                DynamicGI.UpdateEnvironment();
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                return McpResponse.Ok(new { material_path = req.MaterialPath, dry_run = false });
            });

        // -----------------------------------------------------------------------
        // POST /tools/lighting/set_fog
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetFogAsync(SetFogRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Mode != null && !TryParseFogMode(req.Mode, out _))
                    return McpResponse.Fail("INVALID_FOG_MODE",
                        $"Unknown fog mode '{req.Mode}'. Valid values: Linear, Exponential, ExponentialSquared.");

                if (req.DryRun)
                    return McpResponse.Ok(new { enabled = req.Enabled, mode = req.Mode, dry_run = true });

                RecordRenderSettings("MCP Set Fog");
                RenderSettings.fog = req.Enabled;

                if (req.Color != null) RenderSettings.fogColor = ToColor(req.Color);

                if (req.Mode != null && TryParseFogMode(req.Mode, out FogMode fogMode))
                    RenderSettings.fogMode = fogMode;

                if (req.Density.HasValue) RenderSettings.fogDensity        = req.Density.Value;
                if (req.Start.HasValue)   RenderSettings.fogStartDistance  = req.Start.Value;
                if (req.End.HasValue)     RenderSettings.fogEndDistance    = req.End.Value;

                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                return McpResponse.Ok(new { enabled = req.Enabled, mode = req.Mode, dry_run = false });
            });

        // -----------------------------------------------------------------------
        // Parsers
        // -----------------------------------------------------------------------

        private static bool TryParseLightType(string value, out LightType result)
        {
            switch (value?.ToLowerInvariant())
            {
                case "point":       result = LightType.Point;       return true;
                case "spot":        result = LightType.Spot;        return true;
                case "directional": result = LightType.Directional; return true;
                default:            result = LightType.Point;       return false;
            }
        }

        private static bool TryParseAmbientMode(string value, out AmbientMode result)
        {
            switch (value?.ToLowerInvariant())
            {
                case "flat":     result = AmbientMode.Flat;     return true;
                case "trilight": result = AmbientMode.Trilight; return true;
                case "skybox":   result = AmbientMode.Skybox;   return true;
                default:         result = AmbientMode.Flat;     return false;
            }
        }

        private static bool TryParseFogMode(string value, out FogMode result)
        {
            switch (value?.ToLowerInvariant())
            {
                case "linear":              result = FogMode.Linear;              return true;
                case "exponential":         result = FogMode.Exponential;         return true;
                case "exponentialsquared":  result = FogMode.ExponentialSquared;  return true;
                default:                    result = FogMode.Exponential;         return false;
            }
        }

        private static LightShadows ParseShadows(string value) => value?.ToLowerInvariant() switch
        {
            "none" => LightShadows.None,
            "hard" => LightShadows.Hard,
            _      => LightShadows.Soft   // "soft" or unspecified → soft
        };

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Vector3 ToVector3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        private static Color ToColor(float[] a) =>
            a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2]) : Color.white;

        private static float[] Vec3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
