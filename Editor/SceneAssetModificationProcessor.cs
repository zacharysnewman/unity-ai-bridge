using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Keeps the JSON data directory in sync when a shell .unity scene file is
    /// renamed, moved, or deleted via the Unity Project window.
    ///
    /// Path convention (shared with SceneDataManager.sceneDataPath):
    ///   Assets/Scenes/Level_01.unity  →  Assets/SceneData/Scenes/Level_01
    ///
    /// NOTE: Only Project-window operations are intercepted. OS-level renames
    /// (Finder, `mv`) bypass AssetModificationProcessor entirely; those require
    /// a manual AssetDatabase.Refresh and the user to move the data directory.
    /// </summary>
    public class SceneAssetModificationProcessor : AssetModificationProcessor
    {
        private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
        {
            if (!sourcePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return AssetMoveResult.DidNotMove;

            string oldDataDir = ScenePathToDataDir(sourcePath);
            string newDataDir = ScenePathToDataDir(destinationPath);

            if (string.Equals(oldDataDir, newDataDir, StringComparison.OrdinalIgnoreCase))
                return AssetMoveResult.DidNotMove;

            string fullOld = Path.GetFullPath(oldDataDir);
            if (!Directory.Exists(fullOld))
                return AssetMoveResult.DidNotMove;

            string fullNew = Path.GetFullPath(newDataDir);
            string oldParent = Path.GetDirectoryName(oldDataDir).Replace('\\', '/');

            try
            {
                // Use filesystem ops — AssetDatabase write calls (CreateFolder, MoveAsset)
                // are unreliable from within a modification callback.
                Directory.CreateDirectory(Path.GetDirectoryName(fullNew));
                Directory.Move(fullOld, fullNew);

                // Move the folder's .meta file so Unity doesn't lose asset tracking.
                string oldMeta = fullOld + ".meta";
                string newMeta = fullNew + ".meta";
                if (File.Exists(oldMeta))
                    File.Move(oldMeta, newMeta);

                Debug.Log($"[UnityAIBridge] Scene data directory moved: {oldDataDir} → {newDataDir}");

                // Refresh and prune deferred — can't safely call AssetDatabase here.
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();
                    PruneEmptyParents(oldParent);
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAIBridge] Failed to move scene data directory: {e.Message}");
            }

            // Return DidNotMove — Unity handles the .unity file itself.
            return AssetMoveResult.DidNotMove;
        }

        private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return AssetDeleteResult.DidNotDelete;

            string dataDir = ScenePathToDataDir(assetPath);

            if (!AssetDatabase.IsValidFolder(dataDir))
                return AssetDeleteResult.DidNotDelete;

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Scene Data?",
                $"Delete the associated JSON data directory?\n\n{dataDir}\n\nThis cannot be undone.",
                "Delete", "Keep");

            if (confirm)
                AssetDatabase.DeleteAsset(dataDir);

            // Return DidNotDelete — Unity handles the .unity file itself regardless.
            return AssetDeleteResult.DidNotDelete;
        }

        /// <summary>
        /// Converts a .unity scene asset path to its corresponding data directory path.
        /// Mirrors folder structure under Assets/SceneData/:
        ///   Assets/Scenes/Level_01.unity → Assets/SceneData/Scenes/Level_01
        /// Matches SceneDataManager.sceneDataPath convention exactly.
        /// </summary>
        internal static string ScenePathToDataDir(string scenePath)
        {
            string withoutExt = scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                ? scenePath.Substring(0, scenePath.Length - ".unity".Length)
                : scenePath;

            if (withoutExt.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return "Assets/SceneData/" + withoutExt.Substring("Assets/".Length);

            return "Assets/SceneData/" + withoutExt;
        }

        /// <summary>
        /// Walks up from folderPath toward Assets/SceneData, deleting each folder
        /// that is empty (ignoring .meta files). Stops at Assets/SceneData itself.
        /// Uses the filesystem directly to avoid stale AssetDatabase state after a move.
        /// </summary>
        private static void PruneEmptyParents(string folderPath)
        {
            const string sceneDataRoot = "Assets/SceneData";

            while (!string.IsNullOrEmpty(folderPath) &&
                   !string.Equals(folderPath, sceneDataRoot, StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(folderPath);
                if (!Directory.Exists(fullPath)) break;

                bool isEmpty = true;
                foreach (string entry in Directory.GetFileSystemEntries(fullPath))
                {
                    if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        isEmpty = false;
                        break;
                    }
                }

                if (!isEmpty) break;

                string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
                AssetDatabase.DeleteAsset(folderPath);
                Debug.Log($"[UnityAIBridge] Removed empty SceneData folder: {folderPath}");
                folderPath = parent;
            }
        }

    }
}
