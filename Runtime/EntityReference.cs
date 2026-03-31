using System;
using UnityEngine;

namespace JsonScenesForUnity
{
    /// <summary>
    /// UUID-based reference to another entity in the scene.
    /// Use SceneDataManager.Instance.GetByUUID(targetUUID) to resolve at runtime.
    /// Direct GameObject/MonoBehaviour references are prohibited in synced scripts
    /// because they cannot survive JSON round-trips.
    /// </summary>
    [Serializable]
    public struct EntityReference
    {
        public string targetUUID;
    }
}
