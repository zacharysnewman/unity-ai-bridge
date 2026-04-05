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

            bool pathExists = !string.IsNullOrEmpty(manager.sceneDataPath)
                && Directory.Exists(manager.sceneDataPath);

            bool manifestExists = pathExists
                && File.Exists(Path.Combine(manager.sceneDataPath, "manifest.json"));

            if (!string.IsNullOrEmpty(manager.sceneDataPath))
            {
                if (!pathExists)
                {
                    EditorGUILayout.HelpBox(
                        $"Directory not found: {manager.sceneDataPath}\nCreate the folder structure or fix the path.",
                        MessageType.Error);

                    if (GUILayout.Button("Create Directory Structure"))
                        CreateDirectoryStructure(manager.sceneDataPath);
                }
                else if (!manifestExists)
                {
                    EditorGUILayout.HelpBox(
                        "Directory exists but manifest.json is missing.",
                        MessageType.Warning);

                    if (GUILayout.Button("Create manifest.json"))
                        CreateManifest(manager.sceneDataPath);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Scene data directory ready.\n{manager.sceneDataPath}",
                        MessageType.Info);
                }
            }
        }

        // ─── Actions section ──────────────────────────────────────────────────────

        private void DrawActionsSection()
        {
            GUILayout.Label("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Force Reload Scene"))
                LiveSyncController.ForceReloadScene();

            if (GUILayout.Button("Validate Scene"))
                LiveSyncController.ValidateSceneMenu();

            EditorGUILayout.EndHorizontal();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static void CreateSceneDataManager()
        {
            var go = new GameObject("SceneDataManager");
            go.AddComponent<SceneDataManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create SceneDataManager");
            Selection.activeGameObject = go;
        }

        private static void CreateDirectoryStructure(string sceneDataPath)
        {
            Directory.CreateDirectory(Path.Combine(sceneDataPath, "Entities"));
            Directory.CreateDirectory(Path.Combine(sceneDataPath, "Commands"));
            CreateManifest(sceneDataPath);
            AssetDatabase.Refresh();
            Debug.Log($"[JsonScenes] Created directory structure at {sceneDataPath}");
        }

        private static void CreateManifest(string sceneDataPath)
        {
            string manifestPath = Path.Combine(sceneDataPath, "manifest.json");
            string sceneName = Path.GetFileName(sceneDataPath.TrimEnd('/', '\\'));
            File.WriteAllText(manifestPath,
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                $"  \"sceneName\": \"{sceneName}\"\n" +
                "}\n");
            AssetDatabase.Refresh();
            Debug.Log($"[JsonScenes] Created manifest.json at {manifestPath}");
        }
    }
}
