using UnityEngine;

namespace UnityAIBridge
{
    /// <summary>
    /// Attached dynamically to every spawned GameObject.
    /// Holds the entity's stable UUID and an isDirty flag used by the write pipeline.
    /// </summary>
    public class EntitySync : MonoBehaviour
    {
        [HideInInspector]
        public string uuid;

        /// <summary>
        /// Set by the write pipeline when a change is detected.
        /// Cleared after the JSON file is written to disk.
        /// </summary>
        [HideInInspector]
        public bool isDirty;
    }
}
