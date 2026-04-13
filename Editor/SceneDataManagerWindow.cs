using System.IO;
using UnityEditor;
using UnityEngine;
using JsonScenesForUnity;

namespace JsonScenesForUnity.Editor
{
    /// <summary>
    /// Minimal setup window for JSON Scenes for Unity.
    /// Open via: JSON Scenes → Setup Window
    /// </summary>
    public class SceneDataManagerWindow : EditorWindow
    {
        [MenuItem("JSON Scenes/Setup Window")]
        public static void Open()
        {
            var window = GetWindow<SceneDataManagerWindow>("JSON Scenes Setup");
            window.minSize = new Vector2(400, 280);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("JSON Scenes for Unity", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawManagerSection();
            EditorGUILayout.Space(8);
            DrawActionsSection();
        }

        // ─── SceneDataManager section ─────────────────────────────────────────────

        private void DrawManagerSection()
        {
            GUILayout.Label("Scene Data Manager", EditorStyles.boldLabel);

            var manager = SceneDataManager.Instance;

            if (manager == null)
            {
                EditorGUILayout.HelpBox(
                    "No SceneDataManager found in the active scene.\n" +
                    "Add a GameObject with a SceneDataManager component and set its Scene Data Path.",
                    MessageType.Warning);

                if (GUILayout.Button("Create SceneDataManager in Scene"))
                    CreateSceneDataManager();

                return;
            }

            EditorGUI.BeginChangeCheck();
            string newPath = EditorGUILayout.TextField("Scene Data Path", manager.sceneDataPath);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(manager, "Set Scene Data Path");
                manager.sceneDataPath = newPath;
                EditorUtility.SetDirty(manager);
            }

            if (!string.IsNullOrEmpty(manager.sceneDataPath))
            {
                bool initialized = Directory.Exists(manager.sceneDataPath)
                    && File.Exists(Path.Combine(manager.sceneDataPath, "manifest.json"));

                EditorGUILayout.HelpBox(
                    initialized
                        ? $"Ready — {manager.sceneDataPath}"
                        : "Path set. Click Initialize Scene to create the directory structure and sync existing objects.",
                    initialized ? MessageType.Info : MessageType.Warning);
            }
        }

        // ─── Actions section ──────────────────────────────────────────────────────

        private void DrawActionsSection()
        {
            GUILayout.Label("Actions", EditorStyles.boldLabel);

            var manager = SceneDataManager.Instance;
            bool hasPath = manager != null && !string.IsNullOrEmpty(manager.sceneDataPath);
            bool initialized = hasPath
                && Directory.Exists(manager.sceneDataPath)
                && File.Exists(Path.Combine(manager.sceneDataPath, "manifest.json"));

            EditorGUI.BeginDisabledGroup(!hasPath);

            if (GUILayout.Button("Initialize Scene"))
                LiveSyncController.InitializeScene();

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!initialized);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Force Reload Scene"))
                LiveSyncController.ForceReloadScene();

            if (GUILayout.Button("Validate Scene"))
                LiveSyncController.ValidateSceneMenu();

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static void CreateSceneDataManager()
        {
            var go = new GameObject("SceneDataManager");
            go.AddComponent<SceneDataManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create SceneDataManager");
            Selection.activeGameObject = go;
        }

    }
}
