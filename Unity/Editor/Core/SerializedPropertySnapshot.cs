using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>One serialized leaf: a SerializedProperty path and its culture-invariant string value.</summary>
    [Serializable]
    internal class PropertyValueEntry
    {
        public string path;
        public string value;
    }

    /// <summary>
    /// Flattens a SerializedObject into (path, value) leaves and writes them back.
    /// This is how per-profile pins of Unity objects (e.g. OpenXR features) are
    /// stored readably: one YAML line per property instead of an opaque JSON dump.
    /// Only visible properties are captured; compound value types (vectors,
    /// colors, structs, lists) decompose into their leaves; array sizes are
    /// recorded before their elements and applied in recorded order so lists
    /// resize correctly. Values are culture-invariant strings; asset references
    /// are GlobalObjectId strings ("" for null).
    /// </summary>
    internal static class SerializedPropertySnapshot
    {
        const string LogPrefix = "[Build Forge]";

        enum Kind { Leaf, Container, Unsupported }

        public static List<PropertyValueEntry> Capture(SerializedObject so, ISet<string> skipTopLevel)
        {
            var result = new List<PropertyValueEntry>();
            var it = so.GetIterator();
            var enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                if (it.depth == 0 && skipTopLevel != null && skipTopLevel.Contains(it.name))
                {
                    enterChildren = false;
                    continue;
                }

                switch (Classify(it.propertyType))
                {
                    case Kind.Leaf:
                        result.Add(new PropertyValueEntry { path = it.propertyPath, value = Encode(it) });
                        // Strings are arrays of characters; never descend into a leaf.
                        enterChildren = false;
                        break;
                    case Kind.Container:
                        enterChildren = true;
                        break;
                    default:
                        Debug.LogWarning($"{LogPrefix} {so.targetObject.name}.{it.propertyPath}: " +
                            $"property type {it.propertyType} is not supported and was skipped.");
                        enterChildren = false;
                        break;
                }
            }

            return result;
        }

        public static void Apply(SerializedObject so, IReadOnlyList<PropertyValueEntry> entries)
        {
            foreach (var entry in entries)
            {
                var prop = so.FindProperty(entry.path);
                if (prop == null)
                {
                    Debug.LogWarning($"{LogPrefix} {so.targetObject.name}.{entry.path}: property no longer exists " +
                        "(package upgrade?) — value skipped.");
                    continue;
                }

                if (!TryDecodeInto(prop, entry.value))
                    Debug.LogWarning($"{LogPrefix} {so.targetObject.name}.{entry.path}: could not apply value " +
                        $"'{entry.value}' to a {prop.propertyType} property — skipped.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Kind Classify(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.RenderingLayerMask:
                // Color children (r/g/b/a) are not visible to NextVisible, so it is
                // encoded as one leaf rather than decomposed like vectors.
                case SerializedPropertyType.Color:
                    return Kind.Leaf;

                case SerializedPropertyType.Generic:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    return Kind.Container;

                default:
                    return Kind.Unsupported;
            }
        }

        /// <summary>Leaf value as a culture-invariant string; null for unsupported types.</summary>
        public static string Encode(SerializedProperty p)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return p.numericType == SerializedPropertyNumericType.UInt64
                        ? p.ulongValue.ToString(inv)
                        : p.longValue.ToString(inv);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "1" : "0";
                case SerializedPropertyType.Float:
                    return p.numericType == SerializedPropertyNumericType.Double
                        ? p.doubleValue.ToString("R", inv)
                        : p.floatValue.ToString("R", inv);
                case SerializedPropertyType.String:
                    return p.stringValue ?? "";
                case SerializedPropertyType.Enum:
                    // The underlying value, never enumValueIndex (display order).
                    return p.intValue.ToString(inv);
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null
                        ? ""
                        : GlobalObjectId.GetGlobalObjectIdSlow(p.objectReferenceValue).ToString();
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                    return p.intValue.ToString(inv);
                case SerializedPropertyType.RenderingLayerMask:
                    return p.uintValue.ToString(inv);
                case SerializedPropertyType.Color:
                    var c = p.colorValue;
                    return string.Join(",",
                        c.r.ToString("R", inv), c.g.ToString("R", inv), c.b.ToString("R", inv), c.a.ToString("R", inv));
                default:
                    return null;
            }
        }

        /// <summary>Writes a string produced by <see cref="Encode"/> into the property. False when it cannot be parsed.</summary>
        public static bool TryDecodeInto(SerializedProperty p, string value)
        {
            var inv = CultureInfo.InvariantCulture;
            value ??= "";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (p.numericType == SerializedPropertyNumericType.UInt64)
                    {
                        if (!ulong.TryParse(value, NumberStyles.Integer, inv, out var u)) return false;
                        p.ulongValue = u;
                        return true;
                    }
                    if (!long.TryParse(value, NumberStyles.Integer, inv, out var l)) return false;
                    p.longValue = l;
                    return true;

                case SerializedPropertyType.Boolean:
                    if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { p.boolValue = true; return true; }
                    if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { p.boolValue = false; return true; }
                    return false;

                case SerializedPropertyType.Float:
                    if (!double.TryParse(value, NumberStyles.Float, inv, out var d)) return false;
                    if (p.numericType == SerializedPropertyNumericType.Double) p.doubleValue = d;
                    else p.floatValue = (float)d;
                    return true;

                case SerializedPropertyType.String:
                    p.stringValue = value;
                    return true;

                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                    if (!int.TryParse(value, NumberStyles.Integer, inv, out var i)) return false;
                    p.intValue = i;
                    return true;

                case SerializedPropertyType.RenderingLayerMask:
                    if (!uint.TryParse(value, NumberStyles.Integer, inv, out var ui)) return false;
                    p.uintValue = ui;
                    return true;

                case SerializedPropertyType.Color:
                    var parts = value.Split(',');
                    if (parts.Length != 4) return false;
                    var channels = new float[4];
                    for (int k = 0; k < 4; k++)
                    {
                        if (!float.TryParse(parts[k], NumberStyles.Float, inv, out channels[k])) return false;
                    }
                    p.colorValue = new Color(channels[0], channels[1], channels[2], channels[3]);
                    return true;

                case SerializedPropertyType.ObjectReference:
                    if (value.Length == 0)
                    {
                        p.objectReferenceValue = null;
                        return true;
                    }
                    if (!GlobalObjectId.TryParse(value, out var id)) return false;
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                    if (obj == null)
                        Debug.LogWarning($"{LogPrefix} {p.propertyPath}: referenced asset {value} was not found — reference cleared.");
                    p.objectReferenceValue = obj;
                    return true;

                default:
                    return false;
            }
        }
    }
}
