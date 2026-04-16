# Unity Live-Sync JSON Scene Architecture — Specification

**Package:** `com.zacharysnewman.unity-ai-bridge` (Unity Package Manager)
**Minimum Unity Version:** Unity 6 (6000.x) and later
**JSON Library:** Newtonsoft.Json

---

## 1. Overview

This document defines a custom, text-based, AI-friendly scene format for Unity. The system completely bypasses Unity's native `.unity` scene serialization in favor of a **Live-Sync Disk Model**.

The file system (JSON) is the **absolute Source of Truth**. Unity serves strictly as a runtime visualizer and editor interface. Changes in the Unity Editor are automatically written to disk via a debounced file stream, and external changes to JSON files (e.g., by an LLM or text editor) are hot-reloaded into the Unity hierarchy instantly.

---

## 2. Core Principles

| Principle | Description |
|---|---|
| **Shell Scene** | The native `.unity` file contains exactly one persistent object (`SceneDataManager`). All other objects are ephemeral, flagged with `HideFlags.DontSave`. `DontSave` is a composite of `DontSaveInEditor`, `DontSaveInBuild`, and `DontUnloadUnusedAsset` — it affects persistence only, not editor interactivity. Objects with this flag are fully selectable, moveable, and editable by standard and third-party editor tools. |
| **Decentralized Entities** | The scene is a directory of individual JSON files — one per entity/prefab instance. AI can grep and edit specific chunks efficiently. |
| **Live Database Workflow** | There is no traditional "Save Scene" action. Memory and disk state are kept in constant parity. |
| **Human/AI Readable Types** | Complex Unity types (`Quaternion`, `Color`) are serialized into flat, readable formats (Euler degrees, RGB arrays) to minimize token count and cognitive load. |
| **External Writes Win** | In any conflict between an in-editor change and a simultaneous external file write, the external file is treated as authoritative. |

---

## 3. Data Architecture

### 3.1 Directory Structure

Each logical scene maps to a directory containing a manifest, an entities folder, and a commands folder.

```
Assets/SceneData/Level_01/
├── manifest.json            # Scene metadata (name, schema version)
├── Commands/                # Short-lived command files (auto-deleted after execution)
└── Entities/                # One file per object
    ├── 5f3a1b2c...json      # UUID v4 filename matching the entity's uuid field
    └── 9c8d2e1a...json
```

The `Commands/` directory is watched by the same `FileSystemWatcher` as `Entities/`. Command files are read and executed by the package, then deleted immediately.

### 3.2 Manifest Schema

The manifest stores scene-level metadata only. Skybox, lighting, fog, and other environmental settings are **not** included here — they live in the shell `.unity` file alongside `SceneDataManager`, managed by Unity's normal `RenderSettings` and `LightmapSettings` serialization. Users configure these via Unity's Environment Lighting window as they would in any normal scene.

```json
{
  "schemaVersion": 1,
  "sceneName": "Level_01"
}
```

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | integer | Incremented when breaking schema changes are introduced. Used to gate migration logic. |
| `sceneName` | string | Human-readable scene name. Does not need to match the directory name. |

> Additional manifest fields may be added as the system evolves. All readers must ignore unknown fields.

### 3.3 Entity JSON Schema

Uses **Token-Oriented Object Notation (TOON)** principles to minimize verbosity.

```json
{
  "uuid": "5f3a1b2c-3d4e-5f6a-7b8c-9d0e1f2a3b4c",
  "name": "Heavy_Door_01",
  "prefabPath": "Assets/Prefabs/Environment/HeavyDoor.prefab",
  "parentUuid": "2a1b0c9d-...",
  "transform": {
    "pos": [10.5, 0.0, -2.2],
    "rot": [0.0, 90.0, 0.0],
    "scl": [1.0, 1.0, 1.0]
  },
  "customData": [
    {
      "type": "MyGame.Environment.DoorScript",
      "isLocked": true,
      "triggerTarget": { "targetUUID": "8a7b6c5d-..." }
    }
  ]
}
```

**Field Reference:**

| Field | Type | Required | Description |
|---|---|---|---|
| `uuid` | string (UUID v4) | Yes | Stable identity. Used as the filename and for cross-entity references. AI tools generate a fresh UUID v4 and write the file directly. For human-created entities, `EntityAssetPostprocessor` (§4.5) assigns it. Never fabricate a UUID string. |
| `name` | string | Yes | Display name in the Unity hierarchy. |
| `prefabPath` | string | Yes | Project-relative path to the source prefab, or a primitive identifier (see §3.4). |
| `parentUuid` | string (UUID v4) | No | UUID of this entity's parent. Omitted or `null` for root-level objects. |
| `siblingIndex` | integer | No | Zero-based index among siblings under the same parent (or scene root). Preserved on load via `SetSiblingIndex`. Affects `transform.GetChild(index)`, UI layout groups, canvas render order, and any code that iterates children by index. |
| `transform.pos` | `[x, y, z]` | Yes | **Local-space** position in meters when `parentUuid` is set; world-space when at root. |
| `transform.rot` | `[x, y, z]` | Yes | **Local-space** Euler angles in degrees when `parentUuid` is set; world-space when at root. Values match what Unity's Inspector displays. Round-trip via `Quaternion.Euler(x,y,z)` ↔ `quaternion.eulerAngles`. Unity applies Euler rotations internally in ZXY order, but the stored `[x,y,z]` values are the same Inspector-visible numbers — no manual reordering needed. |
| `transform.scl` | `[x, y, z]` | Yes | Local scale (always local, consistent with Unity). |
| `customData` | array | No | Ordered list of serialized component entries. Each entry has a `type` field (fully-qualified class name) plus inline serialized fields. See §6.1. |

#### Hierarchy & Parenting

Parent-child relationships are expressed as a **`parentUuid` field on the child**. This design was chosen over a `children` array on the parent for the following reasons:

- Each entity file is fully self-contained. Reading a single file tells you everything about that entity.
- Creating or reparenting an object requires editing only the child file, not the parent.
- Matches Unity's own data model (`transform.parent`).
- AI tools can create or move entities without needing to read the parent's file.

The load order during bootstrapping resolves the full hierarchy by processing all entities and then applying parent assignments after all GameObjects are instantiated.

### 3.4 Primitive Objects

Non-prefab primitive objects use a reserved `prefabPath` convention instead of a real asset path:

| `prefabPath` value | Unity Primitive |
|---|---|
| `"primitive/Sphere"` | `PrimitiveType.Sphere` |
| `"primitive/Cube"` | `PrimitiveType.Cube` |
| `"primitive/Cylinder"` | `PrimitiveType.Cylinder` |
| `"primitive/Capsule"` | `PrimitiveType.Capsule` |
| `"primitive/Plane"` | `PrimitiveType.Plane` |
| `"primitive/Quad"` | `PrimitiveType.Quad` |

The prefix `primitive/` is case-sensitive. These are instantiated via `GameObject.CreatePrimitive()` instead of `PrefabUtility.InstantiatePrefab()`.

---

## 4. System Components

### 4.1 `SceneDataManager` (MonoBehaviour)

- The **only serialized object** in the shell `.unity` file.
- The path to the `Assets/SceneData/...` scene directory is **derived at runtime** from `gameObject.scene.path` — it is not a stored serialized field. Convention: `Assets/Scenes/Level_01.unity` → `Assets/SceneData/Scenes/Level_01`. Folder structure is mirrored to prevent name collisions between same-named scenes in different directories. The derived path is exposed as a read-only display field in the Inspector for debugging visibility.
- Guard: if `gameObject.scene.path` is empty (unsaved scene), the path property returns `null` and all sync operations bail with a warning.
- Orchestrates the initial async boot sequence.
- **Owns the UUID→GameObject lookup dictionary.** Exposes `Instance.GetByUUID(string uuid)` for runtime cross-entity reference resolution.
- `SceneIO` (Editor assembly) populates the dictionary by calling into `SceneDataManager`'s public API. `SceneDataManager` (Runtime assembly) never references `SceneIO` directly, preserving Runtime/Editor assembly isolation.
- Each additively-loaded scene has its own `SceneDataManager`.

### 4.2 `EntitySync` (MonoBehaviour)

- Attached dynamically to every spawned GameObject at runtime.
- Holds the object's `uuid` string.
- Maintains an `isDirty` flag used by the write pipeline to gate disk writes.

### 4.3 `LiveSyncController` (Editor Script)

- Manages the `FileSystemWatcher` on the `Entities/` directory.
- Drives the `EditorApplication.update` loop.
- Implements debounce logic (300 ms, non-configurable) to prevent disk thrashing on rapid editor changes.
- Routes detected file events to `SceneIO` on the main Unity thread via `EditorApplication.delayCall`.
- Pauses write detection during Play Mode to prevent spurious updates.

### 4.4 `SceneIO` (Static Class)

- The serialization engine. Uses **Newtonsoft.Json** for all read/write operations.
- Reads/writes JSON for entity files and the manifest.
- Instantiates prefabs via `PrefabUtility.InstantiatePrefab()` or `GameObject.CreatePrimitive()`.
- Populates the UUID→GameObject lookup in `SceneDataManager` via its public API (never the reverse).
- Serializes all `MonoBehaviour` components on custom scripts (see §6.1).
- Before writing to disk, compares serialized output against the existing file; skips the write if content is identical to prevent spurious disk activity.

### 4.5 `SceneAssetModificationProcessor` (Editor Script)

Implements `AssetModificationProcessor` to keep the JSON data directory in sync when the shell `.unity` file is renamed, moved, or deleted via the Unity Project window.

**Path convention (shared with §4.1):**

```
ScenePathToDataDir(scenePath):
  strip "Assets/" prefix and ".unity" extension → relative stem
  return "Assets/SceneData/" + relative stem

Examples:
  Assets/Scenes/Level_01.unity       → Assets/SceneData/Scenes/Level_01
  Assets/SubFolder/Boss.unity        → Assets/SceneData/SubFolder/Boss
```

Both `SceneDataManager` (§4.1) and this processor derive the data directory from the same convention, so they always agree without any stored state.

**`OnWillMoveAsset(sourcePath, destinationPath)`**

Fires before any Project-window rename or move. When the asset is a `.unity` file:

1. Compute `oldDataDir` from `sourcePath` and `newDataDir` from `destinationPath`.
2. If they differ and `oldDataDir` exists in the asset database, ensure the parent folder of `newDataDir` exists (create if missing via `AssetDatabase.CreateFolder`).
3. Call `AssetDatabase.MoveAsset(oldDataDir, newDataDir)` to atomically rename the data directory within the asset pipeline.
4. Return `AssetMoveResult.DidNotMove` — Unity still handles the `.unity` file itself.

If `AssetDatabase.MoveAsset` returns an error string, log a warning; sync will be degraded but not broken (the computed path will point to the new location; the data directory still exists at the old location and can be moved manually).

**`OnWillDeleteAsset(assetPath, options)`**

Fires before Project-window deletion. When the asset is a `.unity` file:

1. Compute `dataDir` from `assetPath`.
2. If `dataDir` exists, open a dialog: `"Delete associated JSON data at <dataDir>? This cannot be undone."` with **Delete** / **Keep** options.
3. On **Delete**: call `AssetDatabase.DeleteAsset(dataDir)`.
4. Return `AssetDeleteResult.DidNotDelete` in both cases — Unity always handles the `.unity` file itself regardless of the user's choice.

**Caveats:**

- `AssetDatabase.MoveAsset` is called from within `OnWillMoveAsset`. This is a side-effecting asset pipeline operation inside a modification callback. It does not recurse (the data directory is a folder, not a `.unity` file) but is not explicitly documented as safe by Unity. If this proves problematic, the fallback is `EditorApplication.delayCall` — slightly racy (there is a one-frame window where the path is stale) but functionally correct.
- Only Project-window operations are intercepted. Moving files via the OS file system (Finder, `mv`) bypasses `AssetModificationProcessor` entirely; those cases require a manual `AssetDatabase.Refresh` and the user to move the data directory themselves.

---

### 4.6 `EntityAssetPostprocessor` (Editor Script)

Implements `AssetPostprocessor.OnPostprocessAllAssets`. Fires inside Unity whenever files in the project change — runs through Unity's own asset pipeline, which is more reliable than `FileSystemWatcher` for UUID assignment.

Handles **human-initiated** entity creation (drag-drop, Ctrl+D):

**UUID injection rule:** filename must always equal the `uuid` field value. Any mismatch or absence triggers assignment:

- **Missing/empty `uuid`:** generate `System.Guid.NewGuid()`, write it into the file, rename to `[uuid].json` via `AssetDatabase.MoveAsset`.
- **`uuid` field ≠ filename:** treat as a duplicate (Ctrl+D copy). Generate a new UUID, update the field, rename.

AI tools generate a fresh UUID v4, write the complete file directly, and use that UUID in any subsequent cross-referencing files. The postprocessor acts as a safety net for any file that arrives without a valid UUID.

---

## 5. Workflows & CRUD Operations

### 5.1 Bootstrapping (Initialization)

Triggered by `[InitializeOnLoad]` or when the shell scene is opened. Loading is **asynchronous** and displays a native Unity progress panel to remain non-blocking for large scenes.

1. Read and validate `manifest.json`. If `schemaVersion` does not match the expected version, abort loading and surface an error — no automatic migration is attempted.
2. Collect all files in `Entities/` into a load queue.
3. **Async loop — pass 1 (instantiate)** — for each entity file:
   a. Instantiate the prefab (`PrefabUtility.InstantiatePrefab`) or primitive (`GameObject.CreatePrimitive`).
   b. Attach `EntitySync`, assign the `uuid`.
   c. Apply `gameObject.hideFlags = HideFlags.DontSave`.
   d. Register in the UUID → GameObject lookup table.
4. **Pass 2 (wire hierarchy)** — resolve all `parentUuid` references by calling `transform.SetParent()`. Must complete before transforms are applied, because `pos` and `rot` are local-space and meaningless until the parent is established. Mirrors Unity's own load order (`m_Father` is wired before positions are interpreted).
5. **Pass 3 (apply data)** — for each entity, apply `transform` (pos, rot, scl) and `customData` fields via reflection.
6. Close the progress panel.

### 5.2 Unity Editor → JSON (Write Pipeline)

Managed by `LiveSyncController` intercepting editor change events. All writes use the debounce final-state model: only the state present when the ~300 ms timer fires is written.

| Operation | Trigger | Mechanism |
|---|---|---|
| **Update** | `Transform.hasChanged` or any Inspector field change | Debounce timer (300 ms). Final state at timer expiry is written to `[UUID].json`. Intermediate states are not written. |
| **Create (human)** | `ObjectChangeEvents` (prefab dropped into scene) | Intercept creation. Attach `EntitySync`, apply `DontSave` flag. `EntityAssetPostprocessor` (§4.5) assigns UUID and writes file. |
| **Create (AI)** | Direct file write | AI generates a fresh UUID v4, writes the complete entity JSON file, then uses that UUID in any subsequent cross-referencing files. |
| **Delete** | `ObjectChangeEvents` (object destroyed) | Intercept deletion. Identify UUID via `EntitySync`, call `File.Delete([UUID].json)`. |
| **Duplicate** | Ctrl+D (clone created) | `EntityAssetPostprocessor` detects filename/UUID mismatch on the cloned file and assigns a fresh UUID automatically. |

### 5.3 JSON → Unity Editor (Hot-Reload Pipeline)

Handles external edits from AI tools, text editors, or Git operations.

1. **Detect**: `FileSystemWatcher` observes a `Created`, `Changed`, or `Deleted` event in `Entities/`.
2. **Queue**: Event is marshalled to the main Unity thread via `EditorApplication.delayCall`.
3. **Apply** (external file always wins over any in-flight editor state):
   - `Changed`: Locate the active `GameObject` by UUID; apply new JSON state (transform, customData).
   - `Created`: Read the UUID from the new file. If a live object with that UUID already exists in the scene, treat as an **update** (same as `Changed`). If not, instantiate as a new entity. This handles the common case where external tools (VS Code, AI tools) write files atomically via temp-file-rename, which the OS reports as `Deleted` + `Created` rather than `Changed`.
   - `Deleted`: Call `DestroyImmediate()` on the corresponding `GameObject`.

### 5.4 Play Mode

On **Enter Play Mode**:
- All `DontSave` entities are destroyed from the hierarchy.
- Unity enters Play Mode in the shell scene with no managed objects.

On **Exit Play Mode**:
- The bootstrap sequence (§5.1) runs again, reinstantiating all entities from disk.
- This provides the most authentic Play Mode experience, identical to what a freshly-opened scene would produce.

The JSON files on disk are **never written to during Play Mode**. The `LiveSyncController` suspends the write pipeline for the duration.

> **Known incompatibility:** If *Project Settings → Editor → Enter Play Mode Settings → Disable Domain Reload* is enabled, Unity does not perform a domain reload on Play Mode entry. `DontSave` objects are **not** automatically destroyed, and the re-bootstrap on exit may produce duplicates. This system requires domain reload to be **enabled** (Unity's default). Projects that have disabled it for faster iteration are not supported.

### 5.5 Scene Lifecycle (Rename & Delete)

Managed by `SceneAssetModificationProcessor` (§4.5). These operations originate in the Unity Project window.

**Rename / Move:**

```
User renames Assets/Scenes/Level_01.unity → Level_01_New.unity
  ↓ OnWillMoveAsset fires
  ↓ oldDataDir = Assets/SceneData/Scenes/Level_01
  ↓ newDataDir = Assets/SceneData/Scenes/Level_01_New
  ↓ AssetDatabase.MoveAsset(oldDataDir, newDataDir)
  ↓ return DidNotMove — Unity moves the .unity file
```

After the rename, `SceneDataManager.sceneDataPath` (computed from `gameObject.scene.path`) automatically resolves to the new path. No manual update is needed.

**Delete:**

```
User deletes Assets/Scenes/Level_01.unity
  ↓ OnWillDeleteAsset fires
  ↓ dataDir = Assets/SceneData/Scenes/Level_01
  ↓ Dialog: "Delete associated JSON data at Assets/SceneData/Scenes/Level_01?"
      ├─ Delete → AssetDatabase.DeleteAsset(dataDir)
      └─ Keep   → data directory remains on disk (orphaned)
  ↓ return DidNotDelete — Unity deletes the .unity file
```

---

### 5.6 Prefab Propagation

Because entities are instantiated via `PrefabUtility.InstantiatePrefab()`, Unity maintains a live prefab connection for each entity in memory — identical to normal scene behavior. When a `.prefab` asset is modified, Unity automatically propagates changes to all connected instances in the hierarchy.

Prefab propagation fires Unity change events that `LiveSyncController` will intercept and attempt to write to disk. The diff guard in `SceneIO` (§4.4) handles this: if the serialized output is identical to what is already on disk, the write is skipped. No spurious file changes are produced.

---

## 6. Constraints & Safeguards

### 6.1 Component Serialization

The system serializes **all fields that Unity would normally serialize** on custom `MonoBehaviour` components: public fields and private fields marked `[SerializeField]`. No extra attribute is required.

**Which components are serialized:** only components where `component is MonoBehaviour` is true. This mirrors Unity's own class ID system — all custom scripts are MonoBehaviour (Unity class ID 114); built-in types (BoxCollider = 65, Rigidbody = 54, MeshRenderer = 23, etc.) are distinct classes and are excluded. `SceneIO` enumerates serializable components via `gameObject.GetComponents<MonoBehaviour>()`. Built-in components derive their state entirely from the source prefab and are never written to `customData`.

`customData` is an **ordered array** of component entries. Each entry contains a `type` field followed by the component's serialized fields inline.

The `type` value is the fully-qualified C# class name — the value returned by `component.GetType().FullName` (e.g., `"MyGame.Environment.DoorScript"`). This is Unity's own native identifier for MonoBehaviour types; there is no shorter stable built-in alternative.

```json
"customData": [
  {
    "type": "MyGame.Environment.DoorScript",
    "isLocked": true,
    "triggerTarget": { "targetUUID": "8a7b6c5d-..." }
  },
  {
    "type": "MyGame.Environment.DoorScript",
    "isLocked": false,
    "triggerTarget": null
  }
]
```

The **fully-qualified class name** is required (e.g., `"MyGame.Environment.DoorScript"`, not `"DoorScript"`) to unambiguously identify components across namespaces.

**Multiple components of the same type** are supported. The Nth entry for a given `type` maps to the Nth component of that type on the GameObject, matched by index via `GetComponents<T>()` on load.

> **Known limitation:** Unity allows reordering components via the Inspector's drag handles. If two components of the same type on a single object are reordered, the `customData` indices will map to the wrong instances on the next load. This is an accepted trade-off for the rare case of same-type multi-components.

### 6.2 Reference Handling

Direct `GameObject` or `MonoBehaviour` references are prohibited in synced scripts because they cannot survive JSON round-trips.

- Use the `EntityReference` struct (wraps a `string targetUUID`) wherever cross-entity references are needed.
- Resolve at runtime with `SceneDataManager.Instance.GetByUUID(targetUUID)`.

> **Known limitation (multi-scene):** `SceneDataManager.Instance` is a static singleton — it returns the manager that was most recently enabled. In an additively-loaded multi-scene setup, cross-scene `EntityReference` resolution via `Instance` will only search the last-enabled scene's registry. To resolve a UUID from a specific scene, hold a direct reference to that scene's `SceneDataManager` and call `GetByUUID` on it directly. UUIDs are only required to be unique within a scene, so two additively-loaded scenes may share UUIDs without registry collision.
>
> Note: JSON→Unity hot reload (file writes) is not affected by this limitation — `EntityAssetPostprocessor` routes each entity file to the correct scene's manager via path-prefix matching, never via `Instance`.

```csharp
[Serializable]
public struct EntityReference
{
    public string targetUUID;
}
```

### 6.3 UUID Generation

UUID v4 is used only where the system itself needs a stable identity — specifically, entity filenames and cross-entity `EntityReference` values. Everywhere Unity already provides a native identifier (asset GUIDs, prefab paths, component order), those are used instead; no new UUID systems are introduced beyond what is necessary.

Two generation paths exist depending on who initiates the creation:

- **AI-initiated:** generate a fresh UUID v4, write the complete entity file, then use that UUID in any subsequent files that reference it via `EntityReference`.
- **Human-initiated** (drag-drop, Ctrl+D): `EntityAssetPostprocessor` (§4.5) calls `System.Guid.NewGuid()` and assigns the UUID automatically inside Unity.

AI assistants must never fabricate or guess UUID strings.

### 6.4 Version Control & Reversions

Because native Unity saving is bypassed:

| Scenario | Mechanism |
|---|---|
| **Undo/Redo** | Unity's `Ctrl+Z` functions normally. Expected to trigger the Update write workflow via `ObjectChangeEvents` / `Transform.hasChanged`, writing the restored state to JSON via the debounce pipeline. *Unverified — needs confirmation during initial implementation that Undo operations fire the same change events as direct edits.* |
| **Session Rollback** | `git checkout -- Assets/SceneData/Level_01/` reverts the text files. `FileSystemWatcher` picks up the changes and snaps the editor to the restored state instantly. |
| **Diff & Review** | Each entity is an isolated file. PRs show per-object diffs rather than a monolithic binary scene blob. |

### 6.5 Schema Versioning

The `manifest.json` `schemaVersion` integer is incremented for any breaking change to the entity or manifest format. On bootstrap, `SceneIO` reads this value and compares it against the package's expected version. If they do not match, loading is aborted and an error is surfaced to the developer. Entity files do not carry individual version fields; the manifest version governs the entire scene directory.

---

## 7. Package Structure

This system is distributed as a **Unity Package Manager (UPM) package**, importable into any Unity 6+ project.

```
com.zacharysnewman.unity-ai-bridge/
├── package.json                        # UPM manifest
├── Runtime/
│   ├── SceneDataManager.cs
│   ├── EntitySync.cs
│   └── EntityReference.cs
├── Editor/
│   ├── LiveSyncController.cs
│   ├── SceneIO.cs
│   └── EntityAssetPostprocessor.cs
├── Samples~/
│   └── DemoScene/                      # Optional importable demo
└── SPEC.md
```

- **Runtime assembly** (`Runtime/`): `SceneDataManager`, `EntitySync`, and `EntityReference` — safe to include in builds.
- **Editor assembly** (`Editor/`): `LiveSyncController` and `SceneIO` — stripped from player builds automatically.
- **Dependency**: Newtonsoft.Json (via Unity's `com.unity.nuget.newtonsoft-json` package).
- **Distribution**: Via **Git URL** in Unity Package Manager. Users add the package by pointing UPM at the GitHub repository URL. No Asset Store or OpenUPM registry required.
- **Package name**: `com.zacharysnewman.unity-ai-bridge`
- **Display name**: Unity AI Bridge
- **Initial version**: 1.0.0

---

## 8. Validation

Two validation mechanisms are planned:

1. **JSON Schema file** (`entity.schema.json`, `manifest.schema.json`) — AI tools and editors (VS Code, etc.) can use these to validate files before writing to disk, catching type errors and missing required fields without running Unity.
2. **Unity-side validator** — validates that all entity files can actually be loaded (prefab paths resolve, parent UUIDs exist, component types are found via reflection). Triggered via `Unity AI Bridge → Validate Scene` or by writing `Commands/validate.json`.

---

## 9. Non-Goals

The following are explicitly out of scope for this architecture:

- Runtime (built player) scene loading — this system targets the **Editor workflow** only.
- Serializing environmental settings (Skybox, lighting, fog) — these remain under normal Unity workflows.
- Replacing Unity's Addressables or AssetBundle systems.
- Serializing Unity-native component overrides (Colliders, Rigidbodies, etc.) beyond what the prefab defines.
- A batch-write API — AI tools (e.g., Claude Code) can already write multiple files in a single operation; no system-level batch endpoint is needed.

---

