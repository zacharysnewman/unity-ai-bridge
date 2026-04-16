using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityAIBridge;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Drives the Unity→JSON write pipeline (300 ms debounce).
    /// Listens for scene object changes via ObjectChangeEvents and flushes dirty entities to disk.
    /// JSON→Unity sync (create, modify, delete) is handled by EntityAssetPostprocessor.
    ///
    /// Initialized via [InitializeOnLoad]. Pauses write detection during Play Mode.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveSyncController
    {
        private const double DebounceSeconds = 0.3;

        // Dirty entities queued for write (uuid → debounce deadline)
        private static readonly Dictionary<string, double> _pendingWrites
            = new Dictionary<string, double>(StringComparer.Ordinal);

        // FSW used solely to trigger AssetDatabase.Refresh() when entity files change externally.
        // The actual sync logic lives in EntityAssetPostprocessor.
        private static FileSystemWatcher _entitiesWatcher;
        private static volatile int _pendingRefresh = 0; // 1 = refresh needed

        private static bool _isPlayMode;
        private static bool _isReloading;

        /// <summary>
        /// When true, HandleDestroyEvent and HandleCreateEvent skip their write/delete logic.
        /// Set during the BootstrapScene destruction pass to prevent JSON files from being
        /// deleted while entities are being re-instantiated.
        /// </summary>
        internal static bool SuppressWriteEvents = false;

        // ─── Static constructor (InitializeOnLoad) ────────────────────────────────
        static LiveSyncController()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // --- THE GUARD: If Unity is transitioning to Play, don't bootstrap here! ---
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Stop watcher cleanly before assembly reload; restart after
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

            // Hook into scene object change events for write pipeline
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;

            // Re-bootstrap when the active scene changes (e.g. opening a different scene)
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;

            // Bootstrap on first load (domain reload / recompile)
            EditorApplication.delayCall += TryBootstrap;
        }

        // ─── Domain reload handling ───────────────────────────────────────────────

        private static void OnBeforeAssemblyReload()
        {
            _isReloading = true;
            StopWatcher();
        }

        private static void OnAfterAssemblyReload()
        {
            _isReloading = false;
            EditorApplication.delayCall += TryBootstrap;
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (_isPlayMode) return;
            SceneIO.CancelBootstrap();
            StopWatcher();
            EditorApplication.delayCall += TryBootstrap;
        }

        // ─── Bootstrap ────────────────────────────────────────────────────────────

        private static void TryBootstrap()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            // Rebuild the registry from existing scene objects first (fast, no file I/O),
            // then reconcile against JSON to update/spawn/prune. JSON always wins on load.
            SceneIO.RebuildRegistry(manager);

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            if (Directory.Exists(entitiesDir))
                EditorCoroutineRunner.StartEditorCoroutine(SceneIO.ReconcileScene(manager));

            StartWatcher(manager.sceneDataPath);
        }

        // ─── File system watcher (refresh trigger only) ───────────────────────────

        private static void StartWatcher(string sceneDataPath)
        {
            StopWatcher();

            string entitiesDir = Path.Combine(sceneDataPath, "Entities");
            if (!Directory.Exists(entitiesDir)) return;

            _entitiesWatcher = new FileSystemWatcher(entitiesDir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            _entitiesWatcher.Created += OnEntityFileChanged;
            _entitiesWatcher.Changed += OnEntityFileChanged;
            _entitiesWatcher.Deleted += OnEntityFileChanged;
            _entitiesWatcher.Renamed += OnEntityFileChanged;
        }

        private static void StopWatcher()
        {
            if (_entitiesWatcher != null)
            {
                _entitiesWatcher.EnableRaisingEvents = false;
                _entitiesWatcher.Dispose();
                _entitiesWatcher = null;
            }
        }

        // Called on a background thread — just flag that a refresh is needed.
        private static void OnEntityFileChanged(object sender, FileSystemEventArgs e)
        {
            Interlocked.Exchange(ref _pendingRefresh, 1);
        }

        // ─── Editor update loop ───────────────────────────────────────────────────

        private static void OnEditorUpdate()
        {
            if (_isPlayMode || _isReloading) return;

            FlushWriteQueue();

            if (Interlocked.Exchange(ref _pendingRefresh, 0) == 1)
                AssetDatabase.Refresh();
        }

        // ─── Write pipeline (Unity Editor → JSON) ────────────────────────────────

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_isPlayMode) return;

            for (int i = 0; i < stream.length; i++)
            {
                var type = stream.GetEventType(i);
                switch (type)
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        {
                            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var ev);
                            MarkDirtyByInstanceId(ev.instanceId);
                            break;
                        }
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        {
                            stream.GetChangeGameObjectStructureEvent(i, out var ev);
                            MarkDirtyByInstanceId(ev.instanceId);
                            break;
                        }
                    case ObjectChangeKind.ChangeGameObjectParent:
                        {
                            stream.GetChangeGameObjectParentEvent(i, out var ev);
                            MarkDirtyByInstanceId(ev.instanceId);
                            break;
                        }
                    case ObjectChangeKind.ChangeChildrenOrder:
                        {
                            stream.GetChangeChildrenOrderEvent(i, out var ev);
                            // Event fires on the parent — mark all children dirty so
                            // their siblingIndex fields are written with the new order.
                            var parent = EditorUtility.InstanceIDToObject(ev.instanceId) as GameObject;
                            if (parent == null) break;
                            foreach (Transform child in parent.transform)
                                MarkDirtyByInstanceId(child.gameObject.GetInstanceID());
                            break;
                        }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        {
                            stream.GetCreateGameObjectHierarchyEvent(i, out var ev);
                            HandleCreateEvent(ev.instanceId);
                            break;
                        }
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        {
                            stream.GetDestroyGameObjectHierarchyEvent(i, out var ev);
                            HandleDestroyEvent(ev.instanceId);
                            break;
                        }
                }
            }
        }

        private static void MarkDirtyByInstanceId(int instanceId)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (go == null) return;

            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            sync.isDirty = true;
            _pendingWrites[sync.uuid] = EditorApplication.timeSinceStartup + DebounceSeconds;
        }

        private static void HandleCreateEvent(int instanceId)
        {
            if (SuppressWriteEvents) return;

            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            // Skip system objects — SceneDataManager is not an entity.
            if (go.GetComponent<SceneDataManager>() != null) return;

            var manager = SceneDataManager.Instance;
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            var sync = go.GetComponent<EntitySync>();

            if (sync != null)
            {
                // Object already has EntitySync — could be a duplicate (Ctrl+D).
                var registeredGo = manager.GetByUUID(sync.uuid);
                if (registeredGo == go)
                {
                    Debug.Log($"[UnityAIBridge] CreateEvent: '{go.name}' already registered, skipping");
                    return;
                }

                if (registeredGo != null)
                {
                    string oldUuid = sync.uuid;
                    sync.uuid = System.Guid.NewGuid().ToString();
                    Debug.Log($"[UnityAIBridge] CreateEvent: DUPLICATE '{go.name}' (was uuid={oldUuid}) — assigning new uuid={sync.uuid}");
                }
                else
                {
                    Debug.Log($"[UnityAIBridge] CreateEvent: '{go.name}' has EntitySync but unregistered — re-registering uuid={sync.uuid}");
                }

                manager.Register(sync.uuid, go);
                if (Directory.Exists(entitiesDir))
                    SceneIO.WriteEntity(go, entitiesDir);
                if (go.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(go.scene);
                return;
            }

            // Brand new object — attach EntitySync, assign UUID, register, write JSON.
            sync = go.AddComponent<EntitySync>();
            sync.uuid = System.Guid.NewGuid().ToString();
            Debug.Log($"[UnityAIBridge] CreateEvent: NEW '{go.name}' — assigned uuid={sync.uuid}");
            manager.Register(sync.uuid, go);

            if (Directory.Exists(entitiesDir))
                SceneIO.WriteEntity(go, entitiesDir);

            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static void HandleDestroyEvent(int instanceId)
        {
            if (SuppressWriteEvents) return;

            // ObjectChangeEvents fires after destruction — InstanceIDToObject returns null.
            // Defer to next frame and scan the registry for entries whose GO is gone.
            EditorApplication.delayCall += PruneDestroyedEntities;
        }

        private static void PruneDestroyedEntities()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null) return;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            var toRemove = new List<string>();

            foreach (string uuid in manager.GetAllUUIDs())
            {
                if (manager.GetByUUID(uuid) == null)
                    toRemove.Add(uuid);
            }

            if (toRemove.Count == 0)
            {
                Debug.Log("[UnityAIBridge] PruneDestroyed: no destroyed entities found in registry");
                return;
            }

            foreach (string uuid in toRemove)
            {
                string filePath = Path.Combine(entitiesDir, uuid + ".json");
                bool fileExisted = File.Exists(filePath);
                if (fileExisted)
                    File.Delete(filePath);
                manager.Unregister(uuid);
                Debug.Log($"[UnityAIBridge] PruneDestroyed: uuid={uuid} — removed from registry, json deleted={fileExisted}");
            }
        }

        private static void FlushWriteQueue()
        {
            if (_pendingWrites.Count == 0) return;

            double now = EditorApplication.timeSinceStartup;
            var manager = SceneDataManager.Instance;
            if (manager == null) return;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");

            var ready = new List<string>();
            foreach (var kv in _pendingWrites)
            {
                if (now >= kv.Value)
                    ready.Add(kv.Key);
            }

            foreach (string uuid in ready)
            {
                _pendingWrites.Remove(uuid);

                GameObject go = manager.GetByUUID(uuid);
                if (go == null) continue;

                var sync = go.GetComponent<EntitySync>();
                if (sync == null || !sync.isDirty) continue;

                SceneIO.WriteEntity(go, entitiesDir);
            }
        }

        // ─── Play Mode ────────────────────────────────────────────────────────────

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    _isPlayMode = true;
                    StopWatcher();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    _isPlayMode = false;
                    var manager = SceneDataManager.Instance;
                    if (manager != null && !string.IsNullOrEmpty(manager.sceneDataPath))
                    {
                        SceneIO.RebuildRegistry(manager);
                        StartWatcher(manager.sceneDataPath);
                    }
                    break;
            }
        }

        /// <summary>
        /// Removes all EntitySync components and the SceneDataManager from the scene,
        /// leaving the GameObjects intact. Does not delete any JSON files.
        /// </summary>
        [MenuItem("Unity AI Bridge/Clean Scene")]
        public static void CleanScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Clean Scene",
                "This will remove all EntitySync components and the SceneDataManager from the scene. GameObjects and JSON files will not be deleted.\n\nContinue?",
                "Clean", "Cancel"))
                return;

            int removedSyncs = 0;
            var syncs = UnityEngine.Object.FindObjectsByType<EntitySync>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var sync in syncs)
            {
                Undo.DestroyObjectImmediate(sync);
                removedSyncs++;
            }

            var manager = SceneDataManager.Instance;
            if (manager != null)
                Undo.DestroyObjectImmediate(manager.gameObject);

            StopWatcher();

            Debug.Log($"[UnityAIBridge] Clean Scene: removed {removedSyncs} EntitySync component(s) and SceneDataManager.");
        }

        /// <summary>
        /// Fully initializes the scene for JSON sync in one step:
        /// creates the SceneDataManager if absent, creates the directory structure
        /// and manifest if missing, then migrates all unmanaged objects into sync.
        /// Idempotent — safe to run on an already-initialized scene.
        /// The scene must be saved before initialization — sceneDataPath is derived
        /// from the scene file path automatically.
        /// </summary>
        [MenuItem("Unity AI Bridge/Initialize Scene")]
        public static void InitializeScene()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                EditorUtility.DisplayDialog(
                    "Scene Not Saved",
                    "Save the scene before initializing JSON sync. The data directory path is derived from the scene file path.",
                    "OK");
                return;
            }

            var manager = SceneDataManager.Instance;

            if (manager == null)
            {
                var go = new GameObject("SceneDataManager");
                manager = go.AddComponent<SceneDataManager>();
                Undo.RegisterCreatedObjectUndo(go, "Create SceneDataManager");
            }

            SceneIO.InitializeScene(manager);
        }

        /// <summary>
        /// Manually triggers a full scene reload. Can be called from menu items or tests.
        /// </summary>
        [MenuItem("Unity AI Bridge/Force Reload Scene")]
        public static void ForceReloadScene()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[UnityAIBridge] No SceneDataManager found in the active scene.");
                return;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            EditorCoroutineRunner.StartEditorCoroutine(SceneIO.BootstrapScene(manager));
        }

        /// <summary>
        /// Validates the current scene and logs the result.
        /// </summary>
        [MenuItem("Unity AI Bridge/Validate Scene")]
        public static void ValidateSceneMenu()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[UnityAIBridge] No SceneDataManager found in the active scene.");
                return;
            }

            var result = SceneIO.ValidateScene(manager.sceneDataPath);
            if (result.valid)
            {
                Debug.Log("[UnityAIBridge] Scene validation passed — no errors.");
            }
            else
            {
                foreach (var error in result.errors)
                    Debug.LogError($"[UnityAIBridge] Validation error [{error.uuid}] {error.field}: {error.message}");
            }
        }
    }

    // ─── Minimal editor coroutine runner ──────────────────────────────────────────

    /// <summary>
    /// Runs IEnumerator coroutines inside the Editor update loop.
    /// Unity's EditorCoroutineUtility (from com.unity.editorcoroutines) is not always
    /// available, so we provide a minimal fallback.
    /// </summary>
    internal static class EditorCoroutineRunner
    {
        private static readonly List<IEnumerator> _coroutines = new List<IEnumerator>();
        private static bool _hooked;

        public static void StartEditorCoroutine(IEnumerator coroutine)
        {
            _coroutines.Add(coroutine);
            if (!_hooked)
            {
                EditorApplication.update += Tick;
                _hooked = true;
            }
        }

        private static void Tick()
        {
            for (int i = _coroutines.Count - 1; i >= 0; i--)
            {
                if (!_coroutines[i].MoveNext())
                    _coroutines.RemoveAt(i);
            }

            if (_coroutines.Count == 0)
            {
                EditorApplication.update -= Tick;
                _hooked = false;
            }
        }
    }
}
