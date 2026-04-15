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
        private static bool _isBootstrapping = false;

        /// <summary>
        /// Full async bootstrap: reads manifest, instantiates all entities, wires hierarchy,
        /// applies transforms and customData. Displays a Unity progress bar.
        /// </summary>
        public static IEnumerator BootstrapScene(SceneDataManager manager)
        {
            // 1. THE GUARD: Prevent multiple bootstraps from running at the same time
            if (_isBootstrapping) yield break;
            _isBootstrapping = true;

            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath))
            {
                _isBootstrapping = false;
                yield break;
            }

            // 2. THE TRY-FINALLY: Ensures the progress bar and guard are ALWAYS 
            // cleaned up, even if the code crashes or hits an error.
            try
            {
                // 3. THE CLEANUP: Wipe "DontSave" puppets before spawning
                ClearExistingEntities(manager);

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
                if (!Directory.Exists(entitiesDir))
                {
                    Debug.LogWarning($"[UnityAIBridge] Entities directory not found: {entitiesDir}");
                    yield break;
                }

                string[] entityFiles = Directory.GetFiles(entitiesDir, "*.json");
                var entityData = new List<(string uuid, JObject data, GameObject go)>();

                // PASS 1 – Instantiate
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Instantiating entities…", 0f);
                for (int i = 0; i < entityFiles.Length; i++)
                {
                    string path = entityFiles[i];
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", $"Instantiating {Path.GetFileName(path)}", (float)i / entityFiles.Length * 0.33f);

                    bool parseSuccess = false;
                    JObject data = null;
                    try
                    {
                        data = JObject.Parse(File.ReadAllText(path));
                        parseSuccess = true;
                    }
                    catch
                    {
                        Debug.LogWarning($"[UnityAIBridge] Failed to parse {path}. Skipping.");
                    }

                    if (!parseSuccess)
                    {
                        yield return null;
                        continue;
                    }

                    string uuid = data.Value<string>("uuid");
                    if (string.IsNullOrEmpty(uuid)) { yield return null; continue; }

                    string prefabPath = data.Value<string>("prefabPath");
                    GameObject go = InstantiateEntity(uuid, prefabPath, data.Value<string>("name"));

                    if (go != null)
                    {
                        manager.Register(uuid, go);
                        entityData.Add((uuid, data, go));
                    }
                    yield return null;
                }

                // PASS 2 – Wire Hierarchy
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Wiring hierarchy…", 0.33f);
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    string parentUuid = data.Value<string>("parentUuid");
                    if (!string.IsNullOrEmpty(parentUuid))
                    {
                        GameObject parentGo = manager.GetByUUID(parentUuid);
                        if (parentGo != null) go.transform.SetParent(parentGo.transform, false);
                    }
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", "Wiring...", 0.33f + (float)i / entityData.Count * 0.33f);
                }

                // PASS 2b – Apply sibling indices
                // Must run after all SetParent calls so every sibling exists before ordering.
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    int siblingIndex = data.Value<int?>("siblingIndex") ?? -1;
                    if (siblingIndex >= 0)
                        go.transform.SetSiblingIndex(siblingIndex);
                }

                // PASS 3 – Apply Transforms & Data
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Applying data…", 0.66f);
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    ApplyTransform(go, data["transform"] as JObject);
                    ReconcileComponents(go, data["customData"] as JArray);
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", "Finishing...", 0.66f + (float)i / entityData.Count * 0.34f);
                }

                // Mark scene dirty so Unity saves the newly instantiated persistent entities.
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                Debug.Log($"[UnityAIBridge] Bootstrap complete — {entityData.Count} entities loaded.");
            }
            finally
            {
                // 4. THE RESET: Always clear the UI and the lock
                EditorUtility.ClearProgressBar();
                _isBootstrapping = false;
            }
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

            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath))
            {
                _isBootstrapping = false;
                yield break;
            }

            try
            {
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
                        go = InstantiateEntity(uuid, data.Value<string>("prefabPath"), data.Value<string>("name"));
                        if (go == null) { yield return null; continue; }
                        manager.Register(uuid, go);
                    }

                    entityData.Add((uuid, data, go));
                    yield return null;
                }

                // PASS 2 – Wire hierarchy
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
                EditorUtility.DisplayProgressBar("Unity AI Bridge", "Applying data…", 0.66f);
                for (int i = 0; i < entityData.Count; i++)
                {
                    var (uuid, data, go) = entityData[i];
                    ApplyTransform(go, data["transform"] as JObject);
                    ReconcileComponents(go, data["customData"] as JArray);
                    EditorUtility.DisplayProgressBar("Unity AI Bridge", "Finishing...", 0.66f + (float)i / entityData.Count * 0.34f);
                }

                // Prune scene objects that have no backing JSON file
                PruneOrphanEntities(manager);

                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                Debug.Log($"[UnityAIBridge] Reconcile complete — {entityData.Count} entities.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isBootstrapping = false;
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

        /// <summary>
        /// Reconciles the MonoBehaviour components on a GameObject against the customData array.
        /// Components in the JSON are applied (added if missing, fields updated).
        /// MonoBehaviours on the GO that are absent from the JSON are removed.
        /// </summary>
        private static void ReconcileComponents(GameObject go, JArray customData)
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
                    Debug.LogWarning($"[UnityAIBridge] Failed to set field {prop.Key} on {type.Name}: {e.Message}");
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

            // --- THE FIX: Force Unity to see the new/updated file ---
            // We use ImportAsset because it's faster than a full Refresh() 
            // when we know exactly which file changed.
            string relativePath = GetRelativePath(filePath);
            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
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

            var root = new JObject
            {
                ["uuid"] = uuid,
                ["name"] = go.name,
                ["prefabPath"] = GetPrefabPath(go),
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
                    Debug.LogWarning($"[UnityAIBridge] Failed to serialize field {field.Name} on {type.Name}: {e.Message}");
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
                    go = InstantiateEntity(uuid, data.Value<string>("prefabPath"), data.Value<string>("name"));
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

            ApplyTransform(go, data["transform"] as JObject);
            ReconcileComponents(go, data["customData"] as JArray);

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

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Migrate all unmanaged scene objects into JSON sync.
            MigrateScene(manager);
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
        public static void MigrateScene(SceneDataManager manager)
        {
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            if (!Directory.Exists(entitiesDir))
            {
                Debug.LogError($"[UnityAIBridge] Entities directory not found: {entitiesDir}. Run Setup first.");
                return;
            }

            // Collect every GameObject in this scene (including inactive).
            // Traverse from roots so parents always precede children in the list.
            var allObjects = new List<GameObject>();
            foreach (var root in manager.gameObject.scene.GetRootGameObjects())
                CollectHierarchy(root, allObjects);

            // Pass 1 — assign EntitySync + UUID to unmanaged objects.
            // SuppressWriteEvents prevents ObjectChangeEvents from writing
            // individual JSON files while we're adding components in bulk.
            LiveSyncController.SuppressWriteEvents = true;
            int newCount = 0;
            foreach (var go in allObjects)
            {
                if (go.GetComponent<SceneDataManager>() != null) continue;
                if (go.GetComponent<EntitySync>() != null) continue;

                var sync = go.AddComponent<EntitySync>();
                sync.uuid = Guid.NewGuid().ToString();
                manager.Register(sync.uuid, go);
                newCount++;
            }
            LiveSyncController.SuppressWriteEvents = false;

            // Pass 2 — write JSON for all managed objects (new and pre-existing).
            // Pre-existing EntitySync objects may not be in the registry yet — register them.
            // WriteEntity's diff-guard skips unchanged files on repeat runs.
            int writtenCount = 0;
            foreach (var go in allObjects)
            {
                var sync = go.GetComponent<EntitySync>();
                if (sync == null || string.IsNullOrEmpty(sync.uuid)) continue;
                if (go.GetComponent<SceneDataManager>() != null) continue;

                if (manager.GetByUUID(sync.uuid) == null)
                    manager.Register(sync.uuid, go);

                WriteEntity(go, entitiesDir);
                writtenCount++;
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Debug.Log($"[UnityAIBridge] Migration complete — {newCount} new entit{(newCount == 1 ? "y" : "ies")} registered, {writtenCount} JSON file{(writtenCount == 1 ? "" : "s")} written.");
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
