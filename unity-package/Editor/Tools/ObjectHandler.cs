using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Components;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles Group 1 Foundation tools — scene query and object mutation primitives.
    ///
    /// Routes to add in McpRouter:
    ///   POST /tools/scene/find_objects          → FindObjectsAsync(req)
    ///   POST /tools/objects/get_properties      → GetObjectPropertiesAsync(req)
    ///   POST /tools/objects/set_properties      → SetObjectPropertiesAsync(req)
    ///   POST /tools/objects/set_metadata        → SetObjectMetadataAsync(req)
    ///   POST /tools/objects/reparent            → ReparentObjectsAsync(req)
    ///   POST /tools/objects/duplicate           → DuplicateObjectsAsync(req)
    /// </summary>
    public static class ObjectHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/scene/find_objects
        // -----------------------------------------------------------------------

        public static Task<McpResponse> FindObjectsAsync(FindObjectsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                // Require at least one filter so this can't accidentally return
                // thousands of objects like get_scene_hierarchy does.
                bool hasFilter =
                    !string.IsNullOrEmpty(req?.Tag)           ||
                    !string.IsNullOrEmpty(req?.ComponentType) ||
                    !string.IsNullOrEmpty(req?.NamePattern)   ||
                    !string.IsNullOrEmpty(req?.Layer)         ||
                    (req?.NearPosition != null && req.NearPosition.Length >= 3);

                if (!hasFilter)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "At least one filter is required (tag, component_type, name_pattern, " +
                        "layer, or near_position). To inspect the full hierarchy use " +
                        "get_scene_hierarchy instead.");

                int maxResults = req.MaxResults > 0 ? req.MaxResults : 100;

                // ── Collect candidates ──────────────────────────────────── //
                IEnumerable<GameObject> candidates;

                if (req.NearPosition != null && req.NearPosition.Length >= 3)
                {
                    var centre = new Vector3(req.NearPosition[0], req.NearPosition[1], req.NearPosition[2]);
                    float radius = req.NearRadius > 0 ? req.NearRadius : 20f;

                    // Physics.OverlapSphere for collider-bearing objects (fast)
                    var hits = Physics.OverlapSphere(centre, radius);
                    var found = new HashSet<GameObject>(hits.Select(c => c.gameObject));

                    // Also catch non-collider objects by transform distance
                    foreach (var go in AllSceneObjects(req.IncludeInactive))
                        if (Vector3.Distance(go.transform.position, centre) <= radius)
                            found.Add(go);

                    candidates = found;
                }
                else
                {
                    candidates = AllSceneObjects(req.IncludeInactive);
                }

                // ── Apply filters ───────────────────────────────────────── //
                Type componentFilterType = null;
                if (!string.IsNullOrEmpty(req.ComponentType))
                {
                    componentFilterType = ComponentCatalogBuilder.ResolveComponentType(req.ComponentType);
                    if (componentFilterType == null)
                        return McpResponse.Fail("TYPE_NOT_FOUND",
                            $"Unknown component type '{req.ComponentType}'. " +
                            "Call get_component_catalog for valid type names.");
                }

                int? layerFilter = null;
                if (!string.IsNullOrEmpty(req.Layer))
                {
                    int l = LayerMask.NameToLayer(req.Layer);
                    if (l < 0)
                        return McpResponse.Fail("LAYER_NOT_FOUND",
                            $"Unknown layer '{req.Layer}'.");
                    layerFilter = l;
                }

                var results = new List<FindObjectData>();
                foreach (var go in candidates)
                {
                    if (results.Count >= maxResults) break;
                    if (go == null) continue;

                    if (!string.IsNullOrEmpty(req.Tag) && go.tag != req.Tag)
                        continue;
                    if (layerFilter.HasValue && go.layer != layerFilter.Value)
                        continue;
                    if (!string.IsNullOrEmpty(req.NamePattern) &&
                        !MatchesGlob(go.name, req.NamePattern))
                        continue;
                    if (componentFilterType != null &&
                        go.GetComponent(componentFilterType) == null)
                        continue;

                    results.Add(BuildObjectData(go));
                }

                return McpResponse.Ok(new FindObjectsResultData
                {
                    Objects = results.ToArray(),
                    Count   = results.Count,
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/objects/get_properties
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetObjectPropertiesAsync(GetObjectPropertiesRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ObjectPath))
                    return McpResponse.Fail("INVALID_PARAMS", "object_path is required.");
                if (string.IsNullOrWhiteSpace(req.ComponentType))
                    return McpResponse.Fail("INVALID_PARAMS", "component_type is required.");

                var go = PlacementHandler.FindByPath(req.ObjectPath);
                if (go == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND",
                        $"No object at '{req.ObjectPath}'. " +
                        "Use get_scene_hierarchy to find valid paths.");

                var type = ComponentCatalogBuilder.ResolveComponentType(req.ComponentType);
                if (type == null)
                    return McpResponse.Fail("TYPE_NOT_FOUND",
                        $"Unknown component type '{req.ComponentType}'.");

                var comp = go.GetComponent(type);
                if (comp == null)
                    return McpResponse.Fail("COMPONENT_NOT_FOUND",
                        $"'{req.ObjectPath}' does not have a {req.ComponentType} component. " +
                        "Use get_scene_hierarchy to inspect object components.");

                var so         = new SerializedObject(comp);
                var properties = new List<ObjectPropertyData>();
                var pathSet    = req.PropertyPaths != null && req.PropertyPaths.Length > 0
                    ? new HashSet<string>(req.PropertyPaths)
                    : null;

                if (pathSet != null)
                {
                    // Read specific requested paths
                    foreach (var path in req.PropertyPaths)
                    {
                        var sp = so.FindProperty(path);
                        if (sp == null)
                        {
                            properties.Add(new ObjectPropertyData
                            {
                                PropertyPath = path,
                                Value        = JValue.CreateNull(),
                                Type         = "NotFound",
                            });
                            continue;
                        }
                        properties.Add(new ObjectPropertyData
                        {
                            PropertyPath = sp.propertyPath,
                            Value        = ReadPropertyValue(sp),
                            Type         = PropertyTypeLabel(sp),
                        });
                    }
                }
                else
                {
                    // Read all top-level serialized properties
                    var sp = so.GetIterator();
                    bool enterChildren = true;
                    while (sp.NextVisible(enterChildren))
                    {
                        enterChildren = false; // only top-level (depth 0/1)
                        if (sp.propertyPath == "m_Script") continue; // skip script ref
                        if (sp.depth > 1) continue;                  // skip nested

                        properties.Add(new ObjectPropertyData
                        {
                            PropertyPath = sp.propertyPath,
                            Value        = ReadPropertyValue(sp),
                            Type         = PropertyTypeLabel(sp),
                        });
                    }
                }

                return McpResponse.Ok(new GetObjectPropertiesResultData
                {
                    ObjectPath    = req.ObjectPath,
                    ComponentType = req.ComponentType,
                    Properties    = properties.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/objects/set_properties
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetObjectPropertiesAsync(SetObjectPropertiesRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ObjectPath))
                    return McpResponse.Fail("INVALID_PARAMS", "object_path is required.");
                if (string.IsNullOrWhiteSpace(req.ComponentType))
                    return McpResponse.Fail("INVALID_PARAMS", "component_type is required.");

                var go = PlacementHandler.FindByPath(req.ObjectPath);
                if (go == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND",
                        $"No object at '{req.ObjectPath}'.");

                var type = ComponentCatalogBuilder.ResolveComponentType(req.ComponentType);
                if (type == null)
                    return McpResponse.Fail("TYPE_NOT_FOUND",
                        $"Unknown component type '{req.ComponentType}'.");

                var comp = go.GetComponent(type);
                if (comp == null)
                {
                    if (!req.CreateIfMissing)
                        return McpResponse.Fail("COMPONENT_NOT_FOUND",
                            $"'{req.ObjectPath}' has no {req.ComponentType}. " +
                            "Set create_if_missing=true to add it first.");
                    comp = Undo.AddComponent(go, type);
                    if (comp == null)
                        return McpResponse.Fail("ADD_FAILED",
                            $"Could not add {req.ComponentType} to '{req.ObjectPath}'.");
                }

                var warnings = new List<string>();
                var applied  = new List<string>();
                var failed   = new List<string>();

                if (req.Properties != null && req.Properties.Count > 0)
                {
                    var so = new SerializedObject(comp);
                    foreach (var pv in req.Properties)
                    {
                        if (string.IsNullOrEmpty(pv?.propertyPath))
                        { warnings.Add("Skipping entry with empty property_path."); continue; }

                        var sp = so.FindProperty(pv.propertyPath);
                        if (sp == null)
                        {
                            failed.Add(
                                $"'{pv.propertyPath}': not found on {req.ComponentType}. " +
                                "Use get_component_catalog to find valid property paths.");
                            continue;
                        }

                        if (SetProperty(sp, pv.value, out var err))
                            applied.Add(pv.propertyPath);
                        else
                            failed.Add($"'{pv.propertyPath}': {err}");
                    }
                    so.ApplyModifiedProperties();
                }

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Undo.SetCurrentGroupName($"MCP Set Properties on {go.name}");

                return McpResponse.Ok(new SetObjectPropertiesResultData
                {
                    ObjectPath        = req.ObjectPath,
                    ComponentType     = req.ComponentType,
                    AppliedProperties = applied.ToArray(),
                    FailedProperties  = failed.ToArray(),
                    Warnings          = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/objects/set_metadata
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetObjectMetadataAsync(SetObjectMetadataRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                var warnings = new List<string>();
                var applied  = new HashSet<string>();
                int updated  = 0;

                // Validate layer once if provided
                int? layerIndex = null;
                if (!string.IsNullOrEmpty(req.Layer))
                {
                    int l = LayerMask.NameToLayer(req.Layer);
                    if (l < 0)
                    {
                        warnings.Add($"Layer '{req.Layer}' not found — layer not applied.");
                    }
                    else
                    {
                        layerIndex = l;
                    }
                }

                // Parse static flags once
                StaticEditorFlags? parsedFlags = null;
                if (req.StaticFlags != null && req.StaticFlags.Length > 0)
                {
                    parsedFlags = ParseStaticFlags(req.StaticFlags, warnings);
                }

                Undo.SetCurrentGroupName("MCP Set Object Metadata");
                int undoGroup = Undo.GetCurrentGroup();

                for (int i = 0; i < req.ObjectPaths.Length; i++)
                {
                    var go = PlacementHandler.FindByPath(req.ObjectPaths[i]);
                    if (go == null)
                    {
                        warnings.Add($"Object not found: '{req.ObjectPaths[i]}'.");
                        continue;
                    }

                    Undo.RecordObject(go, "MCP Set Metadata");

                    // Name
                    if (!string.IsNullOrEmpty(req.Name))
                    {
                        go.name = req.ObjectPaths.Length == 1
                            ? req.Name
                            : $"{req.Name}_{i + 1:D2}";
                        applied.Add("name");
                    }

                    // Tag
                    if (!string.IsNullOrEmpty(req.Tag))
                    {
                        try   { go.tag = req.Tag; applied.Add("tag"); }
                        catch { warnings.Add($"Tag '{req.Tag}' does not exist. Create it in Project Settings → Tags."); }
                    }

                    // Layer
                    if (layerIndex.HasValue)
                    {
                        go.layer = layerIndex.Value;
                        applied.Add("layer");
                    }

                    // Static flags
                    if (parsedFlags.HasValue)
                    {
                        GameObjectUtility.SetStaticEditorFlags(go, parsedFlags.Value);
                        applied.Add("static_flags");
                    }

                    // Active state
                    if (req.IsActive.HasValue)
                    {
                        go.SetActive(req.IsActive.Value);
                        applied.Add("is_active");
                    }

                    EditorUtility.SetDirty(go);
                    updated++;
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new SetObjectMetadataResultData
                {
                    UpdatedCount = updated,
                    Applied      = applied.ToArray(),
                    Warnings     = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/objects/reparent
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ReparentObjectsAsync(ReparentObjectsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                // Resolve optional new parent
                Transform newParent = null;
                if (!string.IsNullOrEmpty(req.NewParentPath))
                {
                    var parentGo = PlacementHandler.FindByPath(req.NewParentPath);
                    if (parentGo == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at new_parent_path '{req.NewParentPath}'.");
                    newParent = parentGo.transform;
                }

                var warnings = new List<string>();
                int reparented = 0;

                Undo.SetCurrentGroupName("MCP Reparent Objects");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) { warnings.Add($"Object not found: '{path}'."); continue; }

                    // Guard against reparenting into own descendant
                    if (newParent != null && newParent.IsChildOf(go.transform))
                    {
                        warnings.Add(
                            $"Cannot reparent '{path}' — new parent is a descendant of it.");
                        continue;
                    }

                    Undo.SetTransformParent(go.transform, newParent,
                        req.KeepWorldPosition, "MCP Reparent");
                    EditorUtility.SetDirty(go);
                    reparented++;
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new ReparentObjectsResultData
                {
                    ReparentedCount = reparented,
                    Warnings        = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/objects/duplicate
        // -----------------------------------------------------------------------

        public static Task<McpResponse> DuplicateObjectsAsync(DuplicateObjectsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                int count = Mathf.Max(1, req.Count);

                // Resolve optional explicit parent
                Transform explicitParent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    var p = PlacementHandler.FindByPath(req.ParentPath);
                    if (p == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at parent_path '{req.ParentPath}'.");
                    explicitParent = p.transform;
                }

                var posOffset = ToV3(req.PositionOffset);
                var rotOffset = ToV3(req.RotationOffset);

                var warnings = new List<string>();
                var created  = new List<string>();

                Undo.SetCurrentGroupName("MCP Duplicate Objects");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var path in req.ObjectPaths)
                {
                    var source = PlacementHandler.FindByPath(path);
                    if (source == null) { warnings.Add($"Object not found: '{path}'."); continue; }

                    var parent = explicitParent ?? source.transform.parent;

                    for (int i = 0; i < count; i++)
                    {
                        var copy = UnityEngine.Object.Instantiate(source);
                        Undo.RegisterCreatedObjectUndo(copy, "MCP Duplicate");

                        // Position / rotation offset accumulated per copy
                        copy.transform.position    = source.transform.position + posOffset * (i + 1);
                        copy.transform.eulerAngles = source.transform.eulerAngles + rotOffset * (i + 1);
                        copy.transform.localScale  = source.transform.localScale;

                        // Name
                        if (!string.IsNullOrEmpty(req.NameSuffix))
                            copy.name = source.name + req.NameSuffix + (count > 1 ? $"_{i + 1:D2}" : "");
                        else
                            copy.name = source.name + (i == 0 ? "_Copy" : $"_Copy_{i + 1}");

                        // Parent (SetParent after position so worldPositionStays = true works correctly)
                        copy.transform.SetParent(parent, worldPositionStays: true);

                        EditorUtility.SetDirty(copy);
                        created.Add(PlacementHandler.GetPath(copy));
                    }
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new DuplicateObjectsResultData
                {
                    CreatedCount = created.Count,
                    Paths        = created.ToArray(),
                    Warnings     = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // SerializedProperty reading
        // -----------------------------------------------------------------------

        private static JToken ReadPropertyValue(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Float:    return sp.floatValue;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask: return sp.intValue;
                case SerializedPropertyType.Boolean:  return sp.boolValue;
                case SerializedPropertyType.String:   return sp.stringValue ?? string.Empty;

                case SerializedPropertyType.Enum:
                {
                    var names = sp.enumNames;
                    int idx   = sp.enumValueIndex;
                    return (idx >= 0 && idx < names.Length) ? names[idx] : (JToken)idx;
                }

                case SerializedPropertyType.Vector2:
                { var v = sp.vector2Value; return new JArray(v.x, v.y); }

                case SerializedPropertyType.Vector3:
                { var v = sp.vector3Value; return new JArray(v.x, v.y, v.z); }

                case SerializedPropertyType.Vector4:
                { var v = sp.vector4Value; return new JArray(v.x, v.y, v.z, v.w); }

                case SerializedPropertyType.Color:
                { var c = sp.colorValue; return new JArray(c.r, c.g, c.b, c.a); }

                case SerializedPropertyType.Bounds:
                {
                    var b = sp.boundsValue;
                    return new JArray(b.center.x, b.center.y, b.center.z,
                                      b.size.x,   b.size.y,   b.size.z);
                }

                case SerializedPropertyType.Rect:
                { var r = sp.rectValue; return new JArray(r.x, r.y, r.width, r.height); }

                case SerializedPropertyType.ObjectReference:
                {
                    if (sp.objectReferenceValue == null) return JValue.CreateNull();
                    var assetPath = AssetDatabase.GetAssetPath(sp.objectReferenceValue);
                    var guid      = AssetDatabase.AssetPathToGUID(assetPath);
                    return new JObject
                    {
                        ["asset_guid"] = guid,
                        ["asset_path"] = assetPath,
                        ["name"]       = sp.objectReferenceValue.name,
                    };
                }

                default:
                    // Return a string representation for unsupported complex types
                    return $"<{sp.propertyType}>";
            }
        }

        private static string PropertyTypeLabel(SerializedProperty sp)
        {
            return sp.propertyType switch
            {
                SerializedPropertyType.Enum =>
                    $"Enum:{sp.type}[{string.Join("|", sp.enumNames ?? Array.Empty<string>())}]",
                SerializedPropertyType.ObjectReference =>
                    $"ObjectReference:{sp.type}",
                _ =>
                    sp.propertyType.ToString(),
            };
        }

        // -----------------------------------------------------------------------
        // SerializedProperty writing  (mirrors ComponentHandler.SetProperty;
        // extract to a shared utility if both handlers need updating together)
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
                        { error = $"'{enumName}' is not valid. Options: {string.Join(", ", prop.enumNames ?? Array.Empty<string>())}"; return false; }
                        prop.enumValueIndex = idx; return true;

                    case SerializedPropertyType.Vector2:
                    { var a = ParseFloats(value, 2, out error); if (a == null) return false;
                      prop.vector2Value = new Vector2(a[0], a[1]); return true; }

                    case SerializedPropertyType.Vector3:
                    { var a = ParseFloats(value, 3, out error); if (a == null) return false;
                      prop.vector3Value = new Vector3(a[0], a[1], a[2]); return true; }

                    case SerializedPropertyType.Vector4:
                    { var a = ParseFloats(value, 4, out error); if (a == null) return false;
                      prop.vector4Value = new Vector4(a[0], a[1], a[2], a[3]); return true; }

                    case SerializedPropertyType.Color:
                    { var a = ParseFloats(value, 3, out error); if (a == null) return false;
                      prop.colorValue = new Color(a[0], a[1], a[2], a.Length >= 4 ? a[3] : 1f); return true; }

                    case SerializedPropertyType.Bounds:
                    { var a = ParseFloats(value, 6, out error); if (a == null) return false;
                      prop.boundsValue = new Bounds(new Vector3(a[0],a[1],a[2]), new Vector3(a[3],a[4],a[5])); return true; }

                    case SerializedPropertyType.Rect:
                    { var a = ParseFloats(value, 4, out error); if (a == null) return false;
                      prop.rectValue = new Rect(a[0], a[1], a[2], a[3]); return true; }

                    case SerializedPropertyType.ObjectReference:
                    {
                        if (value.Type != JTokenType.Object)
                        { error = "ObjectReference needs { \"asset_guid\": \"...\" }."; return false; }
                        var guidStr = value["asset_guid"]?.Value<string>();
                        if (string.IsNullOrEmpty(guidStr)) { prop.objectReferenceValue = null; return true; }
                        var assetPath = AssetDatabase.GUIDToAssetPath(guidStr);
                        if (string.IsNullOrEmpty(assetPath)) { error = $"No asset for GUID '{guidStr}'."; return false; }
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (asset == null) { error = $"Could not load asset at '{assetPath}'."; return false; }
                        prop.objectReferenceValue = asset; return true;
                    }

                    default:
                        error = $"Property type '{prop.propertyType}' is not supported.";
                        return false;
                }
            }
            catch (Exception ex) { error = $"Value conversion failed: {ex.Message}"; return false; }
        }

        private static float[] ParseFloats(JToken value, int minLen, out string error)
        {
            error = null;
            if (value.Type != JTokenType.Array)
            { error = $"Expected a JSON array of {minLen}+ numbers."; return null; }
            var arr = value.ToObject<float[]>();
            if (arr == null || arr.Length < minLen)
            { error = $"Array must have at least {minLen} elements."; return null; }
            return arr;
        }

        // -----------------------------------------------------------------------
        // Static flags parsing
        // -----------------------------------------------------------------------

        private static StaticEditorFlags? ParseStaticFlags(string[] names, List<string> warnings)
        {
            if (names == null || names.Length == 0) return null;

            if (Array.IndexOf(names, "All") >= 0)
                return (StaticEditorFlags)int.MaxValue;

            if (Array.IndexOf(names, "None") >= 0)
                return 0;

            StaticEditorFlags result = 0;
            foreach (var n in names)
            {
                // Unity 2022+ renamed LightmapStatic → ContributeGI
                var lookup = n == "LightmapStatic" ? "ContributeGI" : n;
                if (Enum.TryParse<StaticEditorFlags>(lookup, out var flag))
                    result |= flag;
                else if (Enum.TryParse<StaticEditorFlags>(n, out flag))
                    result |= flag;
                else
                    warnings.Add(
                        $"Unknown static flag '{n}'. Valid values: " +
                        string.Join(", ", Enum.GetNames(typeof(StaticEditorFlags))));
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Scene traversal
        // -----------------------------------------------------------------------

        private static IEnumerable<GameObject> AllSceneObjects(bool includeInactive)
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Transform>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None)
                .Select(t => t.gameObject);
#else
            return UnityEngine.Object.FindObjectsOfType<Transform>(includeInactive)
                .Select(t => t.gameObject);
#endif
        }

        // -----------------------------------------------------------------------
        // Data builder
        // -----------------------------------------------------------------------

        private static FindObjectData BuildObjectData(GameObject go)
        {
            var t = go.transform;
            return new FindObjectData
            {
                Name       = go.name,
                Path       = PlacementHandler.GetPath(go),
                Position   = V3(t.position),
                Rotation   = V3(t.eulerAngles),
                Scale      = V3(t.localScale),
                Tag        = go.tag,
                Layer      = LayerMask.LayerToName(go.layer),
                IsActive   = go.activeInHierarchy,
                Components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray(),
            };
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        private static bool MatchesGlob(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            var regex = "^" +
                Regex.Escape(pattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".") +
                "$";
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
        }

        private static Vector3 ToV3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        private static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
