using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using JsonScenesForUnity;

namespace JsonScenesForUnity.Editor
{
    /// <summary>
    /// Manages the FileSystemWatcher on the Entities/ and Commands/ directories.
    /// Drives the debounced write pipeline (300 ms) and routes hot-reload events to SceneIO
    /// on the main Unity thread via EditorApplication.delayCall.
    ///
    /// Initialized via [InitializeOnLoad]. Pauses write detection during Play Mode.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveSyncController
    {
        private const double DebounceSeconds = 0.3;

        private static FileSystemWatcher _entitiesWatcher;
        private static FileSystemWatcher _commandsWatcher;

        // Pending hot-reload events (file path → event type)
        private static readonly Queue<(string path, WatcherChangeTypes changeType)> _pendingFileEvents
            = new Queue<(string, WatcherChangeTypes)>();

        // Dirty entities queued for write (uuid → debounce deadline)
        private static readonly Dictionary<string, double> _pendingWrites
            = new Dictionary<string, double>(StringComparer.Ordinal);

        private static bool _isPlayMode;

        // ─── Static constructor (InitializeOnLoad) ────────────────────────────────

        static LiveSyncController()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Hook into scene object change events for write pipeline
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;

            // Bootstrap when domain reloads (scene open / recompile)
            EditorApplication.delayCall += TryBootstrap;
        }

        // ─── Bootstrap ────────────────────────────────────────────────────────────

        private static void TryBootstrap()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            EditorCoroutineRunner.StartEditorCoroutine(SceneIO.BootstrapScene(manager));
            StartWatcher(manager.sceneDataPath);
        }

        // ─── File system watcher ──────────────────────────────────────────────────

        private static void StartWatcher(string sceneDataPath)
        {
            StopWatcher();

            string entitiesDir = Path.Combine(sceneDataPath, "Entities");
            string commandsDir = Path.Combine(sceneDataPath, "Commands");

            if (Directory.Exists(entitiesDir))
            {
                _entitiesWatcher = CreateWatcher(entitiesDir, "*.json");
            }

            if (Directory.Exists(commandsDir))
            {
                _commandsWatcher = CreateWatcher(commandsDir, "*.json");
            }
        }

        private static FileSystemWatcher CreateWatcher(string directory, string filter)
        {
            var watcher = new FileSystemWatcher(directory, filter)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };

            watcher.Created += OnFileEvent;
            watcher.Changed += OnFileEvent;
            watcher.Deleted += OnFileDeleted;

            return watcher;
        }

        private static void StopWatcher()
        {
            if (_entitiesWatcher != null)
            {
                _entitiesWatcher.EnableRaisingEvents = false;
                _entitiesWatcher.Dispose();
                _entitiesWatcher = null;
            }
            if (_commandsWatcher != null)
            {
                _commandsWatcher.EnableRaisingEvents = false;
                _commandsWatcher.Dispose();
                _commandsWatcher = null;
            }
        }

        // FSW callbacks — called on a background thread; marshal to main thread
        private static void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            lock (_pendingFileEvents)
                _pendingFileEvents.Enqueue((e.FullPath, e.ChangeType));
        }

        private static void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            lock (_pendingFileEvents)
                _pendingFileEvents.Enqueue((e.FullPath, WatcherChangeTypes.Deleted));
        }

        // ─── Editor update loop ───────────────────────────────────────────────────

        private static void OnEditorUpdate()
        {
            if (_isPlayMode) return;

            // Flush FSW events on main thread
            FlushFileEvents();

            // Flush debounced write queue
            FlushWriteQueue();
        }

        private static void FlushFileEvents()
        {
            List<(string path, WatcherChangeTypes changeType)> events = null;

            lock (_pendingFileEvents)
            {
                if (_pendingFileEvents.Count == 0) return;
                events = new List<(string, WatcherChangeTypes)>(_pendingFileEvents.Count);
                while (_pendingFileEvents.Count > 0)
                    events.Add(_pendingFileEvents.Dequeue());
            }

            var manager = SceneDataManager.Instance;
            if (manager == null) return;

            string sceneDataPath = manager.sceneDataPath;
            string entitiesDir = Path.Combine(sceneDataPath, "Entities");
            string commandsDir = Path.Combine(sceneDataPath, "Commands");

            foreach (var (path, changeType) in events)
            {
                bool isInEntities = path.StartsWith(entitiesDir, StringComparison.OrdinalIgnoreCase);
                bool isInCommands = path.StartsWith(commandsDir, StringComparison.OrdinalIgnoreCase);

                if (isInEntities)
                {
                    HandleEntityFileEvent(path, changeType, manager);
                }
                else if (isInCommands)
                {
                    HandleCommandFileEvent(path, manager);
                }
            }
        }

        private static void HandleEntityFileEvent(string filePath, WatcherChangeTypes changeType, SceneDataManager manager)
        {
            if (changeType == WatcherChangeTypes.Deleted)
            {
                string uuid = Path.GetFileNameWithoutExtension(filePath);
                SceneIO.DestroyEntity(uuid, manager);
            }
            else
            {
                // Created or Changed — external write wins
                SceneIO.HotReloadEntity(filePath, manager);
            }
        }

        private static void HandleCommandFileEvent(string filePath, SceneDataManager manager)
        {
            if (!File.Exists(filePath)) return;

            JObject command;
            try
            {
                command = JObject.Parse(File.ReadAllText(filePath));
            }
            catch
            {
                return;
            }

            // Delete command file immediately
            try { File.Delete(filePath); } catch { }

            string commandName = command.Value<string>("command");
            string targetUuid = command.Value<string>("targetUuid");

            switch (commandName)
            {
                case "force_reload":
                    ExecuteForceReload(targetUuid, manager);
                    break;

                case "validate_scene":
                    ExecuteValidateScene(manager);
                    break;

                default:
                    Debug.LogWarning($"[JsonScenes] Unknown command: {commandName}");
                    break;
            }
        }

        // ─── Commands ─────────────────────────────────────────────────────────────

        private static void ExecuteForceReload(string targetUuid, SceneDataManager manager)
        {
            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");

            if (!string.IsNullOrEmpty(targetUuid))
            {
                string filePath = Path.Combine(entitiesDir, targetUuid + ".json");
                if (File.Exists(filePath))
                    SceneIO.HotReloadEntity(filePath, manager);
                else
                    SceneIO.DestroyEntity(targetUuid, manager);
            }
            else
            {
                // Full scene reload
                EditorCoroutineRunner.StartEditorCoroutine(SceneIO.BootstrapScene(manager));
            }
        }

        private static void ExecuteValidateScene(SceneDataManager manager)
        {
            var result = SceneIO.ValidateScene(manager.sceneDataPath);
            string commandsDir = Path.Combine(manager.sceneDataPath, "Commands");

            var output = new JObject
            {
                ["valid"] = result.valid,
                ["errors"] = new JArray()
            };

            foreach (var error in result.errors)
            {
                ((JArray)output["errors"]).Add(new JObject
                {
                    ["uuid"] = error.uuid,
                    ["field"] = error.field,
                    ["message"] = error.message,
                });
            }

            string resultPath = Path.Combine(commandsDir, "validate_result.json");
            File.WriteAllText(resultPath, output.ToString(Formatting.Indented));
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
                    case ObjectChangeKind.ChangeTransformParenting:
                    {
                        stream.GetChangeTransformParentingEvent(i, out var ev);
                        MarkDirtyByInstanceId(ev.instanceId);
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
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            sync.isDirty = true;
            _pendingWrites[sync.uuid] = EditorApplication.timeSinceStartup + DebounceSeconds;
        }

        private static void HandleCreateEvent(int instanceId)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            // Only manage objects that don't already have EntitySync (avoid double-processing)
            if (go.GetComponent<EntitySync>() != null) return;

            go.hideFlags = HideFlags.DontSave;
            // UUID assignment deferred to EntityAssetPostprocessor when the file is written
        }

        private static void HandleDestroyEvent(int instanceId)
        {
            // Object is being destroyed — find its EntitySync before it's gone
            // (instanceId may still resolve briefly)
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            var manager = SceneDataManager.Instance;
            if (manager == null) return;

            string entitiesDir = Path.Combine(manager.sceneDataPath, "Entities");
            string filePath = Path.Combine(entitiesDir, sync.uuid + ".json");

            if (File.Exists(filePath))
                File.Delete(filePath);

            manager.Unregister(sync.uuid);
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
                    // Re-bootstrap after play mode exits
                    var manager = SceneDataManager.Instance;
                    if (manager != null && !string.IsNullOrEmpty(manager.sceneDataPath))
                    {
                        EditorCoroutineRunner.StartEditorCoroutine(SceneIO.BootstrapScene(manager));
                        StartWatcher(manager.sceneDataPath);
                    }
                    break;
            }
        }

        /// <summary>
        /// Manually triggers a full scene reload. Can be called from menu items or tests.
        /// </summary>
        [MenuItem("JSON Scenes/Force Reload Scene")]
        public static void ForceReloadScene()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[JsonScenes] No SceneDataManager found in the active scene.");
                return;
            }
            EditorCoroutineRunner.StartEditorCoroutine(SceneIO.BootstrapScene(manager));
        }

        /// <summary>
        /// Validates the current scene and logs the result.
        /// </summary>
        [MenuItem("JSON Scenes/Validate Scene")]
        public static void ValidateSceneMenu()
        {
            var manager = SceneDataManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[JsonScenes] No SceneDataManager found in the active scene.");
                return;
            }

            var result = SceneIO.ValidateScene(manager.sceneDataPath);
            if (result.valid)
            {
                Debug.Log("[JsonScenes] Scene validation passed — no errors.");
            }
            else
            {
                foreach (var error in result.errors)
                    Debug.LogError($"[JsonScenes] Validation error [{error.uuid}] {error.field}: {error.message}");
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
