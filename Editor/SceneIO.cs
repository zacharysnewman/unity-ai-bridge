using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using JsonScenesForUnity;

namespace JsonScenesForUnity.Editor
{
    /// <summary>
    /// Serialization engine. Handles all JSON read/write for entity files and the manifest.
    /// Uses Newtonsoft.Json. Never referenced by Runtime assembly code.
    /// </summary>
    public static class SceneIO
    {
        private const int ExpectedSchemaVersion = 1;

        // ─── Primitive path prefix ────────────────────────────────────────────────

        private const string PrimitivePrefix = "primitive/";

        private static readonly Dictionary<string, PrimitiveType> PrimitiveLookup =
            new Dictionary<string, PrimitiveType>(StringComparer.Ordinal)
            {
                { "primitive/Sphere",   PrimitiveType.Sphere   },
                { "primitive/Cube",     PrimitiveType.Cube     },
                { "primitive/Cylinder", PrimitiveType.Cylinder },
                { "primitive/Capsule",  PrimitiveType.Capsule  },
                { "primitive/Plane",    PrimitiveType.Plane    },
                { "primitive/Quad",     PrimitiveType.Quad     },
            };

        // ─── Bootstrap ────────────────────────────────────────────────────────────

        /// <summary>
        /// Full async bootstrap: reads manifest, instantiates all entities, wires hierarchy,
        /// applies transforms and customData. Displays a Unity progress bar.
        /// </summary>
        public static IEnumerator BootstrapScene(SceneDataManager manager)
        {
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath))
                yield break;

            string manifestPath = Path.Combine(manager.sceneDataPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[JsonScenes] manifest.json not found at {manifestPath}");
                yield break;
            }

            // 1. Validate manifest schema version
            JObject manifest;
            try
            {
                manifest = JObject.Parse(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonScenes] Failed to parse manifest.json: {e.Message}");
                yield break;
            }

            int schemaVersion = manifest.Value<int>("schemaVersion");
            if (schemaVersion != ExpectedSchemaVersion)
            {
                Debug.LogError(
                    $"[JsonScenes] Schema version mismatch. Expected {ExpectedSchemaVersion}, got {schemaVersion}. " +
                    "Loading aborted. Update the package or migrate your scene data.");
                yield break;
            }

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            if (!Directory.Exists(entitiesDir))
            {
                Debug.LogWarning($"[JsonScenes] Entities directory not found: {entitiesDir}");
                yield break;
            }

            string[] entityFiles = Directory.GetFiles(entitiesDir, "*.json");

            manager.ClearRegistry();

            // Pass 1 – instantiate
            EditorUtility.DisplayProgressBar("JSON Scenes", "Instantiating entities…", 0f);
            var entityData = new List<(string uuid, JObject data, GameObject go)>();

            for (int i = 0; i < entityFiles.Length; i++)
            {
                string path = entityFiles[i];
                EditorUtility.DisplayProgressBar("JSON Scenes", $"Instantiating {Path.GetFileName(path)}",
                    (float)i / entityFiles.Length * 0.33f);

                var parseSuccess = false;
                JObject data = null;
                try
                {
                    data = JObject.Parse(File.ReadAllText(path));
                    parseSuccess = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[JsonScenes] Skipping {path} — parse error: {e.Message}");
                }

                if (!parseSuccess)
                {
                    yield return null;
                    continue;
                }

                string uuid = data.Value<string>("uuid");
                if (string.IsNullOrEmpty(uuid))
                {
                    Debug.LogWarning($"[JsonScenes] Skipping {path} — missing uuid field.");
                    yield return null;
                    continue;
                }

                string prefabPath = data.Value<string>("prefabPath");
                GameObject go = InstantiateEntity(uuid, prefabPath, data.Value<string>("name"));
                if (go == null)
                {
                    yield return null;
                    continue;
                }

                manager.Register(uuid, go);
                entityData.Add((uuid, data, go));
                yield return null;
            }

            // Pass 2 – wire hierarchy
            EditorUtility.DisplayProgressBar("JSON Scenes", "Wiring hierarchy…", 0.33f);
            for (int i = 0; i < entityData.Count; i++)
            {
                var (uuid, data, go) = entityData[i];
                string parentUuid = data.Value<string>("parentUuid");
                if (!string.IsNullOrEmpty(parentUuid))
                {
                    GameObject parentGo = manager.GetByUUID(parentUuid);
                    if (parentGo != null)
                        go.transform.SetParent(parentGo.transform, false);
                    else
                        Debug.LogWarning($"[JsonScenes] Entity {uuid}: parentUuid {parentUuid} not found.");
                }
                EditorUtility.DisplayProgressBar("JSON Scenes", "Wiring hierarchy…",
                    0.33f + (float)i / entityData.Count * 0.33f);
            }

            // Pass 3 – apply transforms and customData
            EditorUtility.DisplayProgressBar("JSON Scenes", "Applying data…", 0.66f);
            for (int i = 0; i < entityData.Count; i++)
            {
                var (uuid, data, go) = entityData[i];
                ApplyTransform(go, data["transform"] as JObject);
                ApplyCustomData(go, data["customData"] as JArray);
                EditorUtility.DisplayProgressBar("JSON Scenes", "Applying data…",
                    0.66f + (float)i / entityData.Count * 0.34f);
            }

            EditorUtility.ClearProgressBar();
            Debug.Log($"[JsonScenes] Bootstrap complete — {entityData.Count} entities loaded.");
        }

        // ─── Instantiation ────────────────────────────────────────────────────────

        private static GameObject InstantiateEntity(string uuid, string prefabPath, string entityName)
        {
            GameObject go;

            if (!string.IsNullOrEmpty(prefabPath) && prefabPath.StartsWith(PrimitivePrefix, StringComparison.Ordinal))
            {
                if (PrimitiveLookup.TryGetValue(prefabPath, out PrimitiveType primitiveType))
                {
                    go = GameObject.CreatePrimitive(primitiveType);
                }
                else
                {
                    Debug.LogWarning($"[JsonScenes] Unknown primitive type: {prefabPath}");
                    go = new GameObject();
                }
            }
            else if (!string.IsNullOrEmpty(prefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[JsonScenes] Prefab not found at {prefabPath}. Creating empty object.");
                    go = new GameObject();
                }
                else
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                }
            }
            else
            {
                go = new GameObject();
            }

            if (!string.IsNullOrEmpty(entityName))
                go.name = entityName;

            go.hideFlags = HideFlags.DontSave;

            var sync = go.AddComponent<EntitySync>();
            sync.uuid = uuid;

            return go;
        }

        // ─── Transform helpers ────────────────────────────────────────────────────

        private static void ApplyTransform(GameObject go, JObject t)
        {
            if (t == null) return;

            var pos = t["pos"] as JArray;
            if (pos != null && pos.Count == 3)
                go.transform.localPosition = new Vector3(
                    pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

            var rot = t["rot"] as JArray;
            if (rot != null && rot.Count == 3)
                go.transform.localRotation = Quaternion.Euler(
                    rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());

            var scl = t["scl"] as JArray;
            if (scl != null && scl.Count == 3)
                go.transform.localScale = new Vector3(
                    scl[0].Value<float>(), scl[1].Value<float>(), scl[2].Value<float>());
        }

        // ─── customData serialization ─────────────────────────────────────────────

        private static void ApplyCustomData(GameObject go, JArray customData)
        {
            if (customData == null || customData.Count == 0) return;

            // Build a per-type index for multi-component support
            var typeCounters = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (JObject entry in customData)
            {
                string typeName = entry.Value<string>("type");
                if (string.IsNullOrEmpty(typeName)) continue;

                typeCounters.TryGetValue(typeName, out int idx);
                typeCounters[typeName] = idx + 1;

                Type componentType = ResolveType(typeName);
                if (componentType == null)
                {
                    Debug.LogWarning($"[JsonScenes] Component type not found: {typeName}");
                    continue;
                }

                MonoBehaviour[] components = (MonoBehaviour[])go.GetComponents(componentType);
                if (idx >= components.Length)
                {
                    Debug.LogWarning($"[JsonScenes] Component index {idx} out of range for type {typeName} on {go.name}");
                    continue;
                }

                MonoBehaviour component = components[idx];
                DeserializeComponentFields(component, entry);
            }
        }

        private static void DeserializeComponentFields(MonoBehaviour component, JObject entry)
        {
            Type type = component.GetType();
            foreach (var prop in entry)
            {
                if (prop.Key == "type") continue;

                FieldInfo field = FindSerializableField(type, prop.Key);
                if (field == null) continue;

                try
                {
                    object value = prop.Value.ToObject(field.FieldType);
                    field.SetValue(component, value);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[JsonScenes] Failed to set field {prop.Key} on {type.Name}: {e.Message}");
                }
            }
        }

        // ─── Entity → JSON (write pipeline) ──────────────────────────────────────

        /// <summary>
        /// Serializes a GameObject to JSON and writes to disk.
        /// Skips the write if the serialized content is identical to what's already on disk (diff-guard).
        /// </summary>
        public static void WriteEntity(GameObject go, string entitiesDir)
        {
            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            string filePath = Path.Combine(entitiesDir, sync.uuid + ".json");
            string json = SerializeEntity(go, sync.uuid);

            // Diff-guard: skip write if content unchanged
            if (File.Exists(filePath))
            {
                string existing = File.ReadAllText(filePath);
                if (existing == json)
                {
                    sync.isDirty = false;
                    return;
                }
            }

            File.WriteAllText(filePath, json);
            sync.isDirty = false;
        }

        public static string SerializeEntity(GameObject go, string uuid)
        {
            var sync = go.GetComponent<EntitySync>();
            var parentSync = go.transform.parent != null
                ? go.transform.parent.GetComponent<EntitySync>()
                : null;

            var root = new JObject
            {
                ["uuid"] = uuid,
                ["name"] = go.name,
                ["prefabPath"] = GetPrefabPath(go),
            };

            if (parentSync != null)
                root["parentUuid"] = parentSync.uuid;

            var t = go.transform;
            root["transform"] = new JObject
            {
                ["pos"] = new JArray(t.localPosition.x, t.localPosition.y, t.localPosition.z),
                ["rot"] = new JArray(t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z),
                ["scl"] = new JArray(t.localScale.x, t.localScale.y, t.localScale.z),
            };

            var customData = SerializeCustomData(go);
            if (customData.Count > 0)
                root["customData"] = customData;

            return root.ToString(Formatting.Indented);
        }

        private static string GetPrefabPath(GameObject go)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (prefab != null)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            // Check if it's a primitive
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                switch (meshName)
                {
                    case "Sphere": return "primitive/Sphere";
                    case "Cube": return "primitive/Cube";
                    case "Cylinder": return "primitive/Cylinder";
                    case "Capsule": return "primitive/Capsule";
                    case "Plane": return "primitive/Plane";
                    case "Quad": return "primitive/Quad";
                }
            }

            return string.Empty;
        }

        private static JArray SerializeCustomData(GameObject go)
        {
            var result = new JArray();
            var components = go.GetComponents<MonoBehaviour>();

            foreach (var component in components)
            {
                if (component == null) continue;
                // Skip internal package components
                if (component is EntitySync) continue;

                var entry = new JObject
                {
                    ["type"] = component.GetType().FullName
                };

                SerializeComponentFields(component, entry);
                result.Add(entry);
            }

            return result;
        }

        private static void SerializeComponentFields(MonoBehaviour component, JObject entry)
        {
            Type type = component.GetType();
            FieldInfo[] fields = GetSerializableFields(type);

            foreach (var field in fields)
            {
                try
                {
                    object value = field.GetValue(component);
                    entry[field.Name] = value != null ? JToken.FromObject(value) : JValue.CreateNull();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[JsonScenes] Failed to serialize field {field.Name} on {type.Name}: {e.Message}");
                }
            }
        }

        // ─── Hot-reload (external file changes) ──────────────────────────────────

        /// <summary>
        /// Applies an updated entity JSON file to the live scene.
        /// If no object with the UUID exists, a new one is instantiated.
        /// </summary>
        public static void HotReloadEntity(string filePath, SceneDataManager manager)
        {
            if (!File.Exists(filePath)) return;

            JObject data;
            try
            {
                data = JObject.Parse(File.ReadAllText(filePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[JsonScenes] Hot-reload parse error for {filePath}: {e.Message}");
                return;
            }

            string uuid = data.Value<string>("uuid");
            if (string.IsNullOrEmpty(uuid)) return;

            string entitiesDir = Path.GetDirectoryName(filePath);

            GameObject go = manager.GetByUUID(uuid);
            if (go == null)
            {
                // New entity
                go = InstantiateEntity(uuid, data.Value<string>("prefabPath"), data.Value<string>("name"));
                if (go == null) return;
                manager.Register(uuid, go);
            }
            else
            {
                // Update name
                string newName = data.Value<string>("name");
                if (!string.IsNullOrEmpty(newName))
                    go.name = newName;
            }

            // Re-wire parent
            string parentUuid = data.Value<string>("parentUuid");
            if (!string.IsNullOrEmpty(parentUuid))
            {
                GameObject parentGo = manager.GetByUUID(parentUuid);
                if (parentGo != null && go.transform.parent != parentGo.transform)
                    go.transform.SetParent(parentGo.transform, false);
            }
            else if (go.transform.parent != null)
            {
                go.transform.SetParent(null, false);
            }

            ApplyTransform(go, data["transform"] as JObject);
            ApplyCustomData(go, data["customData"] as JArray);
        }

        /// <summary>
        /// Destroys the entity with the given UUID and removes it from the registry.
        /// </summary>
        public static void DestroyEntity(string uuid, SceneDataManager manager)
        {
            GameObject go = manager.GetByUUID(uuid);
            if (go != null)
            {
                manager.Unregister(uuid);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─── Validation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Validates all entity files in the scene: prefab paths, parent UUIDs, component types.
        /// Returns a validation result object.
        /// </summary>
        public static ValidationResult ValidateScene(string sceneDataPath)
        {
            var result = new ValidationResult();
            string entitiesDir = Path.Combine(sceneDataPath, "Entities");

            if (!Directory.Exists(entitiesDir))
            {
                result.AddError(null, "entitiesDir", $"Entities directory not found: {entitiesDir}");
                return result;
            }

            var allUuids = new HashSet<string>(StringComparer.Ordinal);
            var entities = new List<(string uuid, JObject data)>();

            // First pass: collect UUIDs
            foreach (string file in Directory.GetFiles(entitiesDir, "*.json"))
            {
                JObject data;
                try { data = JObject.Parse(File.ReadAllText(file)); }
                catch { result.AddError(null, "parse", $"Parse error in {file}"); continue; }

                string uuid = data.Value<string>("uuid");
                if (string.IsNullOrEmpty(uuid))
                { result.AddError(null, "uuid", $"Missing uuid in {file}"); continue; }

                allUuids.Add(uuid);
                entities.Add((uuid, data));
            }

            // Second pass: validate references
            foreach (var (uuid, data) in entities)
            {
                string prefabPath = data.Value<string>("prefabPath");
                if (!string.IsNullOrEmpty(prefabPath) && !prefabPath.StartsWith(PrimitivePrefix, StringComparison.Ordinal))
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                        result.AddError(uuid, "prefabPath", $"Prefab not found at {prefabPath}");
                }

                string parentUuid = data.Value<string>("parentUuid");
                if (!string.IsNullOrEmpty(parentUuid) && !allUuids.Contains(parentUuid))
                    result.AddError(uuid, "parentUuid", $"Parent UUID {parentUuid} not found in scene");

                var customData = data["customData"] as JArray;
                if (customData != null)
                {
                    foreach (JObject entry in customData)
                    {
                        string typeName = entry.Value<string>("type");
                        if (!string.IsNullOrEmpty(typeName) && ResolveType(typeName) == null)
                            result.AddError(uuid, "customData.type", $"Component type not found: {typeName}");
                    }
                }
            }

            return result;
        }

        // ─── Reflection helpers ───────────────────────────────────────────────────

        private static FieldInfo[] GetSerializableFields(Type type)
        {
            var fields = new List<FieldInfo>();
            var t = type;
            while (t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour)
                   && t != typeof(Component) && t != typeof(UnityEngine.Object))
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                    {
                        if (f.GetCustomAttribute<NonSerializedAttribute>() == null)
                            fields.Add(f);
                    }
                }
                t = t.BaseType;
            }
            return fields.ToArray();
        }

        private static FieldInfo FindSerializableField(Type type, string name)
        {
            var t = type;
            while (t != null && t != typeof(MonoBehaviour))
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null && (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                              && f.GetCustomAttribute<NonSerializedAttribute>() == null)
                    return f;
                t = t.BaseType;
            }
            return null;
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

    // ─── Validation result ────────────────────────────────────────────────────────

    public class ValidationResult
    {
        public bool valid = true;
        public List<ValidationError> errors = new List<ValidationError>();

        public void AddError(string uuid, string field, string message)
        {
            valid = false;
            errors.Add(new ValidationError { uuid = uuid, field = field, message = message });
        }
    }

    public class ValidationError
    {
        public string uuid;
        public string field;
        public string message;
    }
}
