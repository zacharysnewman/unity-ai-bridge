using System;
using System.IO;
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
