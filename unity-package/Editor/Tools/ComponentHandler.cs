using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Components;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Routes to register in McpRouter:
    ///   GET  /assets/components/catalog  → GetComponentCatalogAsync()
    ///   POST /tools/components/add       → AddComponentAsync(req)
    /// </summary>
    public static class ComponentHandler
    {
        // -----------------------------------------------------------------------
        // GET /assets/components/catalog
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetComponentCatalogAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
                McpResponse.Ok(ComponentCatalogBuilder.Build()));

        // -----------------------------------------------------------------------
        // POST /tools/components/add
        // -----------------------------------------------------------------------

        public static Task<McpResponse> AddComponentAsync(AddComponentRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.objectPath))
                    return McpResponse.Fail("INVALID_PARAMS", "object_path must not be empty.");
                if (string.IsNullOrWhiteSpace(req.componentTypeName))
                    return McpResponse.Fail("INVALID_PARAMS", "component_type_name must not be empty.");

                var go = PlacementHandler.FindByPath(req.objectPath);
                if (go == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND", $"No object at path '{req.objectPath}'.");

                var componentType = ComponentCatalogBuilder.ResolveComponentType(req.componentTypeName);
                if (componentType == null)
                    return McpResponse.Fail("TYPE_NOT_FOUND",
                        $"Unknown component type '{req.componentTypeName}'. Call get_component_catalog for valid type names.");

                var response = new AddComponentResponse
                {
                    objectPath        = PlacementHandler.GetPath(go),
                    componentTypeName = req.componentTypeName,
                };

                // Find or add the component
                Component comp;
                var existing = go.GetComponent(componentType);
                if (existing != null)
                {
                    if (!req.updateIfExists)
                        return McpResponse.Fail("ALREADY_EXISTS",
                            $"'{req.objectPath}' already has {req.componentTypeName}. Set update_if_exists=true to modify it.");
                    comp = existing;
                    response.updatedExisting = true;
                }
                else
                {
                    try { comp = Undo.AddComponent(go, componentType); }
                    catch (Exception ex)
                    { return McpResponse.Fail("ADD_FAILED", $"Failed to add {req.componentTypeName}: {ex.Message}"); }

                    if (comp == null)
                        return McpResponse.Fail("ADD_FAILED",
                            $"Undo.AddComponent returned null for {req.componentTypeName}. Check for unmet dependencies.");
                }

                // Set properties via SerializedObject
                if (req.properties != null && req.properties.Count > 0)
                {
                    var so = new SerializedObject(comp);
                    foreach (var pv in req.properties)
                    {
                        if (string.IsNullOrEmpty(pv?.propertyPath))
                        { response.warnings.Add("Skipping entry with empty property_path."); continue; }

                        var prop = so.FindProperty(pv.propertyPath);
                        if (prop == null)
                        {
                            response.failedProperties.Add(
                                $"'{pv.propertyPath}': not found on {req.componentTypeName}. " +
                                "Verify path against get_component_catalog → properties[].property_path.");
                            continue;
                        }

                        if (SetProperty(prop, pv.value, out var setError))
                            response.appliedProperties.Add(pv.propertyPath);
                        else
                            response.failedProperties.Add($"'{pv.propertyPath}': {setError}");
                    }
                    so.ApplyModifiedProperties();
                }

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Undo.SetCurrentGroupName($"MCP Add {req.componentTypeName} to {go.name}");

                return McpResponse.Ok(response);
            });

        // -----------------------------------------------------------------------
        // SerializedProperty setter
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

                    case SerializedPropertyType.Bounds:
                        var vb = ParseFloats(value, 6, out error); if (vb == null) return false;
                        prop.boundsValue = new Bounds(new Vector3(vb[0], vb[1], vb[2]), new Vector3(vb[3], vb[4], vb[5])); return true;

                    case SerializedPropertyType.Rect:
                        var vr = ParseFloats(value, 4, out error); if (vr == null) return false;
                        prop.rectValue = new Rect(vr[0], vr[1], vr[2], vr[3]); return true;

                    case SerializedPropertyType.ObjectReference:
                        if (value.Type != JTokenType.Object)
                        { error = "ObjectReference needs { \"asset_guid\": \"...\" }."; return false; }
                        var guidStr   = value["asset_guid"]?.Value<string>();
                        if (string.IsNullOrEmpty(guidStr)) { prop.objectReferenceValue = null; return true; }
                        var assetPath = AssetDatabase.GUIDToAssetPath(guidStr);
                        if (string.IsNullOrEmpty(assetPath))
                        { error = $"No asset for GUID '{guidStr}'."; return false; }
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (asset == null) { error = $"Failed to load asset at '{assetPath}'."; return false; }
                        prop.objectReferenceValue = asset; return true;

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
            { error = $"Expected a JSON array of {minLength}+ numbers."; return null; }
            var arr = value.ToObject<float[]>();
            if (arr == null || arr.Length < minLength)
            { error = $"Array must have at least {minLength} elements."; return null; }
            return arr;
        }
    }
}
