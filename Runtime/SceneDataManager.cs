using System.Collections.Generic;
using UnityEngine;

namespace JsonScenesForUnity
{
    /// <summary>
    /// The only serialized (non-DontSave) object in the shell .unity scene file.
    /// Owns the UUID→GameObject lookup dictionary and orchestrates the boot sequence.
    /// SceneIO (Editor assembly) populates this via the public API — never the reverse.
    /// Each additively-loaded scene has its own SceneDataManager instance.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("JSON Scenes/Scene Data Manager")]
    public class SceneDataManager : MonoBehaviour
    {
        [Tooltip("Project-relative path to the scene data directory, e.g. Assets/SceneData/Level_01")]
        public string sceneDataPath;

        private static SceneDataManager _instance;

        /// <summary>
        /// Returns the SceneDataManager for the active scene.
        /// For multi-scene setups, iterate SceneDataManager.FindObjectsOfType instead.
        /// </summary>
        public static SceneDataManager Instance => _instance;

        private readonly Dictionary<string, GameObject> _uuidToGameObject = new Dictionary<string, GameObject>();

        // OnEnable fires on domain reload (with [ExecuteAlways]), Play Mode entry, and scene load.
        // This ensures _instance is valid after script recompiles without needing Awake.
        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>
        /// Resolves a UUID to its live GameObject. Returns null if not found.
        /// </summary>
        public GameObject GetByUUID(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return null;

            _uuidToGameObject.TryGetValue(uuid, out var go);
            return go;
        }

        /// <summary>
        /// Registers a UUID→GameObject mapping. Called by SceneIO after instantiation.
        /// </summary>
        public void Register(string uuid, GameObject go)
        {
            if (string.IsNullOrEmpty(uuid) || go == null)
                return;

            _uuidToGameObject[uuid] = go;
        }

        /// <summary>
        /// Removes a UUID from the registry. Called by SceneIO before DestroyImmediate.
        /// </summary>
        public void Unregister(string uuid)
        {
            if (!string.IsNullOrEmpty(uuid))
                _uuidToGameObject.Remove(uuid);
        }

        /// <summary>
        /// Clears the entire registry. Called before a full re-bootstrap.
        /// </summary>
        public void ClearRegistry()
        {
            _uuidToGameObject.Clear();
        }

        /// <summary>
        /// Returns all currently registered UUIDs.
        /// </summary>
        public IEnumerable<string> GetAllUUIDs()
        {
            return _uuidToGameObject.Keys;
        }
    }
}
