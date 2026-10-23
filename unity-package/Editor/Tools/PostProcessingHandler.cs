using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

#if URP
using UnityEngine.Rendering;
#endif

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/create_postprocessing_volume
    ///
    /// Creates a URP Volume GameObject with a VolumeProfile containing the requested
    /// post-processing overrides. Supports global and local (box-bounded) volumes,
    /// reusing existing profiles or creating new ones under Assets/Volumes/.
    ///
    /// Override properties are set via SerializedObject using the VolumeParameter
    /// serialized layout: "{fieldName}.m_Value" for the value and
    /// "{fieldName}.overrideState" to enable the override.
    ///
    /// Effect type names are resolved by searching loaded assemblies for types that
    /// inherit from VolumeComponent, with the URP namespace tried first. Results are
    /// cached for the editor session.
    ///
    /// Requires: com.unity.render-pipelines.universal (guarded by AI_NAVIGATION define).
    /// Falls back to PACKAGE_NOT_INSTALLED if URP is absent.
    ///
    /// TODO: SetProperty / ParseFloats are duplicated from ComponentHandler.
    ///       Extract to Editor/Core/PropertyHelper.cs when convenient.
    /// </summary>
    public static class PostProcessingHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/world/create_postprocessing_volume
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreatePostProcessingVolumeAsync(
            CreatePostProcessingVolumeRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
#if !URP
                return McpResponse.Fail("PACKAGE_NOT_INSTALLED",
                    "com.unity.render-pipelines.universal is required for post-processing volume tools. " +
                    "Install it via Window > Package Manager.");
#else
                if (req == null)
                    return McpResponse.Fail("INVALID_PARAMS", "Request body must not be empty.");

                if (!req.IsGlobal && (req.Position == null || req.Position.Length < 3))
                    return McpResponse.Fail("INVALID_PARAMS",
                        "position [x,y,z] is required for non-global volumes. " +
                        "Set is_global=true to create a scene-wide volume.");

                var warnings         = new List<string>();
                var overridesApplied = new List<string>();
                var overridesFailed  = new List<string>();

                // ── 1. Create or load VolumeProfile ──────────────────────────
                VolumeProfile profile;
                string        profileAssetPath;

                if (!string.IsNullOrEmpty(req.ProfilePath))
                {
                    // Reuse and modify an existing profile
                    profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(req.ProfilePath);
                    if (profile == null)
                        return McpResponse.Fail("PROFILE_NOT_FOUND",
                            $"No VolumeProfile found at '{req.ProfilePath}'. " +
                            "Omit profile_path to create a new one.");
                    profileAssetPath = req.ProfilePath;
                    Undo.RecordObject(profile, "MCP Modify Volume Profile");
                }
                else
                {
                    EnsureVolumeFolder();
                    var profileName = string.IsNullOrWhiteSpace(req.ProfileName)
                        ? "PostProcessVolume"
                        : req.ProfileName;
                    var rawPath = $"Assets/Volumes/{profileName}_Profile.asset";

                    // GenerateUniqueAssetPath appends " 1", " 2" etc. if the path exists
                    profileAssetPath = AssetDatabase.GenerateUniqueAssetPath(rawPath);
                    profile          = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, profileAssetPath);
                }

                // ── 2. Apply overrides ────────────────────────────────────────
                foreach (var overrideData in req.Overrides ?? Array.Empty<VolumeOverrideData>())
                {
                    if (string.IsNullOrWhiteSpace(overrideData?.Effect)) continue;

                    var (success, failReason) = ApplyOverride(profile, overrideData, warnings);
                    if (success) overridesApplied.Add(overrideData.Effect);
                    else         overridesFailed.Add($"{overrideData.Effect}: {failReason}");
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();

                // ── 3. Create Volume GameObject ───────────────────────────────
                var goName = !string.IsNullOrWhiteSpace(req.Name)
                    ? req.Name
                    : $"PostProcessVolume_{System.IO.Path.GetFileNameWithoutExtension(profileAssetPath)}";

                var go = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(go, "MCP Create Post Processing Volume");

                if (!req.IsGlobal && req.Position != null && req.Position.Length >= 3)
                    go.transform.position = new Vector3(req.Position[0], req.Position[1], req.Position[2]);

                // Optional parent
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    var parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent != null)
                        go.transform.SetParent(parent.transform, worldPositionStays: true);
                    else
                        warnings.Add($"Parent path not found '{req.ParentPath}' — created at scene root.");
                }

                // Volume component
                var volume           = Undo.AddComponent<Volume>(go);
                volume.isGlobal      = req.IsGlobal;
                volume.blendDistance = Mathf.Max(0f, req.BlendDistance);
                volume.priority      = req.Priority;
                volume.profile       = profile;

                // BoxCollider for local volume scene-view visualisation (trigger-only, no physics)
                if (!req.IsGlobal && req.Size != null && req.Size.Length >= 3)
                {
                    var box       = Undo.AddComponent<BoxCollider>(go);
                    box.isTrigger = true;
                    box.size      = new Vector3(req.Size[0], req.Size[1], req.Size[2]);
                }

                EditorUtility.SetDirty(go);

                return McpResponse.Ok(new CreatePostProcessingVolumeResultData
                {
                    ObjectPath       = PlacementHandler.GetPath(go),
                    ProfilePath      = profileAssetPath,
                    IsGlobal         = req.IsGlobal,
                    BlendDistance    = req.BlendDistance,
                    OverridesApplied = overridesApplied.ToArray(),
                    OverridesFailed  = overridesFailed.ToArray(),
                    Warnings         = warnings.ToArray()
                });
#endif
            });

        // -----------------------------------------------------------------------
        // Override application
        // -----------------------------------------------------------------------

#if URP
        /// <summary>
        /// Finds the VolumeComponent type, adds it to the profile if absent, then
        /// sets each requested property via SerializedObject.
        /// Returns (true, null) on success or (false, reason) on failure.
        /// </summary>
        private static (bool success, string failReason) ApplyOverride(
            VolumeProfile      profile,
            VolumeOverrideData data,
            List<string>       warnings)
        {
            var type = FindVolumeComponentType(data.Effect);
            if (type == null)
                return (false,
                    $"Type not found. Check spelling — use the exact C# class name " +
                    $"(e.g. 'Bloom', 'ColorAdjustments', 'DepthOfField'). " +
                    $"Custom effects must be in a loaded assembly.");

            // Find existing component of this type in the profile, or add a new one.
            // VolumeProfile.Add<T> also registers the component as a sub-asset, so the
            // profile must already be saved to disk before this is called.
            VolumeComponent component = profile.components
                .FirstOrDefault(c => c != null && c.GetType() == type);

            if (component == null)
            {
                var addMethod = typeof(VolumeProfile)
                    .GetMethod("Add", new[] { typeof(bool) })
                    ?.MakeGenericMethod(type);

                if (addMethod == null)
                    return (false, "Could not locate VolumeProfile.Add<T>(bool) via reflection.");

                component = (VolumeComponent)addMethod.Invoke(profile, new object[] { false });
            }

            if (component == null)
                return (false, "VolumeProfile.Add returned null — check the effect type.");

            // No properties specified — just adding the effect is valid.
            if (data.Properties == null || data.Properties.Count == 0)
                return (true, null);

            // Set properties via SerializedObject.
            // VolumeParameter<T> serialises as two sibling fields:
            //   {fieldName}.m_Value        — the typed value
            //   {fieldName}.overrideState  — whether this param overrides the default
            var so = new SerializedObject(component);

            foreach (var kvp in data.Properties)
            {
                var overrideProp = so.FindProperty($"{kvp.Key}.overrideState");
                var valueProp    = so.FindProperty($"{kvp.Key}.m_Value");

                if (overrideProp == null || valueProp == null)
                {
                    warnings.Add(
                        $"  {data.Effect}.{kvp.Key}: property not found. " +
                        "Verify the C# field name (case-insensitive) and that it is a VolumeParameter.");
                    continue;
                }

                overrideProp.boolValue = true;

                if (!SetProperty(valueProp, kvp.Value, out string propError))
                    warnings.Add($"  {data.Effect}.{kvp.Key}: {propError}");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return (true, null);
        }

        // -----------------------------------------------------------------------
        // Type resolution with session cache
        // -----------------------------------------------------------------------

        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves an effect name to a VolumeComponent subtype.
        /// Fast path: exact match in UnityEngine.Rendering.Universal namespace.
        /// Slow path: scan all loaded assemblies (result is cached for the session).
        /// </summary>
        private static Type FindVolumeComponentType(string effectName)
        {
            if (string.IsNullOrWhiteSpace(effectName)) return null;
            if (TypeCache.TryGetValue(effectName, out var cached)) return cached;

            // 1. Try the standard URP runtime assembly directly
            var fullName = $"UnityEngine.Rendering.Universal.{effectName}";
            var type = Type.GetType($"{fullName}, Unity.RenderPipelines.Universal.Runtime");
            if (IsValidVolumeComponent(type)) { TypeCache[effectName] = type; return type; }

            // 2. Scan all loaded assemblies — handles custom effects and version differences
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Exact namespace match first
                type = assembly.GetType(fullName);
                if (IsValidVolumeComponent(type)) { TypeCache[effectName] = type; return type; }

                // Case-insensitive name-only match
                foreach (var t in assembly.GetTypes())
                {
                    if (string.Equals(t.Name, effectName, StringComparison.OrdinalIgnoreCase) &&
                        IsValidVolumeComponent(t))
                    { TypeCache[effectName] = t; return t; }
                }
            }

            TypeCache[effectName] = null;
            return null;
        }

        private static bool IsValidVolumeComponent(Type t) =>
            t != null && typeof(VolumeComponent).IsAssignableFrom(t) && !t.IsAbstract;

        // -----------------------------------------------------------------------
        // Property setter — copied from ComponentHandler (TODO: extract to PropertyHelper.cs)
        // -----------------------------------------------------------------------

        private static bool SetProperty(SerializedProperty prop, JToken value, out string error)
        {
            error = null;
            if (value == null || value.Type == JTokenType.Null)
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                { prop.objectReferenceValue = null; return true; }
                error = "Null is only valid for ObjectReference properties.";
                return false;
            }

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.Value<float>(); return true;

                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                        prop.intValue = value.Value<int>(); return true;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.Value<bool>(); return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value.Value<string>() ?? string.Empty; return true;

                    case SerializedPropertyType.Enum:
                        if (value.Type == JTokenType.Integer)
                        { prop.enumValueIndex = value.Value<int>(); return true; }
                        var enumName = value.Value<string>();
                        var idx = Array.IndexOf(prop.enumNames ?? Array.Empty<string>(), enumName);
                        if (idx < 0 && prop.enumDisplayNames != null)
                            idx = Array.IndexOf(prop.enumDisplayNames, enumName);
                        if (idx < 0)
                        { error = $"'{enumName}' not valid. Options: {string.Join(", ", prop.enumNames ?? Array.Empty<string>())}"; return false; }
                        prop.enumValueIndex = idx; return true;

                    case SerializedPropertyType.Vector2:
                        var v2 = ParseFloats(value, 2, out error); if (v2 == null) return false;
                        prop.vector2Value = new Vector2(v2[0], v2[1]); return true;

                    case SerializedPropertyType.Vector3:
                        var v3 = ParseFloats(value, 3, out error); if (v3 == null) return false;
                        prop.vector3Value = new Vector3(v3[0], v3[1], v3[2]); return true;

                    case SerializedPropertyType.Vector4:
                        var v4 = ParseFloats(value, 4, out error); if (v4 == null) return false;
                        prop.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); return true;

                    case SerializedPropertyType.Color:
                        var vc = ParseFloats(value, 3, out error); if (vc == null) return false;
                        prop.colorValue = new Color(vc[0], vc[1], vc[2], vc.Length >= 4 ? vc[3] : 1f); return true;

                    default:
                        error = $"Property type '{prop.propertyType}' is not supported."; return false;
                }
            }
            catch (Exception ex) { error = $"Value conversion failed: {ex.Message}"; return false; }
        }

        private static float[] ParseFloats(JToken value, int minLength, out string error)
        {
            error = null;
            if (value.Type != JTokenType.Array)
            { error = $"Expected a JSON array of {minLength} floats."; return null; }
            var arr = value.ToObject<float[]>();
            if (arr == null || arr.Length < minLength)
            { error = $"Array must have at least {minLength} elements."; return null; }
            return arr;
        }
#endif

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static void EnsureVolumeFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Volumes"))
                AssetDatabase.CreateFolder("Assets", "Volumes");
        }
    }
}
