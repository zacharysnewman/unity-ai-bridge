using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Maintains index.ndjson — a fast-lookup sidecar index of all entities and their components.
    /// Two line types per entity:
    ///   {"uuid":"...","name":"...","prefab":"...","parent":"...|null","siblingIndex":N}
    ///   {"uuid":"...","component":"FullyQualifiedTypeName"}
    /// Entities are separated by a blank line. Each line is independently parseable.
    ///
    /// Written only by C#. Never read by the sync pipeline. Excluded from FileSystemWatcher
    /// entity sync because it lives next to (not inside) the Entities/ directory and uses
    /// the .ndjson extension.
    /// </summary>
    internal static class IndexWriter
    {
        private const string IndexFileName = "index.ndjson";

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Upserts the index entry for the given entity.
        /// Replaces any existing lines for this UUID, or appends new ones.
        /// Called after WriteEntity succeeds.
        /// </summary>
        public static void UpsertEntity(GameObject go, string sceneDataPath)
        {
            if (string.IsNullOrEmpty(sceneDataPath)) return;

            var sync = go.GetComponent<EntitySync>();
            if (sync == null || string.IsNullOrEmpty(sync.uuid)) return;

            string uuid = sync.uuid;
            string name = go.name;
            string prefab = SceneIO.GetPrefabPath(go);

            var parentSync = go.transform.parent != null
                ? go.transform.parent.GetComponent<EntitySync>()
                : null;
            string parentUuid = parentSync?.uuid;

            int siblingIndex = go.transform.GetSiblingIndex();

            var componentTypes = new List<string>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp is MonoBehaviour mb)
                {
                    if (mb is EntitySync) continue;
                    componentTypes.Add(comp.GetType().FullName);
                }
                else if (BuiltInComponentSerializer.ShouldInclude(comp))
                {
                    componentTypes.Add(comp.GetType().FullName);
                }
            }

            string block = BuildBlock(uuid, name, prefab, parentUuid, siblingIndex, componentTypes);
            ReplaceBlock(sceneDataPath, uuid, block);
        }

        /// <summary>
        /// Removes all index lines for the given UUID.
        /// Called when an entity is deleted.
        /// </summary>
        public static void RemoveEntity(string uuid, string sceneDataPath)
        {
            if (string.IsNullOrEmpty(sceneDataPath) || string.IsNullOrEmpty(uuid)) return;
            ReplaceBlock(sceneDataPath, uuid, null);
        }

        /// <summary>
        /// Regenerates the entire index from the Entities directory on disk.
        /// Self-healing — produces identical output to a clean incremental run.
        /// Called on scene load (ReconcileScene).
        /// </summary>
        public static void RegenerateIndex(string sceneDataPath)
        {
            if (string.IsNullOrEmpty(sceneDataPath)) return;

            string entitiesDir = Path.Combine(sceneDataPath, "Entities");
            if (!Directory.Exists(entitiesDir)) return;

            string indexPath = GetIndexPath(sceneDataPath);
            var sb = new StringBuilder();
            bool first = true;

            foreach (string file in Directory.GetFiles(entitiesDir, "*.json"))
            {
                JObject data;
                try { data = JObject.Parse(File.ReadAllText(file)); }
                catch { continue; }

                string uuid = data.Value<string>("uuid");
                if (string.IsNullOrEmpty(uuid)) continue;

                string name = data.Value<string>("name") ?? string.Empty;
                string prefab = data.Value<string>("prefabPath") ?? string.Empty;
                string parentUuid = data.Value<string>("parentUuid");
                int siblingIndex = data.Value<int?>("siblingIndex") ?? 0;

                var componentTypes = new List<string>();
                var builtInComponents = data["builtInComponents"] as JArray;
                if (builtInComponents != null)
                {
                    foreach (JObject entry in builtInComponents)
                    {
                        string typeName = entry.Value<string>("type");
                        if (!string.IsNullOrEmpty(typeName))
                            componentTypes.Add(typeName);
                    }
                }
                var customData = data["customData"] as JArray;
                if (customData != null)
                {
                    foreach (JObject entry in customData)
                    {
                        string typeName = entry.Value<string>("type");
                        if (!string.IsNullOrEmpty(typeName))
                            componentTypes.Add(typeName);
                    }
                }

                if (!first) sb.AppendLine();
                first = false;

                sb.AppendLine(BuildInfoLine(uuid, name, prefab, parentUuid, siblingIndex));
                foreach (string type in componentTypes)
                    sb.AppendLine(BuildComponentLine(uuid, type));
            }

            File.WriteAllText(indexPath, sb.ToString(), Encoding.UTF8);
        }

        // ─── Block operations ─────────────────────────────────────────────────────

        /// <summary>
        /// Finds and replaces (or removes) the block for the given UUID in index.ndjson.
        /// If newBlock is null, the block is removed. If the UUID is not found, the new
        /// block is appended.
        /// </summary>
        private static void ReplaceBlock(string sceneDataPath, string uuid, string newBlock)
        {
            string indexPath = GetIndexPath(sceneDataPath);
            string existing = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;

            var blocks = SplitIntoBlocks(existing);
            int existingIdx = blocks.FindIndex(b => ExtractUUID(b) == uuid);

            if (existingIdx >= 0)
                blocks.RemoveAt(existingIdx);

            if (!string.IsNullOrEmpty(newBlock))
                blocks.Add(newBlock);

            var sb = new StringBuilder();
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.AppendLine(blocks[i]);
            }

            File.WriteAllText(indexPath, sb.ToString(), Encoding.UTF8);
        }

        // ─── Parsing ──────────────────────────────────────────────────────────────

        private static List<string> SplitIntoBlocks(string content)
        {
            var blocks = new List<string>();
            if (string.IsNullOrEmpty(content)) return blocks;

            var currentBlock = new StringBuilder();
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentBlock.Length > 0)
                    {
                        blocks.Add(currentBlock.ToString().TrimEnd('\r', '\n'));
                        currentBlock.Clear();
                    }
                }
                else
                {
                    if (currentBlock.Length > 0) currentBlock.AppendLine();
                    currentBlock.Append(line);
                }
            }

            if (currentBlock.Length > 0)
                blocks.Add(currentBlock.ToString().TrimEnd('\r', '\n'));

            return blocks;
        }

        private static string ExtractUUID(string block)
        {
            if (string.IsNullOrEmpty(block)) return null;

            int newlineIdx = block.IndexOf('\n');
            string firstLine = newlineIdx >= 0
                ? block.Substring(0, newlineIdx).TrimEnd('\r')
                : block;

            try { return JObject.Parse(firstLine).Value<string>("uuid"); }
            catch { return null; }
        }

        // ─── Line builders ────────────────────────────────────────────────────────

        private static string BuildBlock(string uuid, string name, string prefab, string parentUuid, int siblingIndex, List<string> componentTypes)
        {
            var sb = new StringBuilder();
            sb.Append(BuildInfoLine(uuid, name, prefab, parentUuid, siblingIndex));
            foreach (string type in componentTypes)
            {
                sb.AppendLine();
                sb.Append(BuildComponentLine(uuid, type));
            }
            return sb.ToString();
        }

        private static string BuildInfoLine(string uuid, string name, string prefab, string parentUuid, int siblingIndex)
        {
            var obj = new JObject
            {
                ["uuid"] = uuid,
                ["name"] = name ?? string.Empty,
                ["prefab"] = prefab ?? string.Empty,
                ["parent"] = string.IsNullOrEmpty(parentUuid) ? (JToken)JValue.CreateNull() : parentUuid,
                ["siblingIndex"] = siblingIndex,
            };
            return obj.ToString(Formatting.None);
        }

        private static string BuildComponentLine(string uuid, string componentType)
        {
            return new JObject
            {
                ["uuid"] = uuid,
                ["component"] = componentType,
            }.ToString(Formatting.None);
        }

        private static string GetIndexPath(string sceneDataPath)
            => Path.Combine(sceneDataPath, IndexFileName);
    }
}
