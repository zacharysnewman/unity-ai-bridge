using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using JsonScenesForUnity;

namespace JsonScenesForUnity.Editor
{
    /// <summary>
    /// Primary sync bridge between the Unity asset pipeline and the scene.
    /// Fires whenever files in Assets/ change (create, modify, delete, move).
    ///
    /// Responsibilities:
    ///   - UUID injection: ensures every entity file's uuid field matches its filename.
    ///       Missing/empty uuid  → generate new UUID, write it, rename file to [uuid].json
    ///       uuid ≠ filename     → treat as duplicate (Ctrl+D). Generate new UUID, rename.
    ///   - Hot reload: spawns or updates the scene object when an entity file is created/modified.
    ///   - Destroy: removes the scene object when an entity file is deleted.
    /// </summary>
    public class EntityAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var manager = SceneDataManager.Instance;

            foreach (string assetPath in importedAssets)
            {
                if (!IsEntityFile(assetPath)) continue;

                Debug.Log($"[JsonScenes] Postprocessor: imported entity file — {assetPath} | manager={(manager != null ? manager.sceneDataPath : "NULL")}");

                bool valid = EnsureValidUuid(assetPath);
                if (!valid)
                {
                    Debug.Log($"[JsonScenes] Postprocessor: UUID fixed/renamed, skipping hot reload for this pass — {assetPath}");
                    continue;
                }

                if (manager == null)
                {
                    Debug.LogWarning($"[JsonScenes] Postprocessor: no SceneDataManager found — cannot hot reload {assetPath}");
                    continue;
                }

                SceneIO.HotReloadEntity(Path.GetFullPath(assetPath), manager);
            }

            foreach (string assetPath in deletedAssets)
            {
                if (!IsEntityFile(assetPath)) continue;
                string uuid = Path.GetFileNameWithoutExtension(assetPath);
                Debug.Log($"[JsonScenes] Postprocessor: deleted entity file — uuid={uuid} | manager={(manager != null ? "found" : "NULL")}");
                if (manager != null)
                    SceneIO.DestroyEntity(uuid, manager);
            }

            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                HandleSceneDuplicate(assetPath);
            }
        }

        /// <summary>
        /// When a new .unity file appears with no data directory, checks if it looks like
        /// a Project-window duplicate (Unity appends " 1", " 2", etc. to the name).
        /// If the inferred source has a data directory, copies it to the new scene's path.
        /// </summary>
        private static void HandleSceneDuplicate(string newScenePath)
        {
            string newDataDir = SceneAssetModificationProcessor.ScenePathToDataDir(newScenePath);
            if (Directory.Exists(Path.GetFullPath(newDataDir)))
                return; // Data dir already exists — not a duplicate

            string sourceScenePath = FindDuplicateSource(newScenePath);
            if (sourceScenePath == null) return;

            string sourceDataDir = SceneAssetModificationProcessor.ScenePathToDataDir(sourceScenePath);
            string fullSource = Path.GetFullPath(sourceDataDir);
            if (!Directory.Exists(fullSource)) return;

            string fullDest = Path.GetFullPath(newDataDir);
            CopyDirectory(fullSource, fullDest);
            AssetDatabase.Refresh();
            Debug.Log($"[JsonScenes] Duplicated scene data: {sourceDataDir} → {newDataDir}");
        }

        /// <summary>
        /// Infers the source scene path for a Unity duplicate by stripping the " N" suffix
        /// Unity appends (e.g. "Level_01 1.unity" → "Level_01.unity").
        /// Returns null if the pattern doesn't match or no source file exists.
        /// </summary>
        private static string FindDuplicateSource(string newScenePath)
        {
            string dir = Path.GetDirectoryName(newScenePath).Replace('\\', '/');
            string nameWithoutExt = Path.GetFileNameWithoutExtension(newScenePath);

            var match = Regex.Match(nameWithoutExt, @"^(.*)\s+\d+$");
            if (!match.Success) return null;

            string sourcePath = dir + "/" + match.Groups[1].Value + ".unity";
            return File.Exists(Path.GetFullPath(sourcePath)) ? sourcePath : null;
        }

        /// <summary>
        /// Recursively copies a directory. Skips .meta files — Unity generates fresh ones on refresh.
        /// </summary>
        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
            foreach (string subDir in Directory.GetDirectories(source))
            {
                CopyDirectory(subDir, Path.Combine(destination, Path.GetFileName(subDir)));
            }
        }

        private static bool IsEntityFile(string assetPath)
        {
            if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return false;

            return assetPath.Contains("/Entities/") || assetPath.Contains("\\Entities\\");
        }

        /// <summary>
        /// Ensures the entity file has a UUID that matches its filename.
        /// Returns true if the file was already valid (no rename needed).
        /// Returns false if the file was renamed — caller should skip hot reload
        /// since the renamed file will trigger a fresh import.
        /// </summary>
        private static bool EnsureValidUuid(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath)) return false;

            JObject data;
            try
            {
                data = JObject.Parse(File.ReadAllText(fullPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[JsonScenes] EntityAssetPostprocessor: Failed to parse {assetPath}: {e.Message}");
                return false;
            }

            string existingUuid = data.Value<string>("uuid");
            string fileUuid = Path.GetFileNameWithoutExtension(assetPath);

            bool missingUuid = string.IsNullOrEmpty(existingUuid);
            bool uuidMismatch = !missingUuid && !string.Equals(existingUuid, fileUuid, StringComparison.OrdinalIgnoreCase);

            if (!missingUuid && !uuidMismatch)
                return true; // Already valid

            string newUuid = Guid.NewGuid().ToString();
            data["uuid"] = newUuid;
            File.WriteAllText(fullPath, data.ToString(Newtonsoft.Json.Formatting.Indented));

            string directory = Path.GetDirectoryName(assetPath);
            string newAssetPath = directory.Replace('\\', '/') + "/" + newUuid + ".json";

            if (!string.Equals(assetPath, newAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                string moveError = AssetDatabase.MoveAsset(assetPath, newAssetPath);
                if (!string.IsNullOrEmpty(moveError))
                    Debug.LogWarning($"[JsonScenes] EntityAssetPostprocessor: Failed to rename {assetPath} → {newAssetPath}: {moveError}");
                else if (missingUuid)
                    Debug.Log($"[JsonScenes] Assigned UUID {newUuid} to {assetPath}");
                else
                    Debug.Log($"[JsonScenes] Duplicate detected — assigned new UUID {newUuid} (was {existingUuid})");
            }

            return false; // Renamed — fresh import will handle hot reload
        }
    }
}
