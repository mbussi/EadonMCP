using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Components
{
    /// <summary>
    /// Builds the component catalog served by GET /assets/components/catalog.
    ///
    /// Probing strategy:
    ///   For each component type (built-in or user script), we create a temporary
    ///   hidden GameObject, add the component to it, and walk its SerializedObject
    ///   to extract the EXACT internal property paths, display names, types, and
    ///   default values that Unity uses internally.
    ///
    ///   This is the only reliable way to discover property paths for built-in
    ///   Unity components — their C# property names (Rigidbody.mass) do NOT always
    ///   match their serialized paths (m_Mass). Guessing fails silently.
    ///
    /// Two source pools:
    ///   1. Curated built-in list  — well-known Unity components, always included.
    ///   2. User MonoBehaviours    — discovered via TypeCache in project assemblies.
    ///
    /// Result is cached until domain reload.
    /// </summary>
    public static class ComponentCatalogBuilder
    {
        private static ComponentCatalogResponse _cached;

        // ------------------------------------------------------------------ //
        //  Curated built-in component list                                    //
        // ------------------------------------------------------------------ //

        private static readonly List<(Type type, string category)> BuiltInComponents =
            new List<(Type, string)>
        {
            // Physics 3D
            (typeof(Rigidbody),            "Physics"),
            (typeof(BoxCollider),          "Physics"),
            (typeof(SphereCollider),       "Physics"),
            (typeof(CapsuleCollider),      "Physics"),
            (typeof(MeshCollider),         "Physics"),
            (typeof(CharacterController),  "Physics"),
            (typeof(ConstantForce),        "Physics"),
            (typeof(HingeJoint),           "Physics"),

            // Physics 2D
            (typeof(Rigidbody2D),          "Physics2D"),
            (typeof(BoxCollider2D),        "Physics2D"),
            (typeof(CircleCollider2D),     "Physics2D"),
            (typeof(CapsuleCollider2D),    "Physics2D"),
            (typeof(PolygonCollider2D),    "Physics2D"),

            // Rendering
            (typeof(MeshFilter),           "Rendering"),
            (typeof(MeshRenderer),         "Rendering"),
            (typeof(SkinnedMeshRenderer),  "Rendering"),
            (typeof(LineRenderer),         "Rendering"),
            (typeof(TrailRenderer),        "Rendering"),
            (typeof(SpriteRenderer),       "Rendering"),
            (typeof(Light),                "Rendering"),
            (typeof(LensFlare),            "Rendering"),
            (typeof(Projector),            "Rendering"),
            (typeof(LODGroup),             "Rendering"),

            // Camera
            (typeof(Camera),               "Camera"),

            // Audio
            (typeof(AudioSource),          "Audio"),
            (typeof(AudioListener),        "Audio"),
            (typeof(AudioReverbZone),      "Audio"),

            // Animation
            (typeof(Animator),             "Animation"),
            (typeof(Animation),            "Animation"),

            // Navigation
            (typeof(UnityEngine.AI.NavMeshAgent),    "Navigation"),
            (typeof(UnityEngine.AI.NavMeshObstacle), "Navigation"),
            (typeof(UnityEngine.AI.OffMeshLink),     "Navigation"),

            // Effects
            (typeof(ParticleSystem),       "Effects"),
            (typeof(WindZone),             "Effects"),
        };

        // ------------------------------------------------------------------ //
        //  Public API                                                         //
        // ------------------------------------------------------------------ //

        public static ComponentCatalogResponse Build()
        {
            if (_cached != null) return _cached;

            var response = new ComponentCatalogResponse();

            // ── Probe GameObject to create a fresh temp scene object ──────── //
            var probeGo = new GameObject("__MCP_ComponentProbe");
            probeGo.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                // 1. Built-in curated list
                foreach (var (type, category) in BuiltInComponents)
                {
                    var info = ProbeComponent(type, category, "BuiltIn", null, probeGo);
                    if (info != null)
                        response.components.Add(info);
                }

                // 2. User MonoBehaviour scripts (project assemblies only)
                foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                {
                    if (!IsUserScript(type)) continue;
                    if (type.IsAbstract)     continue;

                    var scriptPath = GetScriptPath(type);
                    var info = ProbeComponent(type, "UserScript", "UserScript", scriptPath, probeGo);
                    if (info != null)
                        response.components.Add(info);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probeGo);
            }

            // Sort: built-ins by category, then user scripts alphabetically
            response.components.Sort((a, b) =>
            {
                if (a.source != b.source)
                    return string.Compare(a.source, b.source, StringComparison.Ordinal);
                if (a.category != b.category)
                    return string.Compare(a.category, b.category, StringComparison.Ordinal);
                return string.Compare(a.typeName, b.typeName, StringComparison.Ordinal);
            });

            _cached = response;
            return response;
        }

        public static void InvalidateCache() => _cached = null;

        // ------------------------------------------------------------------ //
        //  Type resolution — by simple class name                            //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Finds a component Type by its simple class name.
        /// Checks built-ins first, then project scripts via TypeCache.
        /// Returns null if not found.
        /// </summary>
        public static Type ResolveComponentType(string typeName)
        {
            // Check curated built-ins first (fastest path)
            foreach (var (type, _) in BuiltInComponents)
                if (type.Name == typeName) return type;

            // Fall back to TypeCache for user scripts and any unlisted Unity types
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
                if (type.Name == typeName) return type;

            // Last resort: Type.GetType for fully-qualified names
            return Type.GetType(typeName);
        }

        // ------------------------------------------------------------------ //
        //  Component probing                                                  //
        // ------------------------------------------------------------------ //

        private static ComponentInfo ProbeComponent(
            Type       type,
            string     category,
            string     source,
            string     scriptPath,
            GameObject probeGo)
        {
            Component comp = null;
            try
            {
                comp = probeGo.AddComponent(type);
                if (comp == null) return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentCatalogBuilder] Could not add {type.Name} to probe: {ex.Message}");
                return null;
            }

            var info = new ComponentInfo
            {
                typeName         = type.Name,
                displayName      = ObjectNames.NicifyVariableName(type.Name),
                category         = category,
                source           = source,
                scriptPath       = scriptPath,
                disallowMultiple = type.IsDefined(typeof(DisallowMultipleComponent), inherit: true),
                properties       = ProbeProperties(comp),
            };

            // Clean up — remove the component from the probe object so
            // the next iteration can add a fresh instance
            UnityEngine.Object.DestroyImmediate(comp);

            return info;
        }

        private static List<ComponentPropertyInfo> ProbeProperties(Component comp)
        {
            var results = new List<ComponentPropertyInfo>();

            SerializedObject so;
            try { so = new SerializedObject(comp); }
            catch { return results; }

            var iter = so.GetIterator();

            // Enter the first level of children
            if (!iter.NextVisible(enterChildren: true)) return results;

            // Walk siblings (enterChildren: false keeps us at the same depth,
            // so Vector3 appears as one property, not as x/y/z sub-properties)
            do
            {
                // Skip Unity's internal bookkeeping properties
                if (IsInternalProperty(iter.name)) continue;

                // Only emit top-level properties (depth 1).
                // Depth 0 is the root; depth 2+ would be nested struct fields.
                if (iter.depth != 1) continue;

                var propInfo = BuildPropertyInfo(iter);
                if (propInfo != null) results.Add(propInfo);

            } while (iter.NextVisible(enterChildren: false));

            return results;
        }

        private static ComponentPropertyInfo BuildPropertyInfo(SerializedProperty prop)
        {
            var info = new ComponentPropertyInfo
            {
                propertyPath = prop.propertyPath,
                displayName  = prop.displayName,
                depth        = prop.depth,
                type         = GetTypeLabel(prop),
                defaultValue = GetDefaultValueString(prop),
            };

            // For enums, capture valid value names so the agent knows what to pass
            if (prop.propertyType == SerializedPropertyType.Enum)
                info.enumNames = prop.enumDisplayNames ?? prop.enumNames;

            // Skip properties we can't set at all (e.g. Generic arrays without
            // a concrete element type — they'd always fail)
            if (prop.propertyType == SerializedPropertyType.Generic &&
                !prop.isArray)
                return null;

            return info;
        }

        // ------------------------------------------------------------------ //
        //  Type label                                                         //
        // ------------------------------------------------------------------ //

        private static string GetTypeLabel(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Float           => "Float",
                SerializedPropertyType.Integer         => "Integer",
                SerializedPropertyType.Boolean         => "Boolean",
                SerializedPropertyType.String          => "String",
                SerializedPropertyType.Vector2         => "Vector2",
                SerializedPropertyType.Vector3         => "Vector3",
                SerializedPropertyType.Vector4         => "Vector4",
                SerializedPropertyType.Color           => "Color",
                SerializedPropertyType.Bounds          => "Bounds",
                SerializedPropertyType.Rect            => "Rect",
                SerializedPropertyType.LayerMask       => "LayerMask",
                SerializedPropertyType.AnimationCurve  => "AnimationCurve",
                SerializedPropertyType.Enum            => $"Enum:{prop.type}",
                SerializedPropertyType.ObjectReference =>
                    $"ObjectReference:{prop.type.TrimStart('P', 'P', 't', 'r')}",
                SerializedPropertyType.Generic when prop.isArray =>
                    $"Array",
                _                                     => prop.propertyType.ToString(),
            };
        }

        private static string GetDefaultValueString(SerializedProperty prop)
        {
            try
            {
                return prop.propertyType switch
                {
                    SerializedPropertyType.Float    => prop.floatValue.ToString("G"),
                    SerializedPropertyType.Integer  => prop.intValue.ToString(),
                    SerializedPropertyType.Boolean  => prop.boolValue.ToString().ToLower(),
                    SerializedPropertyType.String   => prop.stringValue ?? "",
                    SerializedPropertyType.Enum     =>
                        prop.enumNames?.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                            ? prop.enumNames[prop.enumValueIndex] : "0",
                    SerializedPropertyType.Vector2  =>
                        $"[{prop.vector2Value.x:G},{prop.vector2Value.y:G}]",
                    SerializedPropertyType.Vector3  =>
                        $"[{prop.vector3Value.x:G},{prop.vector3Value.y:G},{prop.vector3Value.z:G}]",
                    SerializedPropertyType.Color    =>
                        $"[{prop.colorValue.r:G3},{prop.colorValue.g:G3}," +
                        $"{prop.colorValue.b:G3},{prop.colorValue.a:G3}]",
                    SerializedPropertyType.LayerMask => prop.intValue.ToString(),
                    _                               => "",
                };
            }
            catch
            {
                return "";
            }
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        private static readonly HashSet<string> InternalPropertyNames = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance",
            "m_PrefabAsset", "m_PrefabAssetType", "m_GameObject",
            "m_Enabled", "m_Script",
        };

        private static bool IsInternalProperty(string name) =>
            InternalPropertyNames.Contains(name);

        private static bool IsUserScript(Type type)
        {
            var asmName = type.Assembly.FullName ?? "";
            return !asmName.StartsWith("UnityEngine", StringComparison.Ordinal)
                && !asmName.StartsWith("UnityEditor",  StringComparison.Ordinal)
                && !asmName.StartsWith("Unity.",        StringComparison.Ordinal)
                && !asmName.StartsWith("System",        StringComparison.Ordinal)
                && !asmName.StartsWith("mscorlib",      StringComparison.Ordinal)
                && !asmName.StartsWith("netstandard",   StringComparison.Ordinal);
        }

        private static string GetScriptPath(Type type)
        {
            var guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
            foreach (var guid in guids)
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script?.GetClass() == type) return path;
            }
            return null;
        }
    }

    // ====================================================================== //
    //  Cache invalidation on domain reload                                   //
    // ====================================================================== //

    [InitializeOnLoad]
    internal static class ComponentCatalogCacheInvalidator
    {
        static ComponentCatalogCacheInvalidator()
        {
            AssemblyReloadEvents.afterAssemblyReload +=
                ComponentCatalogBuilder.InvalidateCache;
        }
    }
}
