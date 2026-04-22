using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAIBridge;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Bidirectional sync between the Unity editor state and editor-state.json.
    ///
    /// editor-state.json contains the full editor state: selected UUIDs, active scene path,
    /// scene view camera position/rotation, and UUIDs of objects visible in the frustum.
    ///
    /// Unity → JSON: writes are event-driven — selection changes, scene view camera moves,
    ///   and active scene changes each trigger a write. A diff guard prevents disk writes
    ///   when the computed state is identical to what was last written.
    /// JSON → Unity: external writes to editor-state.json are applied to Selection.objects.
    ///
    /// Lifecycle is managed by LiveSyncController — call Initialize() once from its
    /// static constructor, then StartWatcher/StopWatcher alongside the entities watcher.
    /// </summary>
    internal static class EditorStateSync
    {
        private const string SelectionFileName = "editor-state.json";

        private static FileSystemWatcher _watcher;
        private static string _sceneDataPath;
        private static volatile int _pendingRead = 0;
        private static bool _suppressWrite = false;
        private static bool _isPlayMode = false;
        private static string _lastWrittenJson = null;

        // Last known camera state — used to detect movement in OnSceneGuiUpdate.
        private static Vector3 _lastCamPos;
        private static Quaternion _lastCamRot;

        // ─── Data classes ──────────────────────────────────────────────────────────

        private class EditorStateData
        {
            [JsonProperty("selection")]
            public List<string> selection = new List<string>();

            [JsonProperty("scenePath")]
            public string scenePath;

            [JsonProperty("sceneCamera")]
            public CameraData sceneCamera;

            [JsonProperty("visibleObjects")]
            public List<string> visibleObjects = new List<string>();
        }

        private class CameraData
        {
            [JsonProperty("pos")]
            public float[] pos;

            [JsonProperty("rot")]
            public float[] rot;
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGuiUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        public static void StartWatcher(string sceneDataPath)
        {
            StopWatcher();
            _sceneDataPath = sceneDataPath;

            if (!Directory.Exists(sceneDataPath)) return;

            _watcher = new FileSystemWatcher(sceneDataPath, SelectionFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
        }

        public static void StopWatcher()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            _sceneDataPath = null;
        }

        // ─── JSON → Unity (external file change) ─────────────────────────────────

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Interlocked.Exchange(ref _pendingRead, 1);
        }

        private static void OnEditorUpdate()
        {
            if (_isPlayMode) return;
            if (Interlocked.Exchange(ref _pendingRead, 0) == 1)
                ApplySelectionFromFile();
        }

        private static void OnSceneGuiUpdate(SceneView sv)
        {
            if (_isPlayMode || string.IsNullOrEmpty(_sceneDataPath)) return;

            var cam = sv.camera;
            if (cam == null) return;

            var t = cam.transform;
            if (t.position == _lastCamPos && t.rotation == _lastCamRot) return;

            _lastCamPos = t.position;
            _lastCamRot = t.rotation;
            WriteFullState(_sceneDataPath);
        }

        private static void OnActiveSceneChanged(
            UnityEngine.SceneManagement.Scene previous,
            UnityEngine.SceneManagement.Scene next)
        {
            if (_isPlayMode || string.IsNullOrEmpty(_sceneDataPath)) return;
            WriteFullState(_sceneDataPath);
        }

        private static void ApplySelectionFromFile()
        {
            if (string.IsNullOrEmpty(_sceneDataPath)) return;

            var manager = SceneDataManager.Instance;
            if (manager == null) return;

            string path = GetSelectionPath(_sceneDataPath);
            if (!File.Exists(path)) return;

            string json;
            try { json = File.ReadAllText(path); }
            catch { return; }

            List<string> uuids;
            try
            {
                var state = JsonConvert.DeserializeObject<EditorStateData>(json);
                uuids = state?.selection ?? new List<string>();
            }
            catch
            {
                // Fallback: old flat-array format
                try { uuids = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>(); }
                catch { return; }
            }

            var incomingSet = new HashSet<string>(
                uuids.Where(u => manager.GetByUUID(u) != null), StringComparer.Ordinal);

            var currentSet = new HashSet<string>(
                Selection.gameObjects
                    .Select(g => g.GetComponent<EntitySync>()?.uuid)
                    .Where(u => !string.IsNullOrEmpty(u)), StringComparer.Ordinal);

            if (incomingSet.SetEquals(currentSet)) return;

            var objects = uuids
                .Select(u => manager.GetByUUID(u))
                .Where(go => go != null)
                .Cast<UnityEngine.Object>()
                .ToArray();

            _suppressWrite = true;
            Selection.objects = objects;
            _suppressWrite = false;
        }

        // ─── Unity → JSON (event-driven writes) ───────────────────────────────────

        private static void OnSelectionChanged()
        {
            if (_isPlayMode || _suppressWrite) return;

            var manager = SceneDataManager.Instance;
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            WriteFullState(manager.sceneDataPath);
        }

        private static void WriteFullState(string sceneDataPath)
        {
            var manager = SceneDataManager.Instance;

            var selection = Selection.gameObjects
                .Select(go => go.GetComponent<EntitySync>()?.uuid)
                .Where(u => !string.IsNullOrEmpty(u))
                .ToList();

            string scenePath = EditorSceneManager.GetActiveScene().path;

            CameraData cameraData = null;
            var visibleObjects = new List<string>();
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                var t = sv.camera.transform;
                cameraData = new CameraData
                {
                    pos = new[] { t.position.x, t.position.y, t.position.z },
                    rot = new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z },
                };

                if (manager != null)
                    visibleObjects = ComputeVisibleObjects(sv.camera, manager);
            }

            var state = new EditorStateData
            {
                selection = selection,
                scenePath = scenePath,
                sceneCamera = cameraData,
                visibleObjects = visibleObjects,
            };

            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            if (json == _lastWrittenJson) return;
            _lastWrittenJson = json;

            string path = GetSelectionPath(sceneDataPath);
            try { File.WriteAllText(path, json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAIBridge] EditorStateSync: failed to write {path}: {e.Message}");
            }
        }

        private static List<string> ComputeVisibleObjects(Camera camera, SceneDataManager manager)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var result = new List<string>();

            foreach (string uuid in manager.GetAllUUIDs())
            {
                var go = manager.GetByUUID(uuid);
                if (go == null || !go.activeInHierarchy) continue;

                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    foreach (var r in renderers)
                    {
                        if (GeometryUtility.TestPlanesAABB(planes, r.bounds))
                        {
                            result.Add(uuid);
                            break;
                        }
                    }
                }
                else
                {
                    // No renderer — test position with minimal bounds
                    var bounds = new Bounds(go.transform.position, Vector3.one * 0.1f);
                    if (GeometryUtility.TestPlanesAABB(planes, bounds))
                        result.Add(uuid);
                }
            }

            return result;
        }

        // ─── Play mode ────────────────────────────────────────────────────────────

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    _isPlayMode = true;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    _isPlayMode = false;
                    break;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        internal static string GetSelectionPath(string sceneDataPath)
            => Path.Combine(sceneDataPath, SelectionFileName);
    }
}
