using System.Collections.Generic;
using UnityEngine;

namespace UnityAIBridge
{
    /// <summary>
    /// The only serialized (non-DontSave) object in the shell .unity scene file.
    /// Owns the UUID→GameObject lookup dictionary and orchestrates the boot sequence.
    /// SceneIO (Editor assembly) populates this via the public API — never the reverse.
    /// Each additively-loaded scene has its own SceneDataManager instance.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Unity AI Bridge/Scene Data Manager")]
    public class SceneDataManager : MonoBehaviour
    {
        /// <summary>
        /// Project-relative path to the scene data directory.
        /// Derived from the scene file path — no manual configuration needed.
        /// Convention: Assets/Scenes/Level_01.unity → Assets/SceneData/Scenes/Level_01
        /// Returns null if the scene has not been saved yet.
        /// </summary>
        public string sceneDataPath
        {
            get
            {
                string scenePath = gameObject.scene.path;
                if (string.IsNullOrEmpty(scenePath))
                    return null;
                // Mirror folder structure under Assets/SceneData/
                // Assets/Scenes/Level_01.unity → Assets/SceneData/Scenes/Level_01
                string withoutExt = scenePath.Substring(0, scenePath.Length - ".unity".Length);
                if (withoutExt.StartsWith("Assets/"))
                    return "Assets/SceneData/" + withoutExt.Substring("Assets/".Length);
                return "Assets/SceneData/" + withoutExt;
            }
        }

        private readonly Dictionary<string, GameObject> _uuidToGameObject = new Dictionary<string, GameObject>();

        private static SceneDataManager _instance;

        /// <summary>
        /// Returns the SceneDataManager for the active scene.
        /// Searches the scene if the static reference is null (useful for Editor tools).
        /// </summary>
        public static SceneDataManager Instance
        {
            get
            {
                // If we don't have a reference, try to find one in the current scene
                if (_instance == null)
                {
                    _instance = Object.FindAnyObjectByType<SceneDataManager>();
                }
                return _instance;
            }
        }

        // OnEnable fires on domain reload (with [ExecuteAlways]), Play Mode entry, and scene load.
        // This ensures _instance is valid after script recompiles without needing Awake.
        private void OnEnable()
        {
            // Ensure the static instance is set when entering Play Mode
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
