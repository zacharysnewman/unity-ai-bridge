using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityAIBridge;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Bidirectional sync between the Unity editor selection and selection.json.
    ///
    /// Unity → JSON: whenever the editor selection changes, the UUIDs of all selected
    ///   entities are written to selection.json. Non-entity GameObjects are ignored.
    ///
    /// JSON → Unity: when selection.json is written externally (e.g. by a CLI tool),
    ///   the UUIDs are resolved to GameObjects and applied to Selection.objects.
    ///
    /// Lifecycle is managed by LiveSyncController — call Initialize() once from its
    /// static constructor, then StartWatcher/StopWatcher alongside the entities watcher.
    /// </summary>
    internal static class SelectionSync
    {
        private const string SelectionFileName = "selection.json";

        private static FileSystemWatcher _watcher;
        private static string _sceneDataPath;
        private static volatile int _pendingRead = 0;

        // Set true while applying an external file change so OnSelectionChanged
        // doesn't echo it back to disk, preventing an infinite write loop.
        private static bool _suppressWrite = false;
        private static bool _isPlayMode = false;

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers editor callbacks. Called once from LiveSyncController's static constructor.
        /// </summary>
        public static void Initialize()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// Starts watching selection.json in the given scene data directory.
        /// Called by LiveSyncController alongside StartWatcher for entities.
        /// </summary>
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

        /// <summary>
        /// Stops watching selection.json. Called by LiveSyncController.
        /// </summary>
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

        // Called on background FSW thread — just flag the pending read.
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
            try { uuids = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>(); }
            catch { return; }

            // Diff guard: skip if the resolved entity selection is already identical.
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

        // ─── Unity → JSON (editor selection changed) ──────────────────────────────

        private static void OnSelectionChanged()
        {
            if (_isPlayMode || _suppressWrite) return;

            var manager = SceneDataManager.Instance;
            if (manager == null || string.IsNullOrEmpty(manager.sceneDataPath)) return;

            var uuids = Selection.gameObjects
                .Select(go => go.GetComponent<EntitySync>()?.uuid)
                .Where(u => !string.IsNullOrEmpty(u))
                .ToList();

            WriteSelectionFile(manager.sceneDataPath, uuids);
        }

        private static void WriteSelectionFile(string sceneDataPath, List<string> uuids)
        {
            string path = GetSelectionPath(sceneDataPath);
            string json = JsonConvert.SerializeObject(uuids, Formatting.Indented);

            try { File.WriteAllText(path, json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAIBridge] SelectionSync: failed to write {path}: {e.Message}");
            }
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
