using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Serializes and deserializes built-in Unity components (Colliders, Rigidbody, etc.)
    /// using the SerializedObject/SerializedProperty API — the same mechanism the Inspector uses.
    /// Keeps all SerializedObject logic out of SceneIO.
    /// </summary>
    internal static class BuiltInComponentSerializer
    {
        public static bool ShouldInclude(Component component)
        {
            if (component == null) return false;
            if (component is Transform) return false;
            if (component is EntitySync) return false;
            if (component is MonoBehaviour) return false; // handled by customData
            if (component is Renderer) return false;
            if (component is MeshFilter) return false;
            return true;
        }

        public static JArray SerializeAll(GameObject go)
        {
            var result = new JArray();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (!ShouldInclude(comp)) continue;
                var obj = Serialize(comp);
                if (obj != null)
                    result.Add(obj);
            }
            return result;
        }

        public static JObject Serialize(Component component)
        {
            var obj = new JObject();
            obj["type"] = component.GetType().FullName;

            var so = new SerializedObject(component);
            var iter = so.GetIterator();

            for (bool enter = true; iter.NextVisible(enter); enter = false)
            {
                if (iter.name == "m_Script") continue;
                try
                {
                    var token = SerializeProp(iter);
                    if (token != null)
                        obj[iter.name] = token;
                }
                catch { /* skip unreadable properties */ }
            }

            return obj;
        }

        private static JToken SerializeProp(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.Enum:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    return new JArray(v.x, v.y);
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    return new JArray(v.x, v.y, v.z);
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = prop.vector4Value;
                    return new JArray(v.x, v.y, v.z, v.w);
                }
                case SerializedPropertyType.Quaternion:
                {
                    var q = prop.quaternionValue;
                    return new JArray(q.x, q.y, q.z, q.w);
                }
                case SerializedPropertyType.Color:
                {
                    var c = prop.colorValue;
                    return new JArray(c.r, c.g, c.b, c.a);
                }
                case SerializedPropertyType.Rect:
                {
                    var r = prop.rectValue;
                    return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };
                }
                case SerializedPropertyType.Bounds:
                {
                    var b = prop.boundsValue;
                    return new JObject
                    {
                        ["center"] = new JArray(b.center.x, b.center.y, b.center.z),
                        ["size"]   = new JArray(b.size.x,   b.size.y,   b.size.z),
                    };
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var asset = prop.objectReferenceValue;
                    if (asset == null) return null;
                    string path = AssetDatabase.GetAssetPath(asset);
                    // Skip built-in Unity assets that aren't loadable via AssetDatabase
                    if (string.IsNullOrEmpty(path) || path.StartsWith("Resources/", StringComparison.Ordinal))
                        return null;
                    return new JValue(path);
                }
                case SerializedPropertyType.Generic:
                    return SerializeGeneric(prop);
                default:
                    return null;
            }
        }

        private static JToken SerializeGeneric(SerializedProperty prop)
        {
            if (prop.isArray)
            {
                var arr = new JArray();
                int cap = Math.Min(prop.arraySize, 256);
                for (int i = 0; i < cap; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    arr.Add(SerializeProp(elem) ?? JValue.CreateNull());
                }
                return arr;
            }

            // Struct — iterate immediate children
            var obj = new JObject();
            var copy = prop.Copy();
            var end = prop.GetEndProperty();
            int childDepth = prop.depth + 1;

            if (!copy.Next(true)) return null;

            while (!SerializedProperty.EqualContents(copy, end))
            {
                if (copy.depth == childDepth)
                {
                    try
                    {
                        var token = SerializeProp(copy);
                        if (token != null)
                            obj[copy.name] = token;
                    }
                    catch { /* skip */ }
                }
                if (!copy.Next(false)) break;
            }

            return obj.Count > 0 ? obj : null;
        }

        public static void Deserialize(Component component, JObject data)
        {
            var so = new SerializedObject(component);

            foreach (var kvp in data)
            {
                if (kvp.Key == "type") continue;
                var prop = so.FindProperty(kvp.Key);
                if (prop == null) continue;

                try { ApplyProp(prop, kvp.Value); }
                catch { /* skip unwritable properties */ }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static void ApplyProp(SerializedProperty prop, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.Enum:
                    prop.intValue = token.Value<int>();
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = token.Value<bool>();
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = token.Value<float>();
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = token.Value<string>() ?? string.Empty;
                    break;
                case SerializedPropertyType.Vector2:
                    if (token is JArray v2)
                        prop.vector2Value = new Vector2(v2[0].Value<float>(), v2[1].Value<float>());
                    break;
                case SerializedPropertyType.Vector3:
                    if (token is JArray v3)
                        prop.vector3Value = new Vector3(v3[0].Value<float>(), v3[1].Value<float>(), v3[2].Value<float>());
                    break;
                case SerializedPropertyType.Vector4:
                    if (token is JArray v4)
                        prop.vector4Value = new Vector4(v4[0].Value<float>(), v4[1].Value<float>(), v4[2].Value<float>(), v4[3].Value<float>());
                    break;
                case SerializedPropertyType.Quaternion:
                    if (token is JArray q)
                        prop.quaternionValue = new Quaternion(q[0].Value<float>(), q[1].Value<float>(), q[2].Value<float>(), q[3].Value<float>());
                    break;
                case SerializedPropertyType.Color:
                    if (token is JArray c)
                        prop.colorValue = new Color(c[0].Value<float>(), c[1].Value<float>(), c[2].Value<float>(), c[3].Value<float>());
                    break;
                case SerializedPropertyType.Rect:
                    if (token is JObject ro)
                        prop.rectValue = new Rect(ro["x"].Value<float>(), ro["y"].Value<float>(), ro["width"].Value<float>(), ro["height"].Value<float>());
                    break;
                case SerializedPropertyType.Bounds:
                    if (token is JObject bo)
                    {
                        var center = bo["center"] as JArray;
                        var size   = bo["size"]   as JArray;
                        if (center != null && size != null)
                            prop.boundsValue = new Bounds(
                                new Vector3(center[0].Value<float>(), center[1].Value<float>(), center[2].Value<float>()),
                                new Vector3(size[0].Value<float>(),   size[1].Value<float>(),   size[2].Value<float>()));
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                {
                    string assetPath = token.Value<string>();
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (asset != null)
                            prop.objectReferenceValue = asset;
                        // Silently skip missing assets — built-in defaults come from the prefab
                    }
                    break;
                }
                case SerializedPropertyType.Generic:
                    ApplyGeneric(prop, token);
                    break;
            }
        }

        private static void ApplyGeneric(SerializedProperty prop, JToken token)
        {
            if (prop.isArray && token is JArray arr)
            {
                prop.arraySize = arr.Count;
                for (int i = 0; i < arr.Count; i++)
                    ApplyProp(prop.GetArrayElementAtIndex(i), arr[i]);
            }
            else if (token is JObject obj)
            {
                foreach (var kvp in obj)
                {
                    var child = prop.FindPropertyRelative(kvp.Key);
                    if (child != null)
                        ApplyProp(child, kvp.Value);
                }
            }
        }

        public static void ReconcileAll(GameObject go, JArray builtInComponents)
        {
            // Build expected type counts from JSON
            var jsonTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (builtInComponents != null)
            {
                foreach (JObject entry in builtInComponents)
                {
                    string typeName = entry.Value<string>("type");
                    if (string.IsNullOrEmpty(typeName)) continue;
                    jsonTypeCounts.TryGetValue(typeName, out int count);
                    jsonTypeCounts[typeName] = count + 1;
                }
            }

            // Remove built-in components absent from JSON
            var existing = go.GetComponents<Component>();
            for (int i = existing.Length - 1; i >= 0; i--)
            {
                var comp = existing[i];
                if (!ShouldInclude(comp)) continue;

                string typeName = comp.GetType().FullName;
                jsonTypeCounts.TryGetValue(typeName, out int allowed);
                if (allowed <= 0)
                    UnityEngine.Object.DestroyImmediate(comp);
                else
                    jsonTypeCounts[typeName] = allowed - 1;
            }

            if (builtInComponents == null || builtInComponents.Count == 0) return;

            // Apply field values (add missing components, update fields)
            var typeCounters = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JObject entry in builtInComponents)
            {
                string typeName = entry.Value<string>("type");
                if (string.IsNullOrEmpty(typeName)) continue;

                typeCounters.TryGetValue(typeName, out int idx);
                typeCounters[typeName] = idx + 1;

                Type componentType = ResolveType(typeName);
                if (componentType == null)
                {
                    Debug.LogWarning($"[UnityAIBridge] Built-in component type not found: {typeName}");
                    continue;
                }

                Component[] components = go.GetComponents(componentType);
                Component component = idx < components.Length ? components[idx] : go.AddComponent(componentType);
                Deserialize(component, entry);
            }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = assembly.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
