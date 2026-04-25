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
using UnityEngine.SceneManagement;
using UnityAIBridge;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Serialization engine. Handles all JSON read/write for entity files and the manifest.
    /// Uses Newtonsoft.Json. Never referenced by Runtime assembly code.
    /// </summary>
    public static class SceneIO
    {
        private const int ExpectedSchemaVersion = 1;

        private static readonly JsonSerializer FieldSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new UnityMathConverter() },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 8,
        });

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

        private static bool _isBootstrapping = false;
        private static SceneDataManager _bootstrappingManager = null;

        /// <summary>
        /// Cancels any in-flight reconcile. Called by LiveSyncController when the active
        /// scene changes so the stale coroutine stops and the new scene can bootstrap.
        /// </summary>
        public static void CancelBootstrap()
        {
            _isBootstrapping = false;
            _bootstrappingManager = null;
        }

        /// <summary>
        /// Non-destructive sync: reads all JSON files and reconciles the scene.
        /// Existing entities are updated in place; missing entities are spawned;
        /// scene objects with no backing JSON file are pruned.
        /// Used on every bootstrap so JSON always wins on load, without destroying
        /// persistent scene objects unnecessarily.
        /// </summary>
        public static IEnumerator ReconcileScene(SceneDataManager manager)
        {
            if (_isBootstrapping) yield break;
            _isBootstrapping = true;
            _bootstrappingManager = manager;

            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath))
            {
                _isBootstrapping = false;
                _bootstrappingManager = null;
                yield break;
            }

            try
            {
                // Capture the target scene before any yields — manager.gameObject may be
                // destroyed mid-coroutine if the scene is switched or unloaded.
                Scene targetScene = manager.gameObject.scene;

                string manifestPath = Path.Combine(manager.sceneDataPath, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    Debug.LogError($"[UnityAIBridge] manifest.json not found at {manifestPath}");
                    yield break;
                }

                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                int schemaVersion = manifest.Value<int>("schemaVersion");
                if (schemaVersion != ExpectedSchemaVersion)
                {
                    Debug.LogError($"[UnityAIBridge] Schema mismatch. Expected {ExpectedSchemaVersion}, got {schemaVersion}.");
                    yield break;
                }

                string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
                if (!Directory.Exists(entitiesDir)) yield break;

                string[] entityFiles = Directory.GetFiles(entitiesDir, "*.json");
                var entityData = new List<(string uuid, JObject data, GameObject go)>();

                // PASS 1 – Update existing or spawn missing
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Reconciling entities…", 0f);
                for (int i = 0; i < entityFiles.Length; i++)
                {
                    string path = entityFiles[i];
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", $"Reconciling {Path.GetFileName(path)}", (float)i / entityFiles.Length * 0.33f);

                    JObject data = null;
                    try { data = JObject.Parse(File.ReadAllText(path)); }
                    catch { Debug.LogWarning($"[UnityAIBridge] Failed to parse {path}. Skipping."); }

                    if (data == null) { yield return null; continue; }

                    string uuid = data.Value<string>("uuid");
                    if (string.IsNullOrEmpty(uuid)) { yield return null; continue; }

                    // Reuse existing GO if registered; scan scene as fallback; spawn if truly absent
                    GameObject go = manager.GetByUUID(uuid);
                    if (go == null)
                    {
                        var sceneEntities = UnityEngine.Object.FindObjectsByType<EntitySync>(
                            FindObjectsInactive.Include, FindObjectsSortMode.None);
                        foreach (var s in sceneEntities)
                            if (s.uuid == uuid) { go = s.gameObject; break; }
                    }

                    if (go != null)
                    {
                        manager.Register(uuid, go);
                        go.name = data.Value<string>("name") ?? go.name;
                    }
                    else
                    {
                        go = InstantiateEntity(uuid, data.Value<string>("prefabPath"), data.Value<string>("name"), targetScene);
                        if (go == null) { yield return null; continue; }
                        manager.Register(uuid, go);
                    }

                    entityData.Add((uuid, data, go));
                    yield return null;
                    if (_bootstrappingManager != manager || manager == null) yield break;
                }

                // PASS 2 – Wire hierarchy
                if (manager == null) yield break;
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Wiring hierarchy…", 0.33f);
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
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
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", "Wiring...", 0.33f + (float)i / entityData.Count * 0.33f);
                }

                // PASS 2b – Apply sibling indices
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    int siblingIndex = data.Value<int?>("siblingIndex") ?? -1;
                    if (siblingIndex >= 0)
                        go.transform.SetSiblingIndex(siblingIndex);
                }

                // PASS 3 – Apply transforms & customData
                if (manager == null) yield break;
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Applying data…", 0.66f);
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    try
                    {
                        ApplyGoProperties(go, data);
                        ApplyTransform(go, data["transform"] as JObject);
                        ReconcileComponents(go, data["customData"] as JArray, manager);
                        BuiltInComponentSerializer.ReconcileAll(go, data["builtInComponents"] as JArray);
                    }
                    catch (Exception e)
                    {
                        string entityName = data.Value<string>("name") ?? uuid;
                        Debug.LogError($"[UnityAIBridge] Failed to apply entity '{entityName}' (uuid={uuid}): {e.Message}\n{e.StackTrace}");
                    }
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", "Finishing...", 0.66f + (float)i / entityData.Count * 0.34f);
                }

                // Prune scene objects that have no backing JSON file
                PruneOrphanEntities(manager);

                if (manager != null && manager.gameObject != null)
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

                if (manager != null)
                    IndexWriter.RegenerateIndex(manager.sceneDataPath);

                Debug.Log($"[UnityAIBridge] Reconcile complete — {entityData.Count} entities.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isBootstrapping = false;
                if (_bootstrappingManager == manager) _bootstrappingManager = null;
            }
        }

        private static void ClearExistingEntities(SceneDataManager manager)
        {
            // SuppressWriteEvents prevents HandleDestroyEvent from deleting the JSON files
            // while we tear down the existing scene objects.
            LiveSyncController.SuppressWriteEvents = true;
            var existingEntities = UnityEngine.Object.FindObjectsByType<EntitySync>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in existingEntities)
                UnityEngine.Object.DestroyImmediate(s.gameObject);
            LiveSyncController.SuppressWriteEvents = false;

            manager.ClearRegistry();
        }

        // ─── Instantiation ────────────────────────────────────────────────────────

        private static GameObject InstantiateEntity(string uuid, string prefabPath, string entityName, Scene targetScene = default)
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
                    Debug.LogWarning($"[UnityAIBridge] Unknown primitive type: {prefabPath}");
                    go = new GameObject();
                }
            }
            else if (!string.IsNullOrEmpty(prefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UnityAIBridge] Prefab not found at {prefabPath}. Creating empty object.");
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

            // Ensure the object lands in the correct scene regardless of which scene
            // is currently active. Without this, a mid-bootstrap scene switch causes
            // objects to be created in the wrong scene.
            if (targetScene.IsValid() && go.scene != targetScene)
                SceneManager.MoveGameObjectToScene(go, targetScene);

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
            {
                var s = new Vector3(scl[0].Value<float>(), scl[1].Value<float>(), scl[2].Value<float>());
                if (s.x == 0f || s.y == 0f || s.z == 0f)
                    Debug.LogWarning($"[UnityAIBridge] Entity '{go.name}' has a zero scale component {s} — this causes 'Matrix get_rotation() failed'. Fix the 'scl' field in its JSON.");
                go.transform.localScale = s;
            }
        }

        private static void ApplyGoProperties(GameObject go, JObject data)
        {
            var so = new SerializedObject(go);
            bool changed = false;

            string tag = data.Value<string>("tag");
            if (!string.IsNullOrEmpty(tag))
            {
                var prop = so.FindProperty("m_TagString");
                if (prop != null && prop.stringValue != tag)
                {
                    if (System.Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, tag) >= 0)
                    {
                        prop.stringValue = tag;
                        changed = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[UnityAIBridge] Tag '{tag}' is not defined. Skipping.");
                    }
                }
            }

            string layerName = data.Value<string>("layer");
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerIndex = LayerMask.NameToLayer(layerName);
                if (layerIndex >= 0)
                {
                    var prop = so.FindProperty("m_Layer");
                    if (prop != null && prop.intValue != layerIndex)
                    {
                        prop.intValue = layerIndex;
                        changed = true;
                    }
                }
                else
                {
                    Debug.LogWarning($"[UnityAIBridge] Layer '{layerName}' is not defined. Skipping.");
                }
            }

            bool? isStatic = data.Value<bool?>("isStatic");
            if (isStatic.HasValue && go.isStatic != isStatic.Value)
            {
                GameObjectUtility.SetStaticEditorFlags(go,
                    isStatic.Value ? (StaticEditorFlags)~0 : 0);
                changed = true;
            }

            if (changed)
                so.ApplyModifiedPropertiesWithoutUndo();

            bool? activeSelf = data.Value<bool?>("activeSelf");
            if (activeSelf.HasValue && go.activeSelf != activeSelf.Value)
                go.SetActive(activeSelf.Value);
        }

        // ─── customData serialization ─────────────────────────────────────────────

        /// <summary>
        /// Reconciles the MonoBehaviour components on a GameObject against the customData array.
        /// Components in the JSON are applied (added if missing, fields updated).
        /// MonoBehaviours on the GO that are absent from the JSON are removed.
        /// </summary>
        private static void ReconcileComponents(GameObject go, JArray customData, SceneDataManager manager = null)
        {
            // Build the set of types declared in JSON (with per-type counts for multi-component)
            var jsonTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (customData != null)
            {
                foreach (JObject entry in customData)
                {
                    string typeName = entry.Value<string>("type");
                    if (string.IsNullOrEmpty(typeName)) continue;
                    jsonTypeCounts.TryGetValue(typeName, out int count);
                    jsonTypeCounts[typeName] = count + 1;
                }
            }

            // Remove MonoBehaviours not present in JSON
            // Iterate in reverse so removal doesn't shift indices
            var existing = go.GetComponents<MonoBehaviour>();
            for (int i = existing.Length - 1; i >= 0; i--)
            {
                var mb = existing[i];
                if (mb == null) continue;
                if (mb is EntitySync) continue; // never remove EntitySync

                string typeName = mb.GetType().FullName;
                jsonTypeCounts.TryGetValue(typeName, out int allowedCount);
                if (allowedCount <= 0)
                {
                    UnityEngine.Object.DestroyImmediate(mb);
                }
                else
                {
                    jsonTypeCounts[typeName] = allowedCount - 1;
                }
            }

            // Reset counters and apply field values (add missing components, update fields)
            if (customData == null || customData.Count == 0) return;

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
                    Debug.LogWarning($"[UnityAIBridge] Component type not found: {typeName}");
                    continue;
                }

                Component[] components = go.GetComponents(componentType);
                MonoBehaviour component;
                if (idx < components.Length)
                {
                    component = components[idx] as MonoBehaviour;
                }
                else
                {
                    component = (MonoBehaviour)go.AddComponent(componentType);
                }

                DeserializeComponentFields(component, entry, manager);
            }
        }

        private static void DeserializeComponentFields(MonoBehaviour component, JObject entry, SceneDataManager manager = null)
        {
            Type type = component.GetType();
            foreach (var prop in entry)
            {
                if (prop.Key == "type") continue;

                FieldInfo field = FindSerializableField(type, prop.Key);
                if (field == null) continue;

                if (typeof(GameObject).IsAssignableFrom(field.FieldType) ||
                    typeof(Component).IsAssignableFrom(field.FieldType))
                {
                    if (prop.Value is JObject refObj && refObj["targetUUID"] != null && manager != null)
                    {
                        string targetUuid = refObj["targetUUID"].Value<string>();
                        GameObject targetGo = manager.GetByUUID(targetUuid);
                        if (targetGo != null)
                        {
                            if (typeof(GameObject).IsAssignableFrom(field.FieldType))
                                field.SetValue(component, targetGo);
                            else
                                field.SetValue(component, targetGo.GetComponent(field.FieldType));
                        }
                        else
                            Debug.LogWarning($"[UnityAIBridge] Could not resolve targetUUID '{targetUuid}' for field {prop.Key} on {type.Name} — entity may not be loaded yet");
                    }
                    else if (prop.Value?.Type == JTokenType.String)
                    {
                        string assetPath = prop.Value.Value<string>();
                        var asset = AssetDatabase.LoadAssetAtPath(assetPath, field.FieldType);
                        if (asset != null)
                            field.SetValue(component, asset);
                        else
                            Debug.LogWarning($"[UnityAIBridge] Asset not found at '{assetPath}' for field {prop.Key} on {type.Name}");
                    }
                    continue;
                }

                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    var asset = DeserializeAssetReference(prop.Value, field.FieldType);
                    if (asset != null)
                        field.SetValue(component, asset);
                    else if (prop.Value?.Type != JTokenType.Null && prop.Value != null)
                        Debug.LogWarning($"[UnityAIBridge] Asset not found for field {prop.Key} on {type.Name}");
                    continue;
                }
                try
                {
                    object value = prop.Value.ToObject(field.FieldType, FieldSerializer);
                    field.SetValue(component, value);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UnityAIBridge] Failed to set field {prop.Key} on {type.Name}: {e.Message}");
                }
            }
        }

        // ─── Entity → JSON (write pipeline) ──────────────────────────────────────

        /// <summary>
        /// Serializes a GameObject to JSON and writes to disk.
        /// Skips the write if the serialized content is identical to what's already on disk (diff-guard).
        /// </summary>
        internal static bool SuppressAssetImport = false;

        public static void WriteEntity(GameObject go, string entitiesDir)
        {
            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            if (!Directory.Exists(entitiesDir))
            {
                Directory.CreateDirectory(entitiesDir);
            }

            string filePath = Path.Combine(entitiesDir, sync.uuid + ".json");
            string json = SerializeEntity(go, sync.uuid);

            if (File.Exists(filePath))
            {
                if (File.ReadAllText(filePath) == json)
                {
                    sync.isDirty = false;
                    return;
                }
            }

            File.WriteAllText(filePath, json);
            sync.isDirty = false;
            IndexWriter.UpsertEntity(go, Path.GetDirectoryName(entitiesDir));

            if (!SuppressAssetImport)
            {
                string relativePath = GetRelativePath(filePath);
                AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
            }
        }

        // Helper to convert an absolute path back to a Unity "Assets/..." path
        private static string GetRelativePath(string absolutePath)
        {
            if (absolutePath.StartsWith(Application.dataPath))
            {
                return "Assets" + absolutePath.Substring(Application.dataPath.Length);
            }
            return absolutePath;
        }

        public static string SerializeEntity(GameObject go, string uuid)
        {
            var sync = go.GetComponent<EntitySync>();
            var parentSync = go.transform.parent != null
                ? go.transform.parent.GetComponent<EntitySync>()
                : null;

            InitLog.Write($"    -> GetPrefabPath");
            var root = new JObject
            {
                ["uuid"] = uuid,
                ["name"] = go.name,
                ["prefabPath"] = GetPrefabPath(go),
                ["tag"] = go.tag,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["isStatic"] = go.isStatic,
                ["activeSelf"] = go.activeSelf,
            };

            if (parentSync != null)
                root["parentUuid"] = parentSync.uuid;

            root["siblingIndex"] = go.transform.GetSiblingIndex();

            var t = go.transform;
            root["transform"] = new JObject
            {
                ["pos"] = new JArray(t.localPosition.x, t.localPosition.y, t.localPosition.z),
                ["rot"] = new JArray(t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z),
                ["scl"] = new JArray(t.localScale.x, t.localScale.y, t.localScale.z),
            };

            InitLog.Write($"    -> SerializeBuiltInComponents");
            var builtInComponents = BuiltInComponentSerializer.SerializeAll(go);
            if (builtInComponents.Count > 0)
                root["builtInComponents"] = builtInComponents;

            InitLog.Write($"    -> SerializeCustomData");
            var customData = SerializeCustomData(go);
            if (customData.Count > 0)
                root["customData"] = customData;
            InitLog.Write($"    -> WriteFile");

            return root.ToString(Formatting.Indented);
        }

        internal static string GetPrefabPath(GameObject go)
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

                InitLog.Write($"      -> MonoBehaviour: {component.GetType().FullName}");
                var entry = new JObject
                {
                    ["type"] = component.GetType().FullName
                };

                SerializeComponentFields(component, entry);
                InitLog.Write($"         done");
                result.Add(entry);
            }

            return result;
        }

        // Serializes a UnityEngine.Object asset reference.
        // Main assets → plain path string. Sub-assets → { "path": "...", "name": "..." }.
        private static JToken SerializeAssetReference(UnityEngine.Object asset)
        {
            if (asset == null) return JValue.CreateNull();
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return JValue.CreateNull();
            if (AssetDatabase.IsSubAsset(asset))
                return new JObject { ["path"] = path, ["name"] = asset.name };
            return new JValue(path);
        }

        // Deserializes a UnityEngine.Object asset reference from a plain path string
        // or a { "path", "name" } sub-asset object.
        private static UnityEngine.Object DeserializeAssetReference(JToken token, Type fieldType)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String)
            {
                string path = token.Value<string>();
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath(path, fieldType);
            }
            if (token is JObject obj)
            {
                string path = obj["path"]?.Value<string>();
                string name = obj["name"]?.Value<string>();
                if (string.IsNullOrEmpty(path)) return null;
                if (!string.IsNullOrEmpty(name))
                {
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (a != null && a.name == name && fieldType.IsAssignableFrom(a.GetType()))
                            return a;
                }
                return AssetDatabase.LoadAssetAtPath(path, fieldType);
            }
            return null;
        }

        private static void SerializeComponentFields(MonoBehaviour component, JObject entry)
        {
            Type type = component.GetType();
            FieldInfo[] fields = GetSerializableFields(type);

            foreach (var field in fields)
            {
                InitLog.Write($"         field: {field.Name} ({field.FieldType.Name})");
                try
                {
                    // Single GameObject or Component reference → entity UUID or null
                    if (typeof(GameObject).IsAssignableFrom(field.FieldType) ||
                        typeof(Component).IsAssignableFrom(field.FieldType))
                    {
                        entry[field.Name] = SerializeEntityReference(field.GetValue(component) as UnityEngine.Object, type, field.Name);
                        continue;
                    }

                    // Single UnityEngine.Object (asset) reference → asset path
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                    {
                        entry[field.Name] = SerializeAssetReference(field.GetValue(component) as UnityEngine.Object);
                        continue;
                    }

                    // Array or List<T> whose element type is a UnityEngine.Object subclass
                    Type elemType = GetUnityObjectCollectionElementType(field.FieldType);
                    if (elemType != null)
                    {
                        var collection = field.GetValue(component) as System.Collections.IEnumerable;
                        if (collection == null) { entry[field.Name] = JValue.CreateNull(); continue; }
                        bool isEntityType = typeof(GameObject).IsAssignableFrom(elemType) || typeof(Component).IsAssignableFrom(elemType);
                        var jArr = new JArray();
                        foreach (var item in collection)
                        {
                            var refObj = item as UnityEngine.Object;
                            jArr.Add(isEntityType
                                ? SerializeEntityReference(refObj, type, field.Name)
                                : SerializeAssetReference(refObj));
                        }
                        entry[field.Name] = jArr;
                        continue;
                    }

                    object value = field.GetValue(component);
                    entry[field.Name] = value != null ? JToken.FromObject(value, FieldSerializer) : JValue.CreateNull();
                }
                catch (Exception e)
                {
                    InitLog.Write($"           WARN: {e.Message}");
                    Debug.LogWarning($"[UnityAIBridge] Failed to serialize field {field.Name} on {type.Name}: {e.Message}");
                }
            }
        }

        private static JToken SerializeEntityReference(UnityEngine.Object refObj, Type ownerType, string fieldName)
        {
            if (refObj == null) return JValue.CreateNull();
            string assetPath = AssetDatabase.GetAssetPath(refObj);
            if (!string.IsNullOrEmpty(assetPath))
                return new JValue(assetPath);
            var refGo = refObj is GameObject gObj ? gObj : ((Component)refObj).gameObject;
            var refSync = refGo.GetComponent<EntitySync>();
            if (refSync != null && !string.IsNullOrEmpty(refSync.uuid))
                return new JObject { ["targetUUID"] = refSync.uuid };
            Debug.LogWarning($"[UnityAIBridge] Field {fieldName} on {ownerType.Name} references a GameObject with no EntitySync — serialized as null");
            return JValue.CreateNull();
        }

        private static Type GetUnityObjectCollectionElementType(Type fieldType)
        {
            if (fieldType.IsArray)
            {
                var elem = fieldType.GetElementType();
                return typeof(UnityEngine.Object).IsAssignableFrom(elem) ? elem : null;
            }
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = fieldType.GetGenericArguments()[0];
                return typeof(UnityEngine.Object).IsAssignableFrom(elem) ? elem : null;
            }
            return null;
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
                Debug.LogWarning($"[UnityAIBridge] Hot-reload parse error for {filePath}: {e.Message}");
                return;
            }

            string uuid = data.Value<string>("uuid");
            if (string.IsNullOrEmpty(uuid)) return;

            string entitiesDir = Path.GetDirectoryName(filePath);

            GameObject go = manager.GetByUUID(uuid);
            if (go == null)
            {
                // Registry miss — scan the scene for an existing EntitySync with this UUID
                // before spawning a new object. Covers domain-reload registry gaps and
                // races where the async bootstrap hasn't finished registering yet.
                var existing = UnityEngine.Object.FindObjectsByType<EntitySync>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var s in existing)
                {
                    if (s.uuid == uuid) { go = s.gameObject; break; }
                }

                if (go != null)
                {
                    Debug.Log($"[UnityAIBridge] HotReload: uuid={uuid} not in registry but found in scene — re-registering '{go.name}'");
                    manager.Register(uuid, go);
                }
                else
                {
                    Debug.Log($"[UnityAIBridge] HotReload: uuid={uuid} not in registry or scene — SPAWNING new object '{data.Value<string>("name")}'");
                    go = InstantiateEntity(uuid, data.Value<string>("prefabPath"), data.Value<string>("name"), manager.gameObject.scene);
                    if (go == null) return;
                    manager.Register(uuid, go);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }
            }
            else
            {
                Debug.Log($"[UnityAIBridge] HotReload: uuid={uuid} found in registry — UPDATING '{go.name}'");
            }

            string newName = data.Value<string>("name");
            if (!string.IsNullOrEmpty(newName))
                go.name = newName;

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

            int siblingIndex = data.Value<int?>("siblingIndex") ?? -1;
            if (siblingIndex >= 0)
                go.transform.SetSiblingIndex(siblingIndex);

            bool applySucceeded = false;
            try
            {
                ApplyGoProperties(go, data);
                ApplyTransform(go, data["transform"] as JObject);
                ReconcileComponents(go, data["customData"] as JArray, manager);
                BuiltInComponentSerializer.ReconcileAll(go, data["builtInComponents"] as JArray);
                applySucceeded = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityAIBridge] HotReload: failed to apply entity '{go.name}' (uuid={uuid}) — scene object NOT marked dirty to prevent JSON corruption: {e.Message}\n{e.StackTrace}");
            }

            if (!applySucceeded) return;

            EditorUtility.SetDirty(go);
            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
            EditorApplication.RepaintHierarchyWindow();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// Destroys the entity with the given UUID and removes it from the registry.
        /// </summary>
        public static void DestroyEntity(string uuid, SceneDataManager manager)
        {
            GameObject go = manager.GetByUUID(uuid);
            if (go != null)
            {
                Debug.Log($"[UnityAIBridge] DestroyEntity: uuid={uuid} — destroying '{go.name}'");
                manager.Unregister(uuid);
                UnityEngine.Object.DestroyImmediate(go);
            }
            else
            {
                Debug.Log($"[UnityAIBridge] DestroyEntity: uuid={uuid} — not in registry, nothing to destroy");
            }
        }

        // ─── Registry rebuild (domain reload / play mode exit) ───────────────────

        /// <summary>
        /// Scans the active scene for existing EntitySync components and re-registers them
        /// in the manager. Used after domain reload or Play Mode exit to restore the
        /// UUID→GameObject lookup without re-instantiating objects.
        /// Returns the number of entities registered.
        /// </summary>
        public static int RebuildRegistry(SceneDataManager manager)
        {
            if (manager == null) return 0;
            manager.ClearRegistry();
            int count = 0;
            var syncs = UnityEngine.Object.FindObjectsByType<EntitySync>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var sync in syncs)
            {
                if (!string.IsNullOrEmpty(sync.uuid))
                {
                    manager.Register(sync.uuid, sync.gameObject);
                    count++;
                }
            }
            return count;
        }

        // ─── Scene initialization ─────────────────────────────────────────────────

        /// <summary>
        /// Idempotent full initialization: ensures the directory structure and manifest
        /// exist, then migrates all unmanaged scene objects into JSON sync.
        /// Safe to run on an already-initialized scene — nothing is overwritten or duplicated.
        /// </summary>
        public static void InitializeScene(SceneDataManager manager)
        {
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            // Create directories if missing — Directory.CreateDirectory is a no-op if they exist.
            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            Directory.CreateDirectory(entitiesDir);

            // Create manifest only if missing — never overwrite (schemaVersion must not change).
            string manifestPath = Path.Combine(manager.sceneDataPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                string sceneName = Path.GetFileName(manager.sceneDataPath.TrimEnd('/', '\\'));
                File.WriteAllText(manifestPath,
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    $"  \"sceneName\": \"{sceneName}\"\n" +
                    "}\n");
            }

            string manifestRelPath = GetRelativePath(manifestPath);
            AssetDatabase.ImportAsset(manifestRelPath, ImportAssetOptions.ForceUpdate);

            // Migrate all unmanaged scene objects into JSON sync.
            EditorCoroutineRunner.StartEditorCoroutine(MigrateScene(manager));
        }

        // ─── Scene migration ──────────────────────────────────────────────────────

        /// <summary>
        /// Migrates all GameObjects in the active scene into JSON sync management.
        ///
        /// Pass 1 — assigns EntitySync + UUID to every unmanaged object (skips the
        ///           SceneDataManager itself and objects that already have EntitySync).
        /// Pass 2 — writes a JSON file for every managed object. WriteEntity's diff-guard
        ///           makes this a no-op for files whose content hasn't changed.
        ///
        /// Idempotent: running twice leaves the scene and JSON files unchanged.
        /// </summary>
        public static IEnumerator MigrateScene(SceneDataManager manager)
        {
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) yield break;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            if (!Directory.Exists(entitiesDir))
            {
                Debug.LogError($"[UnityAIBridge] Entities directory not found: {entitiesDir}. Run Setup first.");
                yield break;
            }

            // Collect every GameObject in this scene (including inactive).
            // Traverse from roots so parents always precede children in the list.
            var allObjects = new List<GameObject>();
            foreach (var root in manager.gameObject.scene.GetRootGameObjects())
                CollectHierarchy(root, allObjects);

            int total = allObjects.Count;

            try
            {
                // Pass 1 — assign EntitySync + UUID to unmanaged objects.
                // SuppressWriteEvents prevents ObjectChangeEvents from writing
                // individual JSON files while we're adding components in bulk.
                LiveSyncController.SuppressWriteEvents = true;
                SuppressAssetImport = true;
                int newCount = 0;
                for (int i = 0; i < allObjects.Count; i++)
                {
                    var go = allObjects[i];
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", $"Registering {go.name}…", (float)i / total * 0.5f);

                    if (go.GetComponent<SceneDataManager>() != null) continue;
                    if (go.GetComponent<EntitySync>() != null) continue;

                    var sync = go.AddComponent<EntitySync>();
                    sync.uuid = Guid.NewGuid().ToString();
                    manager.Register(sync.uuid, go);
                    newCount++;
                    yield return null;
                }
                LiveSyncController.SuppressWriteEvents = false;

                // Pass 2 — write JSON for all managed objects (new and pre-existing).
                // Pre-existing EntitySync objects may not be in the registry yet — register them.
                // WriteEntity's diff-guard skips unchanged files on repeat runs.
                // AssetDatabase.ImportAsset is suppressed for the whole pass — one Refresh at the end.
                int writtenCount = 0;
                int errorCount = 0;
                InitLog.Begin(manager.sceneDataPath);
                for (int i = 0; i < allObjects.Count; i++)
                {
                    var go = allObjects[i];
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", $"Writing {go.name}…", 0.5f + (float)i / total * 0.5f);

                    var sync = go.GetComponent<EntitySync>();
                    if (sync == null || string.IsNullOrEmpty(sync.uuid)) continue;
                    if (go.GetComponent<SceneDataManager>() != null) continue;

                    if (manager.GetByUUID(sync.uuid) == null)
                        manager.Register(sync.uuid, go);

                    InitLog.Write($"  [{i + 1}/{total}] Writing: {go.name} ({sync.uuid})");

                    try
                    {
                        WriteEntity(go, entitiesDir);
                        writtenCount++;
                        InitLog.Write($"    OK");
                    }
                    catch (Exception e)
                    {
                        errorCount++;
                        InitLog.Write($"    ERROR: {e.Message}");
                        Debug.LogError($"[UnityAIBridge] Migration: failed to write '{go.name}' (uuid={sync.uuid}): {e.Message}\n{e.StackTrace}");
                    }
                    yield return null;
                }

                SuppressAssetImport = false;
                AssetDatabase.Refresh();

                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                string errorSuffix = errorCount > 0 ? $", {errorCount} error{(errorCount == 1 ? "" : "s")} (see Console)" : "";
                Debug.Log($"[UnityAIBridge] Migration complete — {newCount} new entit{(newCount == 1 ? "y" : "ies")} registered, {writtenCount} JSON file{(writtenCount == 1 ? "" : "s")} written{errorSuffix}.");
            }
            finally
            {
                InitLog.End();
                LiveSyncController.SuppressWriteEvents = false;
                SuppressAssetImport = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static void CollectHierarchy(GameObject go, List<GameObject> result)
        {
            result.Add(go);
            foreach (Transform child in go.transform)
                CollectHierarchy(child.gameObject, result);
        }

        // ─── Orphan pruning ───────────────────────────────────────────────────────

        /// <summary>
        /// Destroys any scene entities that have an EntitySync component but no corresponding
        /// JSON file on disk. Enforces the invariant that every managed entity has a backing file.
        /// Called on startup after RebuildRegistry when the scene already has persistent entities.
        /// </summary>
        public static void PruneOrphanEntities(SceneDataManager manager)
        {
            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");

            // Snapshot UUIDs before modifying the registry
            var orphans = new List<string>();
            foreach (string uuid in manager.GetAllUUIDs())
            {
                string filePath = Path.Combine(entitiesDir, uuid + ".json");
                if (!File.Exists(filePath))
                    orphans.Add(uuid);
            }

            if (orphans.Count == 0) return;

            // SuppressWriteEvents prevents HandleDestroyEvent from also deleting the
            // JSON files of any children destroyed as a side-effect of parent destruction.
            LiveSyncController.SuppressWriteEvents = true;
            foreach (string uuid in orphans)
                DestroyEntity(uuid, manager);
            LiveSyncController.SuppressWriteEvents = false;

            // Rebuild registry to clear stale entries for children destroyed above.
            RebuildRegistry(manager);

            Debug.Log($"[UnityAIBridge] Pruned {orphans.Count} orphan entit{(orphans.Count == 1 ? "y" : "ies")} with no backing JSON file.");
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

    // ─── Unity math type converter ────────────────────────────────────────────────

    internal class UnityMathConverter : JsonConverter
    {
        public override bool CanConvert(Type t) =>
            t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
            t == typeof(Quaternion) || t == typeof(Color) || t == typeof(Color32);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            switch (value)
            {
                case Vector2 v:  writer.WriteValue(v.x); writer.WriteValue(v.y); break;
                case Vector3 v:  writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); break;
                case Vector4 v:  writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); writer.WriteValue(v.w); break;
                case Quaternion q: writer.WriteValue(q.x); writer.WriteValue(q.y); writer.WriteValue(q.z); writer.WriteValue(q.w); break;
                case Color c:    writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); break;
                case Color32 c:  writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); break;
            }
            writer.WriteEndArray();
        }

        public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer)
        {
            var arr = JArray.Load(reader);
            float F(int i) => arr.Count > i ? arr[i].Value<float>() : 0f;
            byte B(int i) => arr.Count > i ? arr[i].Value<byte>() : (byte)0;
            if (t == typeof(Vector2))   return new Vector2(F(0), F(1));
            if (t == typeof(Vector3))   return new Vector3(F(0), F(1), F(2));
            if (t == typeof(Vector4))   return new Vector4(F(0), F(1), F(2), F(3));
            if (t == typeof(Quaternion)) return new Quaternion(F(0), F(1), F(2), F(3));
            if (t == typeof(Color))     return new Color(F(0), F(1), F(2), F(3));
            if (t == typeof(Color32))   return new Color32(B(0), B(1), B(2), B(3));
            return null;
        }
    }

    // ─── Validation result ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a crash-safe init log to disk. Each Write() call flushes immediately so
    /// the file is complete up to the last line even if Unity crashes mid-operation.
    /// Also forwards Unity console errors/exceptions into the file automatically.
    /// </summary>
    public static class InitLog
    {
        private static string _path;
        public static bool IsActive => _path != null;

        public static void Begin(string sceneDataPath)
        {
            _path = Path.Combine(sceneDataPath, "init-log.txt");
            File.WriteAllText(_path, $"[InitLog] Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            Application.logMessageReceived += OnLogMessage;
        }

        public static void Write(string message)
        {
            if (_path == null) return;
            File.AppendAllText(_path, message + "\n");
        }

        public static void End()
        {
            Application.logMessageReceived -= OnLogMessage;
            Write($"[InitLog] Finished {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _path = null;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Write($"  [UNITY {type.ToString().ToUpper()}] {message}\n{stackTrace}");
        }
    }

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
