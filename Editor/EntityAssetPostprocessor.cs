using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace JsonScenesForUnity.Editor
{
    /// <summary>
    /// Fires inside Unity whenever files in the project change (via Unity's asset pipeline).
    /// Handles UUID injection for human-created entity files that arrive without a valid UUID.
    ///
    /// UUID injection rule: filename must always equal the uuid field value.
    ///   - Missing/empty uuid  → generate new UUID, write it into the file, rename to [uuid].json
    ///   - uuid field ≠ filename → treat as a duplicate (Ctrl+D copy). Generate new UUID, update field, rename.
    ///
    /// AI-initiated creation uses the MCP create_entity tool instead (see MCP.md §3).
    /// This postprocessor acts as a safety net for any file arriving without a valid UUID.
    /// </summary>
    public class EntityAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                if (!IsEntityFile(assetPath)) continue;
                ProcessEntityFile(assetPath);
            }
        }

        private static bool IsEntityFile(string assetPath)
        {
            if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return false;

            // Must be inside an Entities/ directory within a SceneData folder
            return assetPath.Contains("/Entities/") || assetPath.Contains("\\Entities\\");
        }

        private static void ProcessEntityFile(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath)) return;

            JObject data;
            try
            {
                data = JObject.Parse(File.ReadAllText(fullPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[JsonScenes] EntityAssetPostprocessor: Failed to parse {assetPath}: {e.Message}");
                return;
            }

            string existingUuid = data.Value<string>("uuid");
            string fileUuid = Path.GetFileNameWithoutExtension(assetPath);

            bool missingUuid = string.IsNullOrEmpty(existingUuid);
            bool uuidMismatch = !missingUuid && !string.Equals(existingUuid, fileUuid, StringComparison.OrdinalIgnoreCase);

            if (!missingUuid && !uuidMismatch)
                return; // File is valid — uuid matches filename

            // Generate a new UUID (missing or duplicate/mismatch → new identity)
            string newUuid = Guid.NewGuid().ToString();

            data["uuid"] = newUuid;
            File.WriteAllText(fullPath, data.ToString(Newtonsoft.Json.Formatting.Indented));

            // Rename asset to [newUuid].json
            string directory = Path.GetDirectoryName(assetPath);
            string newAssetPath = directory.Replace('\\', '/') + "/" + newUuid + ".json";

            if (!string.Equals(assetPath, newAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                string moveError = AssetDatabase.MoveAsset(assetPath, newAssetPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogWarning(
                        $"[JsonScenes] EntityAssetPostprocessor: Failed to rename {assetPath} → {newAssetPath}: {moveError}");
                }
                else
                {
                    if (missingUuid)
                        Debug.Log($"[JsonScenes] Assigned UUID {newUuid} to {assetPath}");
                    else
                        Debug.Log($"[JsonScenes] Duplicate detected — assigned new UUID {newUuid} (was {existingUuid})");
                }
            }
        }
    }
}
