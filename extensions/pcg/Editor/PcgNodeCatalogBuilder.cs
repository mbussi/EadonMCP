using System;
using System.Collections.Generic;
using System.Reflection;
using PCGFramework.PCGG;
using PCGFramework.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.PCG
{
    /// <summary>
    /// Builds the PCG node catalog served by GET /assets/pcg/node-catalog.
    ///
    /// Uses TypeCache (fast, compiler-generated) to discover all concrete
    /// PCGNode subclasses in the project.  For each type it extracts:
    ///   - Display metadata from the [PCGNode] attribute
    ///   - Node classification from PCGGNodeRegistry (Algorithm / SceneBinding / EditorOnly)
    ///   - Port descriptors from [PCGInput] / [PCGOutput] fields
    ///   - Parameter descriptors from remaining public fields, with default values
    ///     read from a freshly-created default instance
    ///
    /// The result is cached after the first call and invalidated by
    /// InvalidateCache() (called on domain reload by [InitializeOnLoad] in
    /// PcgMcpCacheInvalidator, below).
    /// </summary>
    public static class PcgNodeCatalogBuilder
    {
        private static PcgNodeCatalogResponse _cached;

        // ------------------------------------------------------------------ //
        //  Public API                                                         //
        // ------------------------------------------------------------------ //

        public static PcgNodeCatalogResponse Build()
        {
            if (_cached != null) return _cached;

            var response = new PcgNodeCatalogResponse();

            foreach (var type in TypeCache.GetTypesDerivedFrom<PCGNode>())
            {
                if (type.IsAbstract) continue;

                // PCGCommentNode and similar internal nodes are tagged [HideFromSearch]
                if (type.IsDefined(typeof(HideFromSearchAttribute), inherit: false)) continue;

                response.nodes.Add(BuildNodeInfo(type));
            }

            // Sort by category then display name for a predictable catalog order
            response.nodes.Sort((a, b) =>
            {
                var cat = string.Compare(a.category, b.category, StringComparison.Ordinal);
                return cat != 0 ? cat : string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
            });

            _cached = response;
            return response;
        }

        public static void InvalidateCache() => _cached = null;

        // ------------------------------------------------------------------ //
        //  Node info                                                          //
        // ------------------------------------------------------------------ //

        private static PcgNodeInfo BuildNodeInfo(Type type)
        {
            var nodeAttr = type.GetCustomAttribute<PCGNodeAttribute>();
            var regEntry = PCGGNodeRegistry.GetByUnityType(type);

            var info = new PcgNodeInfo
            {
                canonicalName = regEntry?.CanonicalName ?? type.Name,
                unityTypeName = type.Name,
                displayName   = nodeAttr?.DisplayName ?? type.Name,
                category      = nodeAttr?.Category    ?? "Uncategorised",
                tooltip       = nodeAttr?.Tooltip      ?? string.Empty,
                nodeClass     = regEntry != null ? regEntry.Class.ToString() : "Unregistered",
                mutatesInput  = type.IsDefined(typeof(PCGMutatesInputAttribute), inherit: false),
            };

            // Instantiate a default instance to read default field values.
            // DestroyImmediate at the end so no ScriptableObjects leak into the scene.
            PCGNode defaultInstance = null;
            try
            {
                defaultInstance = (PCGNode)ScriptableObject.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PcgNodeCatalogBuilder] Could not create default instance " +
                                 $"of {type.Name}: {ex.Message}");
            }

            // Walk all public instance fields — classify into ports vs parameters
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
            {
                bool isInput  = field.IsDefined(typeof(PCGInputAttribute),  inherit: true);
                bool isOutput = field.IsDefined(typeof(PCGOutputAttribute),  inherit: true);

                if (isInput)
                {
                    info.inputs.Add(BuildPortInfo(field, isInput: true));
                }
                else if (isOutput)
                {
                    info.outputs.Add(BuildPortInfo(field, isInput: false));
                }
                else
                {
                    var param = BuildParamInfo(field, defaultInstance);
                    if (param != null) info.parameters.Add(param);
                }
            }

            if (defaultInstance != null)
                UnityEngine.Object.DestroyImmediate(defaultInstance);

            return info;
        }

        // ------------------------------------------------------------------ //
        //  Port descriptor                                                    //
        // ------------------------------------------------------------------ //

        private static PcgPortInfo BuildPortInfo(FieldInfo field, bool isInput)
        {
            string overrideName = isInput
                ? field.GetCustomAttribute<PCGInputAttribute>()?.DisplayName
                : field.GetCustomAttribute<PCGOutputAttribute>()?.DisplayName;

            return new PcgPortInfo
            {
                fieldName   = field.Name,
                displayName = !string.IsNullOrEmpty(overrideName)
                              ? overrideName
                              : NicifyName(field.Name),
                portType    = field.FieldType.Name,
            };
        }

        // ------------------------------------------------------------------ //
        //  Parameter descriptor                                               //
        // ------------------------------------------------------------------ //

        private static PcgParamInfo BuildParamInfo(FieldInfo field, PCGNode defaultInstance)
        {
            // Skip Unity-internal identity fields
            if (field.Name is "NodeGuid" or "GraphPosition" or "EditorColor") return null;

            // Skip explicitly hidden fields
            if (field.IsDefined(typeof(HideInInspector), inherit: false)) return null;

            // Skip [NonSerialized] fields
            if (field.IsDefined(typeof(NonSerializedAttribute), inherit: false)) return null;

            // Skip UnityEngine.Object references and complex generic collections —
            // these are scene/asset bindings that cannot round-trip through PCGG JSON.
            // They are noted in the catalog tooltip as "requires manual assignment".
            var ft = field.FieldType;
            if (typeof(UnityEngine.Object).IsAssignableFrom(ft)) return null;
            if (ft.IsGenericType) return null;   // List<PCGPrefabEntry>, etc.

            // Restrict to value types that PCGGFieldSerializer can handle
            if (!IsSupportedFieldType(ft)) return null;

            var tooltipAttr = field.GetCustomAttribute<TooltipAttribute>();
            var rangeAttr   = field.GetCustomAttribute<RangeAttribute>();
            var minAttr     = field.GetCustomAttribute<MinAttribute>();

            var param = new PcgParamInfo
            {
                fieldName   = field.Name,
                displayName = NicifyName(field.Name),
                type        = TypeLabel(ft),
                tooltip     = tooltipAttr?.tooltip ?? string.Empty,
                rangeHint   = rangeAttr != null
                              ? $"{rangeAttr.min}–{rangeAttr.max}"
                              : (minAttr != null ? $"≥{minAttr.min}" : string.Empty),
            };

            // Read the C# default value from the temporary instance
            if (defaultInstance != null)
            {
                try
                {
                    var rawValue = field.GetValue(defaultInstance);
                    param.defaultValue = FormatDefault(rawValue, ft);
                }
                catch
                {
                    param.defaultValue = string.Empty;
                }
            }

            return param;
        }

        // ------------------------------------------------------------------ //
        //  Type helpers                                                       //
        // ------------------------------------------------------------------ //

        private static bool IsSupportedFieldType(Type t)
        {
            return t == typeof(float)          ||
                   t == typeof(int)            ||
                   t == typeof(bool)           ||
                   t == typeof(string)         ||
                   t == typeof(Vector2)        ||
                   t == typeof(Vector3)        ||
                   t == typeof(Color)          ||
                   t == typeof(Bounds)         ||
                   t == typeof(LayerMask)      ||
                   t == typeof(AnimationCurve) ||
                   t.IsEnum;
        }

        private static string TypeLabel(Type t)
        {
            if (t == typeof(float))          return "float";
            if (t == typeof(int))            return "int";
            if (t == typeof(bool))           return "bool";
            if (t == typeof(string))         return "string";
            if (t == typeof(Vector2))        return "vector2";
            if (t == typeof(Vector3))        return "vector3";
            if (t == typeof(Color))          return "color";
            if (t == typeof(Bounds))         return "bounds";
            if (t == typeof(LayerMask))      return "layermask";
            if (t == typeof(AnimationCurve)) return "curve";
            if (t.IsEnum)
                return $"enum:{t.Name}[{string.Join("|", Enum.GetNames(t))}]";
            return t.Name;
        }

        private static string FormatDefault(object v, Type t)
        {
            if (v == null) return string.Empty;
            if (t == typeof(float))   return ((float)v).ToString("G");
            if (t == typeof(int))     return v.ToString();
            if (t == typeof(bool))    return v.ToString().ToLower();
            if (t == typeof(string))  return (string)v ?? string.Empty;
            if (t.IsEnum)             return v.ToString();
            if (t == typeof(Vector2)) { var u = (Vector2)v; return $"[{u.x:G},{u.y:G}]"; }
            if (t == typeof(Vector3)) { var u = (Vector3)v; return $"[{u.x:G},{u.y:G},{u.z:G}]"; }
            if (t == typeof(Color))   { var u = (Color)v;   return $"rgba({u.r:G3},{u.g:G3},{u.b:G3},{u.a:G3})"; }
            if (t == typeof(Bounds))
            {
                var u = (Bounds)v;
                return $"center:[{u.center.x:G},{u.center.y:G},{u.center.z:G}] " +
                       $"size:[{u.size.x:G},{u.size.y:G},{u.size.z:G}]";
            }
            return v.ToString();
        }

        // Mirrors ObjectNames.NicifyVariableName without the Editor namespace import
        private static string NicifyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("m_", StringComparison.Ordinal)) name = name[2..];
            if (name.StartsWith("_",  StringComparison.Ordinal)) name = name[1..];

            var sb = new System.Text.StringBuilder(name.Length + 4);
            sb.Append(char.ToUpper(name[0]));
            for (var i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }
    }

    // ====================================================================== //
    //  Cache invalidation on domain reload                                   //
    // ====================================================================== //

    /// <summary>
    /// Clears the catalog cache after every script recompile so that newly
    /// added node types are picked up without requiring a Unity restart.
    /// </summary>
    [InitializeOnLoad]
    internal static class PcgMcpCacheInvalidator
    {
        static PcgMcpCacheInvalidator()
        {
            // AssemblyReloadEvents fires after the new domain is set up,
            // ensuring TypeCache is populated before the next Build() call.
            AssemblyReloadEvents.afterAssemblyReload += PcgNodeCatalogBuilder.InvalidateCache;
        }
    }
}
