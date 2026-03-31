# Unity Live-Sync JSON Scene Architecture — Specification

**Package:** `com.yournamespace.json-scenes-for-unity` (Unity Package Manager)
**Minimum Unity Version:** Unity 6 (6000.x) and later

---

## 1. Overview

This document defines a custom, text-based, AI-friendly scene format for Unity. The system completely bypasses Unity's native `.unity` scene serialization in favor of a **Live-Sync Disk Model**.

The file system (JSON) is the **absolute Source of Truth**. Unity serves strictly as a runtime visualizer and editor interface. Changes in the Unity Editor are automatically written to disk via a debounced file stream, and external changes to JSON files (e.g., by an LLM or text editor) are hot-reloaded into the Unity hierarchy instantly.

---

## 2. Core Principles

| Principle | Description |
|---|---|
| **Shell Scene** | The native `.unity` file contains exactly one persistent object (`SceneDataManager`). All other objects are ephemeral, flagged with `HideFlags.DontSave`. |
| **Decentralized Entities** | The scene is a directory of individual JSON files — one per entity/prefab instance. AI can grep and edit specific chunks efficiently. |
| **Live Database Workflow** | There is no traditional "Save Scene" action. Memory and disk state are kept in constant parity. |
| **Human/AI Readable Types** | Complex Unity types (`Quaternion`, `Color`) are serialized into flat, readable formats (Euler degrees, RGB arrays) to minimize token count and cognitive load. |

---

## 3. Data Architecture

### 3.1 Directory Structure

Each logical scene maps to a directory containing a manifest and an entities folder.

```
Assets/SceneData/Level_01/
├── manifest.json            # Global settings (Skybox, Lighting, build dependencies)
└── Entities/                # One file per object
    ├── 5f3a1b2c...json      # UUID filename matching the entity
    └── 9c8d2e1a...json
```

### 3.2 Manifest Schema

```json
{
  "sceneName": "Level_01",
  "skybox": "Assets/Materials/Skyboxes/Overcast.mat",
  "ambientLight": [0.2, 0.2, 0.25],
  "buildDependencies": [
    "Assets/Prefabs/Environment/HeavyDoor.prefab"
  ]
}
```

> The full manifest schema is not yet specified. See [Clarifying Questions](#clarifying-questions) §1.

### 3.3 Entity JSON Schema

Uses **Token-Oriented Object Notation (TOON)** principles to minimize verbosity.

```json
{
  "uuid": "5f3a1b2c-3d4e-5f6a-7b8c-9d0e1f2a3b4c",
  "name": "Heavy_Door_01",
  "prefabPath": "Assets/Prefabs/Environment/HeavyDoor.prefab",
  "transform": {
    "pos": [10.5, 0.0, -2.2],
    "rot": [0.0, 90.0, 0.0],
    "scl": [1.0, 1.0, 1.0]
  },
  "customData": {
    "DoorScript": {
      "isLocked": true,
      "triggerTargetUuid": "8a7b6c5d-..."
    }
  }
}
```

**Field Reference:**

| Field | Type | Description |
|---|---|---|
| `uuid` | string (UUID v4) | Stable identity. Used as the filename and for cross-entity references. |
| `name` | string | Display name in the Unity hierarchy. |
| `prefabPath` | string | Project-relative path to the source prefab. |
| `transform.pos` | `[x, y, z]` | World-space position in meters. |
| `transform.rot` | `[x, y, z]` | Euler angles in degrees (XYZ order). |
| `transform.scl` | `[x, y, z]` | Local scale. |
| `customData` | object | Map of component class name → serialized `[AiSerializable]` fields. |

> Non-prefab (primitive) objects and parent/child hierarchies are not yet specified. See [Clarifying Questions](#clarifying-questions) §2 and §3.

---

## 4. System Components

### 4.1 `SceneDataManager` (MonoBehaviour)

- The **only serialized object** in the shell `.unity` file.
- Holds the path to the `Assets/SceneData/...` scene directory.
- Orchestrates the initial boot/load sequence.
- Exposes `Instance.GetByUUID(string uuid)` for runtime cross-entity reference resolution.

### 4.2 `EntitySync` (MonoBehaviour)

- Attached dynamically to every spawned GameObject at runtime.
- Holds the object's `uuid` string.
- Maintains an `isDirty` flag used by the write pipeline to gate disk writes.

### 4.3 `LiveSyncController` (Editor Script)

- Manages the `FileSystemWatcher` on the `Entities/` directory.
- Drives the `EditorApplication.update` loop.
- Implements debounce logic (~300 ms) to prevent disk thrashing on rapid editor changes.
- Routes detected file events to `SceneIO` on the main Unity thread via `EditorApplication.delayCall`.

### 4.4 `SceneIO` (Static Class)

- The serialization engine.
- Reads/writes JSON for entity files and the manifest.
- Instantiates prefabs via `PrefabUtility.InstantiatePrefab()`.
- Maps UUIDs to live `GameObject` instances.
- Filters component fields via reflection, honoring `[AiSerializable]` allow-listing.

---

## 5. Workflows & CRUD Operations

### 5.1 Bootstrapping (Initialization)

Triggered by `[InitializeOnLoad]` or when the shell scene is opened.

1. Read `manifest.json` and apply global scene settings.
2. Iterate through all files in `Entities/`.
3. Instantiate each referenced prefab using `PrefabUtility.InstantiatePrefab()`.
4. Apply `transform` and `customData` from the JSON.
5. Attach `EntitySync`, assign the `uuid`.
6. Apply `gameObject.hideFlags = HideFlags.DontSave`.

### 5.2 Unity Editor → JSON (Write Pipeline)

Managed by `LiveSyncController` intercepting editor change events.

| Operation | Trigger | Mechanism |
|---|---|---|
| **Update** | `Transform.hasChanged` or Inspector edit | Debounce timer (~300 ms). Once the user stops editing, `SceneIO` writes the updated state to `[UUID].json`. |
| **Create** | `ObjectChangeEvents` (prefab dropped into scene) | Intercept creation. Generate new UUID, attach `EntitySync`, apply `DontSave` flag, write `[NEW_UUID].json`. |
| **Delete** | `ObjectChangeEvents` (object destroyed) | Intercept deletion. Identify UUID via `EntitySync`, call `File.Delete([UUID].json)`. |
| **Duplicate** | Ctrl+D (clone created) | Intercept clone. Strip old UUID, generate new UUID, write new JSON immediately. |

### 5.3 JSON → Unity Editor (Hot-Reload Pipeline)

Handles external edits from AI tools, text editors, or Git operations.

1. **Detect**: `FileSystemWatcher` observes a `Created`, `Changed`, or `Deleted` event in `Entities/`.
2. **Queue**: Event is marshalled to the main Unity thread via `EditorApplication.delayCall`.
3. **Apply**:
   - `Changed`: Locate the active `GameObject` by UUID; apply new JSON state (position, rotation, custom data).
   - `Created`: Instantiate the new prefab, attach `EntitySync`, register UUID.
   - `Deleted`: Call `DestroyImmediate()` on the corresponding `GameObject`.

---

## 6. Constraints & Safeguards

### 6.1 Component Serialization Allow-List

To prevent JSON bloat and preserve AI readability:

- Only fields explicitly marked `[AiSerializable]` are written to the `customData` block.
- Unity-native components (`Collider`, `Rigidbody`, `MeshRenderer`, etc.) derive their configuration from the source `.prefab`. Scene-level overrides are minimized.

**Example:**

```csharp
public class DoorScript : MonoBehaviour
{
    [AiSerializable] public bool isLocked = false;
    [AiSerializable] public EntityReference triggerTarget;

    // Not serialized — internal state only
    private bool _isAnimating;
}
```

### 6.2 Reference Handling

Direct `GameObject` or `MonoBehaviour` references are prohibited in synced scripts because they cannot survive JSON round-trips.

- Use the `EntityReference` struct (wraps a `string targetUUID`) wherever cross-entity references are needed.
- Resolve at runtime with `SceneDataManager.Instance.GetByUUID(targetUUID)`.

```csharp
[Serializable]
public struct EntityReference
{
    public string targetUUID;
}
```

### 6.3 Version Control & Reversions

Because native Unity saving is bypassed:

| Scenario | Mechanism |
|---|---|
| **Undo/Redo** | Unity's `Ctrl+Z` functions normally. Reverting triggers the Update write workflow, rewriting the prior state to JSON. |
| **Session Rollback** | `git checkout -- Assets/SceneData/Level_01/` reverts the text files. `FileSystemWatcher` picks up the changes and snaps the editor to the restored state instantly. |
| **Diff & Review** | Each entity is an isolated file. PRs show per-object diffs rather than a monolithic binary scene blob. |

---

## 7. Package Structure

This system is distributed as a **Unity Package Manager (UPM) package**, importable into any Unity 6+ project.

```
com.yournamespace.json-scenes-for-unity/
├── package.json                        # UPM manifest
├── Runtime/
│   ├── SceneDataManager.cs
│   ├── EntitySync.cs
│   └── EntityReference.cs
├── Editor/
│   ├── LiveSyncController.cs
│   └── SceneIO.cs
├── Samples~/
│   └── DemoScene/                      # Optional importable demo
└── SPEC.md
```

- **Runtime assembly** (`Runtime/`): `SceneDataManager`, `EntitySync`, and `EntityReference` — safe to include in builds (though active only in the Editor).
- **Editor assembly** (`Editor/`): `LiveSyncController` and `SceneIO` — stripped from builds automatically via `Editor/` folder convention.
- The package defines no hard dependencies beyond the Unity Editor itself (no third-party JSON library required; `UnityEngine.JsonUtility` or `Newtonsoft.Json` TBD — see [Clarifying Questions](#clarifying-questions) §18).

> Full `package.json` schema (name, display name, version, author, Unity requirement) is not yet defined. See [Clarifying Questions](#clarifying-questions) §19.

---

## 8. Non-Goals

The following are explicitly out of scope for this architecture:

- Runtime (built player) scene loading — this system targets the **Editor workflow** only.
- Replacing Unity's Addressables or AssetBundle systems.
- Serializing Unity-native component overrides (Colliders, Rigidbodies, etc.) beyond what the prefab defines.

---

## Clarifying Questions

The following questions identify gaps or ambiguities in the current specification that should be resolved before implementation.

### Schema & Data Model

1. **Manifest schema**: What fields does `manifest.json` define beyond Skybox, ambient lighting, and build dependencies? Does it store scene-level fog settings, physics layer configuration, or navigation mesh references?

2. **Primitive / non-prefab objects**: Can an entity be a primitive (e.g., a Cube, Sphere) rather than a prefab? If so, how is `prefabPath` handled — is it omitted, or are there reserved values like `"primitive://Cube"`?

3. **Hierarchy / parenting**: Does the entity schema support parent-child relationships? If so, is this expressed as a `parentUuid` field on the child, or as a `children: []` array on the parent?

4. **Transform space**: Are `pos` and `rot` always world-space, or local-space when a parent is present?

5. **UUID generation**: Is UUID v4 (random) the required standard, or is any unique string acceptable? Who is responsible for generation when an AI creates a new entity file — the AI or the system?

6. **`customData` key convention**: Is the key the C# class name (`DoorScript`), the full type name with namespace, or a user-defined alias?

### System Behavior

7. **Conflict resolution**: If Unity writes a JSON update and an external tool writes a different update to the same file within the debounce window, which wins? Is there a lock or merge strategy?

8. **Boot performance**: For scenes with hundreds or thousands of entities, bootstrapping will instantiate all prefabs synchronously. Is progressive/async loading planned, or is there a scene-size limit?

9. **Play Mode behavior**: What happens when the user enters Play Mode? Are the `DontSave` objects preserved, re-instantiated, or destroyed? Does the JSON state update during Play Mode?

10. **Prefab modifications**: If a prefab asset is changed on disk (not the entity JSON), does the system re-apply the new prefab to existing scene entities? How are prefab overrides tracked?

11. **Multi-scene / additive loading**: Does the system support multiple active scene directories loaded simultaneously (Unity's additive scene workflow)?

### Editor Integration

12. **Inspector editing**: When a non-`[AiSerializable]` field is edited in the Inspector, is that change silently ignored (not written to JSON) or flagged as a warning to the developer?

13. **Undo granularity**: Unity's Undo system operates per-frame. If a user performs a rapid multi-step action (e.g., drag + rotate), does each intermediate state write to disk, or only the final debounced state?

14. **Selection and gizmos**: Since entities use `HideFlags.DontSave`, do standard editor tools (scene gizmos, multi-select, alignment tools) work without modification?

### AI & Tooling

15. **Schema versioning**: Is there a `"schemaVersion"` field planned for `manifest.json` or entity files to handle forward/backward compatibility as the spec evolves?

16. **Validation**: Is there a planned JSON schema file (JSON Schema Draft 7 / OpenAPI) or CLI validator that AI tools can use to verify generated entity files before writing them to disk?

17. **Bulk operations**: For AI workflows that create or modify dozens of entities at once, is there a batch API planned, or does the AI write individual files and rely on the `FileSystemWatcher` to process them sequentially?

### Package & Distribution

18. **JSON library**: Should the package use `UnityEngine.JsonUtility` (built-in, no dependency, limited type support) or `Newtonsoft.Json` / `System.Text.Json` (richer type handling, potential dependency)? This affects how nested types, arrays, and nullables are handled.

19. **Package identity**: What is the intended UPM package name (`com.<org>.<name>`), display name, initial version, and author/organization? Is the package intended for the Unity Asset Store, OpenUPM, or private distribution only?

20. **Minimum API surface**: Should the package expose a public C# API for runtime tools or CI scripts to read/write entity JSON outside of the Unity Editor (e.g., a headless build pipeline populating a scene from a database)?
