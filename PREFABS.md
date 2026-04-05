# JSON Prefabs for Unity — Bidirectional Sync Plan

This document describes how to extend the JSON Scenes bidirectional sync system to cover **Unity Prefab assets**.
The design mirrors the scene sync architecture as closely as possible, adapting only where prefabs fundamentally differ from scenes.

---

## Core Invariant

> At any point in Edit Mode, the hierarchy and component state of every tracked prefab asset is exactly the data described by its corresponding JSON node files, and vice versa.

---

## Key Differences from Scenes

| Concern | Scenes | Prefabs |
|---|---|---|
| Storage medium | Unity `.unity` scene file + `Entities/*.json` | Unity `.prefab` asset file + `Nodes/*.json` |
| Write API | `ObjectChangeEvents` + serialization | `PrefabUtility.LoadPrefabContents()` + `SaveAsPrefabAsset()` |
| Change detection (Unity → JSON) | `ObjectChangeEvents.changesPublished` | `AssetPostprocessor.OnPostprocessPrefab` |
| Transform space | Root = world-space, children = local-space | All nodes always local-space (no world root) |
| "Entities" analog | One file per scene entity | One file per prefab node (GameObject within the prefab) |
| Nested prefabs | `prefabPath` on entity = what to instantiate | `nestedPrefabPath` on a node = the prefab it wraps |
| Prefab variants | N/A | `variantOf` field in manifest |
| Play mode | Write pipeline suspended | Write pipeline suspended |

---

## Directory Layout

Each tracked prefab maps to a `PrefabData/<PrefabName>/` directory you configure:

```
Assets/PrefabData/
└── HeavyDoor/
    ├── manifest.json          # Prefab metadata — do not modify schemaVersion or prefabPath
    ├── Commands/              # Short-lived command files (auto-deleted after execution)
    └── Nodes/
        ├── <root-uuid>.json   # Root node of the prefab (isRoot: true)
        └── <child-uuid>.json  # Child nodes; filename == uuid field exactly
```

The `.prefab` asset itself lives wherever you place it (e.g. `Assets/Prefabs/Environment/HeavyDoor.prefab`).
`manifest.json` records that path so the sync system knows which asset to read and write.

---

## Manifest File Format

```json
{
  "schemaVersion": 1,
  "prefabName": "HeavyDoor",
  "prefabPath": "Assets/Prefabs/Environment/HeavyDoor.prefab",
  "variantOf": null
}
```

| Field | Notes |
|---|---|
| `schemaVersion` | Do not modify — used for forward-compatibility checks |
| `prefabName` | Human-readable display name |
| `prefabPath` | Project-relative path to the `.prefab` asset. This is where the sync system reads from and writes to. |
| `variantOf` | If this prefab is a Prefab Variant, set to the base prefab path. Otherwise `null`. |

---

## Node File Format

```json
{
  "uuid": "550e8400-e29b-41d4-a716-446655440000",
  "name": "DoorFrame",
  "isRoot": false,
  "nestedPrefabPath": null,
  "parentUuid": "8a7b6c5d-4e3f-2a1b-9c0d-e1f2a3b4c5d6",
  "transform": {
    "pos": [0.0, 1.5, 0.0],
    "rot": [0.0, 0.0, 0.0],
    "scl": [1.0, 1.0, 1.0]
  },
  "customData": [
    {
      "type": "MyGame.Environment.DoorScript",
      "isLocked": true,
      "openAngle": 90.0
    }
  ]
}
```

### Field Reference

| Field | Required | Notes |
|---|---|---|
| `uuid` | Yes | UUID v4. Filename must match exactly (without `.json`). Never change after creation. |
| `name` | Yes | Display name of the GameObject in the prefab hierarchy. |
| `isRoot` | Yes | `true` for the single root node of the prefab. All other nodes are `false`. |
| `nestedPrefabPath` | No | If this node is an instance of another prefab, its project-relative path. `null` otherwise. |
| `parentUuid` | No | UUID of the parent node within this prefab. `null` only for the root. |
| `transform.pos` | Yes | Local-space position (meters). Always local — prefabs have no world root. |
| `transform.rot` | Yes | Local-space Euler angles (degrees), same values shown in Unity Inspector. |
| `transform.scl` | Yes | Local scale. |
| `customData` | No | Array of serialized MonoBehaviour entries (same schema as entity `customData`). |

---

## UUID Rules

All UUID rules from the scene system apply unchanged:

- Generate fresh UUID v4 strings: `xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx`
- The filename must equal the `uuid` field value exactly (without `.json`)
- **Never reuse or modify an existing node's UUID**
- **Never fabricate or guess a UUID** — always generate a fresh one
- When creating node A that will be referenced by node B: create A first, record its UUID, then create B

---

## The Root Node

Every prefab has exactly one root node (`"isRoot": true`). Its `parentUuid` must be `null`.
All other nodes descend from it via `parentUuid` chains.

Do not create a prefab directory without a root node file. The sync engine requires exactly one root to bootstrap the asset.

---

## Nested Prefabs

A node that is itself an instance of another prefab uses `nestedPrefabPath`:

```json
{
  "uuid": "a1b2c3d4-...",
  "name": "Handle",
  "isRoot": false,
  "nestedPrefabPath": "Assets/Prefabs/Props/DoorHandle.prefab",
  "parentUuid": "550e8400-...",
  "transform": { "pos": [0.1, 0.9, 0.05], "rot": [0.0, 0.0, 0.0], "scl": [1.0, 1.0, 1.0] },
  "customData": []
}
```

The sync engine will call `PrefabUtility.InstantiatePrefab()` for these nodes rather than creating a plain `GameObject`.
Custom component data on nested prefab nodes represents **overrides** applied on top of the base prefab values.

---

## Prefab Variants

Set `variantOf` in `manifest.json` to the base prefab path. The root node's state represents variant overrides.
The sync engine uses `PrefabUtility.InstantiatePrefab()` + `PrefabUtility.SaveAsPrefabAsset()` (not `CreateEmptyPrefab`).

```json
{
  "schemaVersion": 1,
  "prefabName": "HeavyDoor_Rusted",
  "prefabPath": "Assets/Prefabs/Environment/HeavyDoor_Rusted.prefab",
  "variantOf": "Assets/Prefabs/Environment/HeavyDoor.prefab"
}
```

---

## Custom MonoBehaviour Data (`customData`)

Identical rules to the scene system:

- `type` is the fully-qualified C# class name
- Only custom `MonoBehaviour` components are serialized (not Colliders, Renderers, etc.)
- Multiple components of the same type: Nth entry maps to Nth component instance on that node
- Built-in Unity components come from the prefab definition, not from JSON

---

## Cross-Node References

Use the same `EntityReference` struct (already in the Runtime assembly) for any serialized field that must point to another node within the same prefab or to a scene entity:

```json
"doorPanel": { "targetUUID": "8a7b6c5d-..." }
```

Resolved at runtime via `SceneDataManager.Instance.GetByUUID(targetUUID)` (for scene entities) or a new `PrefabNodeRegistry` equivalent if intra-prefab resolution is needed at edit time.

---

## Bidirectional Sync Flows

### Flow A: JSON → Prefab (External Edit / Hot Reload)

1. AI tool, text editor, or VCS modifies `Nodes/<uuid>.json`
2. `FileSystemWatcher` on the `Nodes/` directory detects the change on a background thread
3. Event is queued and flushed to the main thread on `EditorApplication.update`
4. `PrefabIO.HotReloadNode()` is called:
   - Load the prefab into a temporary editing environment via `PrefabUtility.LoadPrefabContents()`
   - Find the node by its `PrefabNodeSync.nodeUuid` component
   - **New node** (file created): instantiate as a new child `GameObject` under the correct parent, attach `PrefabNodeSync`
   - **Changed node** (file modified): apply updated transform and `customData` in-place
   - **Deleted node** (file removed): destroy the corresponding `GameObject` within the prefab
   - Re-wire parent hierarchy from `parentUuid` fields
   - Save via `PrefabUtility.SaveAsPrefabAsset()`, then `AssetDatabase.ImportAsset()`

**Key difference from scenes:** Prefab content must be loaded into a temporary `GameObject` tree, modified, and saved back as an asset — there is no persistent live scene to modify directly.

---

### Flow B: Prefab → JSON (Developer Edit in Prefab Mode)

1. Developer opens the prefab in Prefab Mode and changes a transform or component value
2. Developer saves the prefab (Ctrl+S or auto-save on exit from Prefab Mode)
3. Unity's asset pipeline fires `AssetPostprocessor.OnPostprocessPrefab(GameObject root)`
4. `PrefabAssetPostprocessor` checks if the prefab's path is registered in `PrefabDataRegistry`
5. If tracked, `PrefabIO.WritePrefabNodes()` serializes each node:
   - Walk the full hierarchy, read `PrefabNodeSync.nodeUuid` from each `GameObject`
   - Assign fresh UUIDs to any node missing a `PrefabNodeSync` (new GameObjects added in Prefab Mode)
   - **Diff guard:** only write if serialized JSON differs from the existing file
   - Write `Nodes/<uuid>.json` for every node
6. Delete any `Nodes/*.json` files whose UUIDs no longer appear in the hierarchy (nodes were removed)

**Key difference from scenes:** The trigger is `OnPostprocessPrefab` (asset pipeline), not `ObjectChangeEvents`. There is no continuous per-frame dirty check — writes happen on each prefab save.

---

## New Editor Scripts

### `PrefabIO.cs` — Serialization Engine

Mirrors `SceneIO.cs`. Key responsibilities:

- **`BootstrapPrefab(string dataDir)`** — reads all `Nodes/*.json` files, calls `PrefabUtility.LoadPrefabContents()`, builds the full node hierarchy, attaches `PrefabNodeSync` components, saves the prefab asset
- **`HotReloadNode(string dataDir, string uuid)`** — targeted reload of one node from its JSON file; handles create/update/delete within the prefab
- **`WritePrefabNodes(string dataDir, GameObject prefabRoot)`** — walks the prefab hierarchy, serializes each node, writes files with diff guard
- **`SerializeNode(GameObject node)`** — extracts transform (always local), `nestedPrefabPath` (via `PrefabUtility.GetCorrespondingObjectFromSource`), `customData` via reflection; returns `JObject`
- **`ApplyNodeData(GameObject node, JObject data)`** — applies transform and `customData` to a node in the loaded prefab content
- **`ValidatePrefab(string dataDir)`** — checks: exactly one root exists, all `parentUuid` references resolve, all `nestedPrefabPath` values are valid prefab assets, all component type names resolve

### `PrefabLiveSyncController.cs` — File Watcher & Write Pipeline

Mirrors `LiveSyncController.cs`. Key responsibilities:

- `[InitializeOnLoad]` — starts on domain reload
- Maintains a `FileSystemWatcher` per registered `PrefabData/<Name>/Nodes/` directory
- Thread-safe event queue, flushed on `EditorApplication.update`
- Debounce logic (300 ms) before triggering `HotReloadNode` — coalesces burst file writes
- Play mode handling: suspends the hot-reload pipeline on `ExitingEditMode`; restores on `EnteredEditMode`
- `SuppressWriteEvents` flag — prevents re-entrant writes during `BootstrapPrefab`

### `PrefabDataRegistry.cs` — Tracked Prefab Catalog

- `[InitializeOnLoad]` singleton (Editor-only)
- Maintains the list of `(dataDir, prefabPath)` pairs being tracked
- Reads `PrefabData/*/manifest.json` on load to auto-discover all registered prefabs
- Provides `IsTracked(string prefabPath)` for `PrefabAssetPostprocessor` to query
- Exposed in `PrefabDataManagerWindow` for add/remove

### `PrefabAssetPostprocessor.cs` — Unity-Side Change Detection

Mirrors `EntityAssetPostprocessor.cs`, but serves two purposes:

1. **UUID injection on new nodes:** When a developer adds a `GameObject` inside a tracked prefab in Prefab Mode, it won't have a `PrefabNodeSync` component. On the next `OnPostprocessPrefab`, detect any node missing a UUID, generate one, attach `PrefabNodeSync`, and save
2. **Trigger write pipeline:** After UUID injection (if any), call `PrefabIO.WritePrefabNodes()` to sync the entire prefab state to JSON

### `PrefabDataManagerWindow.cs` — Setup UI

Mirrors `SceneDataManagerWindow.cs`. Key features:

- Menu item: `JSON Scenes → Prefab Sync → Setup Window`
- List of currently tracked prefabs (path, data directory)
- "Track Prefab" button — accepts a `.prefab` asset path, creates the `PrefabData/<Name>/` directory and `manifest.json`, runs `BootstrapPrefab`
- "Untrack Prefab" button — removes from registry (does not delete JSON files)
- "Force Reload" button — reruns `BootstrapPrefab` from current JSON files
- "Validate" button — runs `ValidatePrefab` and shows results

---

## New Runtime Scripts

### `PrefabNodeSync.cs`

Minimal component attached to every synced `GameObject` within a tracked prefab:

```csharp
public class PrefabNodeSync : MonoBehaviour
{
    public string nodeUuid;
}
```

This is the prefab equivalent of `EntitySync`. It carries the stable UUID that links the `GameObject` to its `Nodes/<uuid>.json` file. Stored in the prefab asset itself, so the UUID persists across sessions.

---

## JSON Schemas

### `prefab-manifest.schema.json`

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["schemaVersion", "prefabName", "prefabPath"],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": { "type": "integer", "const": 1 },
    "prefabName":    { "type": "string", "minLength": 1 },
    "prefabPath":    { "type": "string", "pattern": "^Assets/.+\\.prefab$" },
    "variantOf":     { "type": ["string", "null"] }
  }
}
```

### `prefab-node.schema.json`

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["uuid", "name", "isRoot", "transform"],
  "additionalProperties": false,
  "properties": {
    "uuid":             { "type": "string", "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$" },
    "name":             { "type": "string", "minLength": 1 },
    "isRoot":           { "type": "boolean" },
    "nestedPrefabPath": { "type": ["string", "null"] },
    "parentUuid":       { "type": ["string", "null"] },
    "transform": {
      "type": "object",
      "required": ["pos", "rot", "scl"],
      "additionalProperties": false,
      "properties": {
        "pos": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3 },
        "rot": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3 },
        "scl": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3 }
      }
    },
    "customData": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["type"],
        "properties": {
          "type": { "type": "string", "minLength": 1 }
        },
        "additionalProperties": true
      }
    }
  }
}
```

---

## AI Instructions (CLAUDE.md additions)

Add the following section to `CLAUDE.md` (or a dedicated `CLAUDE_PREFABS.md`):

```markdown
## Prefab Sync

Each tracked prefab maps to Assets/PrefabData/<PrefabName>/ with a manifest.json and Nodes/ directory.
Use /prefab-overview Assets/PrefabData/HeavyDoor for a formatted hierarchy view.
Use /new-prefab-node for guided node creation with UUID generation.

### Node File Rules
- All transforms are local-space (no world root concept in prefabs)
- Exactly one node per prefab must have "isRoot": true
- isRoot node must have "parentUuid": null
- All other nodes must have a valid parentUuid pointing to another node in the same prefab
- Do not modify schemaVersion in manifest.json
- Do not modify prefabPath in manifest.json unless intentionally relocating the asset

### Nested Prefab Nodes
- Set nestedPrefabPath to the project-relative path of the nested prefab asset
- customData on a nested prefab node represents overrides only (not full component state)

### What NOT to Do
- Do not create a Nodes/ file without a corresponding root node file
- Do not write node files during Play Mode
- Do not fabricate or reuse UUIDs — always generate a fresh UUID v4 before writing a node file
- Do not use direct GameObject or component references in customData — use EntityReference
```

---

## Suggested Slash Commands

| Command | Purpose |
|---|---|
| `/prefab-overview <dataDir>` | Reads all `Nodes/*.json` in the given directory and renders an indented hierarchy with node names, UUIDs, and component types |
| `/new-prefab-node` | Guided interactive flow: prompts for prefab data directory, node name, parent, optional nested prefab path, generates a fresh UUID, writes the node file |

---

## VS Code Schema Wiring

Add to `.vscode/settings.json` in the Unity project:

```json
{
  "json.schemas": [
    {
      "fileMatch": ["**/PrefabData/**/Nodes/*.json"],
      "url": "./Packages/com.zacharysnewman.json-scenes-for-unity/Schemas/prefab-node.schema.json"
    },
    {
      "fileMatch": ["**/PrefabData/**/manifest.json"],
      "url": "./Packages/com.zacharysnewman.json-scenes-for-unity/Schemas/prefab-manifest.schema.json"
    }
  ]
}
```

---

## Implementation Order

Implement in this sequence to allow incremental testing at each step:

1. **`PrefabNodeSync.cs`** (Runtime) — trivial component, no dependencies
2. **`PrefabDataRegistry.cs`** (Editor) — catalog of tracked prefabs; reads manifests on load
3. **`PrefabIO.cs`** (Editor) — serialization engine; start with `WritePrefabNodes` and `BootstrapPrefab` before hot-reload
4. **`PrefabDataManagerWindow.cs`** (Editor) — UI to track/untrack prefabs and trigger bootstrap/validate manually
5. **`PrefabAssetPostprocessor.cs`** (Editor) — UUID injection + write-on-save; validates that Flow B works end-to-end
6. **`PrefabLiveSyncController.cs`** (Editor) — FileSystemWatcher + debounce; validates that Flow A works end-to-end
7. **Schemas** — `prefab-manifest.schema.json`, `prefab-node.schema.json`
8. **AI instructions** — update `CLAUDE.md`; add `/prefab-overview` and `/new-prefab-node` slash commands

---

## Open Questions / Design Decisions

1. **Variant override granularity** — Should `customData` on a variant node record only the overridden fields, or the full component state? Full state is simpler to implement; override-only matches Unity's internal model but requires diffing against the base.

2. **Nested prefab `customData` scope** — For a node that is a nested prefab instance, does `customData` mean "all components" or "only overrides"? Recommend "only overrides" for correctness, but this requires `PrefabUtility.GetPropertyModifications()` rather than plain reflection.

3. **Prefab Mode auto-save** — Unity can be configured to auto-save or prompt on Prefab Mode exit. The write pipeline triggers on `OnPostprocessPrefab`, which fires on either path. No special handling needed, but document this for users.

4. **`AssetDatabase.StartAssetEditing()` batching** — When hot-reloading many node files at once (e.g. on initial bootstrap or a large git checkout), batch all asset writes inside `StartAssetEditing()` / `StopAssetEditing()` to avoid a re-import per file.

5. **Rename guard** — If a node is renamed in JSON (`name` field changes), `PrefabIO` must find it by UUID (via `PrefabNodeSync.nodeUuid`), not by `GameObject.name`, to avoid creating a duplicate.

6. **Undo integration** — Changes applied via `HotReloadNode` bypass Unity's Undo stack (same as the scene system). Document that Ctrl+Z after an external edit will not undo the JSON-sourced change.
