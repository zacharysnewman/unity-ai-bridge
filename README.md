# JSON Scenes for Unity

A live-sync, text-based scene format for Unity. Replaces binary `.unity` scene serialization with human- and AI-readable per-entity JSON files, bidirectionally synced with the Unity Editor in real time.

- **AI-friendly** — each entity is its own file; LLMs can read, create, and edit scenes directly
- **Git-friendly** — per-object diffs instead of monolithic binary blobs
- **Live sync** — changes on disk hot-reload instantly into the Editor, and Editor changes write back to disk automatically
- **No "Save Scene"** — the file system is always the source of truth

**Minimum Unity version:** Unity 6 (6000.x)

---

## Installation

### 1. Add the Unity package

In Unity: **Window → Package Manager → + → Add package from git URL**

```
https://github.com/zacharysnewman/json-scenes-for-unity.git
```

This also installs the required `com.unity.nuget.newtonsoft-json` dependency.

### 2. Install the MCP server

```bash
cd Packages/com.zacharysnewman.json-scenes-for-unity/mcp-server
npm install
```

### 3. Configure Claude Code

Add to your Claude Code MCP settings (`.claude/mcp.json` or `~/Library/Application Support/Claude/claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "json-scenes-for-unity": {
      "command": "node",
      "args": ["/path/to/Packages/com.zacharysnewman.json-scenes-for-unity/mcp-server/index.js"]
    }
  }
}
```

---

## Project Setup

### 1. Create the shell scene

1. Create a new empty Unity scene (this will be the only `.unity` file you ever save)
2. Add an empty GameObject, name it `SceneDataManager`
3. Add the **Scene Data Manager** component to it
4. Open **JSON Scenes → Setup Window** and set the **Scene Data Path**, e.g. `Assets/SceneData/Level_01`
5. Click **Create Directory Structure** — this creates the `Entities/`, `Commands/`, and `manifest.json` automatically
6. Save the scene (`Ctrl+S`) — this is the only time you need to save it

### 2. Verify

Open **JSON Scenes → Setup Window**. The panel should show:

- A green info box confirming the scene data directory is ready
- No warnings about missing `SceneDataManager` or `manifest.json`

The package bootstraps automatically on Editor load. Any `.json` files placed in `Entities/` will appear in the hierarchy instantly.

---

## How It Works

```
Assets/SceneData/Level_01/
├── manifest.json          ← scene metadata (name, schema version)
├── Commands/              ← MCP server writes here; package executes and deletes
└── Entities/
    ├── <uuid>.json        ← one file per entity
    └── <uuid>.json
```

Each entity file looks like this:

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
      "isLocked": true
    }
  ]
}
```

**Primitive objects** use reserved `prefabPath` values: `primitive/Cube`, `primitive/Sphere`, `primitive/Cylinder`, `primitive/Capsule`, `primitive/Plane`, `primitive/Quad`.

**Cross-entity references** use the `EntityReference` struct instead of direct `GameObject` refs:

```csharp
public EntityReference doorTarget; // serializes as { "targetUUID": "..." }
// resolve at runtime:
var go = SceneDataManager.Instance.GetByUUID(doorTarget.targetUUID);
```

---

## MCP Tools (for AI assistants)

The MCP server exposes three tools. Most scene editing is plain file CRUD — these tools cover the cases that need a UUID or Unity's code execution.

### `create_entity`

Generates a UUID, writes the entity file, and returns the UUID and file path **before** any other files are written. Use this whenever you need to reference the new entity from another entity.

```
create_entity(
  scenePath: "Assets/SceneData/Level_01",
  name: "Chest_01",
  prefabPath: "Assets/Prefabs/Items/Chest.prefab",
  transform: { pos: [5, 0, 3], rot: [0, 45, 0], scl: [1, 1, 1] }
)
→ { uuid: "...", filePath: "Assets/SceneData/Level_01/Entities/<uuid>.json" }
```

### `force_reload`

Forces Unity to re-read entity files from disk. Use on macOS where `FileSystemWatcher` can occasionally miss events, or after a large batch of writes.

```
force_reload(scenePath: "Assets/SceneData/Level_01")             // full scene
force_reload(scenePath: "...", targetUuid: "5f3a1b2c-...")       // single entity
```

### `validate_scene`

Runs Unity-side validation: checks that all prefab paths resolve, parent UUIDs exist, and component types are found via reflection.

```
validate_scene(scenePath: "Assets/SceneData/Level_01")
→ { valid: true, errors: [] }
```

---

## Editor Workflows

| Action | How it works |
|---|---|
| **Drop a prefab into the scene** | `EntityAssetPostprocessor` assigns a UUID and writes `Entities/<uuid>.json` automatically |
| **Move / rename / edit in Inspector** | 300 ms debounce writes the updated JSON to disk |
| **Delete an object** | The corresponding `<uuid>.json` is deleted from disk |
| **Duplicate (Ctrl+D)** | Postprocessor detects UUID mismatch, assigns a fresh UUID |
| **Edit a file externally** | `FileSystemWatcher` picks up the change and hot-reloads the entity |
| **Git checkout** | FSW detects all changed files and snaps the scene to the restored state |
| **Force reload** | `JSON Scenes → Force Reload Scene` or the `force_reload` MCP tool |
| **Validate** | `JSON Scenes → Validate Scene` or the `validate_scene` MCP tool |

---

## Play Mode

On **Enter Play Mode** all managed entities (flagged `DontSave`) are destroyed. On **Exit Play Mode** the full bootstrap runs again, reinstantiating everything from disk. JSON files are never written during Play Mode.

> **Requirement:** Unity's domain reload must be **enabled** (the default). Projects with *Enter Play Mode Settings → Disable Domain Reload* are not supported.

---

## JSON Schema Validation

`Schemas/entity.schema.json` and `Schemas/manifest.schema.json` (JSON Schema Draft-07) let VS Code and other editors validate entity files offline — no Unity or MCP required. Point your editor's JSON schema association at these files for real-time validation while editing.

---

## Known Limitations

- **Undo/Redo** — expected to work via `ObjectChangeEvents`, but unverified in practice. Test early and file an issue if Undo doesn't write back to disk correctly.
- **macOS FileSystemWatcher** — occasionally misses events. Use `force_reload` as a recovery tool.
- **Same-type multi-component reordering** — if two components of the same type on one object are reordered via the Inspector, `customData` indices will map incorrectly on the next load.
- **Built-in components** — only custom `MonoBehaviour` fields are serialized. Built-in components (Collider, Rigidbody, etc.) derive their state from the source prefab.
