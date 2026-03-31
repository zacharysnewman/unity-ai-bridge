# JSON Scenes for Unity — MCP Server Specification

## 1. Overview

The MCP (Model Context Protocol) server is a lightweight companion to the Unity package. Its scope is intentionally narrow: it handles operations that require executing code inside the Unity Editor and cannot be reduced to creating, reading, updating, or deleting text files.

Most AI interactions with the scene are pure file CRUD — write an entity file, and the Unity package reacts automatically via its `FileSystemWatcher`. The MCP server is only needed when the AI must *trigger an action* inside Unity, not just change data on disk.

---

## 2. Communication Architecture

The MCP server never opens a network connection to Unity. Instead, it communicates via **command files** written to the `Commands/` directory inside each scene directory:

```
Assets/SceneData/Level_01/
├── Commands/          # MCP server writes here; package reads and deletes
├── manifest.json
└── Entities/
```

The Unity package's `FileSystemWatcher` watches `Commands/` alongside `Entities/`. When a command file appears, the package reads it, executes the requested action inside Unity, then immediately deletes the file. This keeps all MCP→Unity communication within the file-CRUD model.

**Command file schema:**

```json
{
  "command": "force_reload",
  "targetUuid": "5f3a1b2c-..."
}
```

| Field | Type | Description |
|---|---|---|
| `command` | string | The command name. See §3 for valid values. |
| `targetUuid` | string (UUID v4) | Optional. Scopes the command to a single entity. Omit to apply to the whole scene. |

Command files are ephemeral. The AI tool must not assume a command file persists; the package deletes it after execution.

---

## 3. Tools

### `generate_uuid`

Generates a single UUID v4 value.

**When to call:** Before writing any new entity JSON file to disk. Must be called for both `Create` and `Duplicate` operations (see SPEC.md §5.2). AI assistants must never fabricate or substitute a UUID string.

**Why not file CRUD:** UUID generation requires a cryptographically random value that cannot be fabricated or guessed. It is not a file operation.

**Input:** None.

**Output:**

```json
{
  "uuid": "5f3a1b2c-3d4e-5f6a-7b8c-9d0e1f2a3b4c"
}
```

**Workflow:**

1. Call `generate_uuid` → receive `uuid`.
2. Use `uuid` as both the filename (`[uuid].json`) and the `uuid` field inside the entity file.
3. Write the entity JSON to `Assets/SceneData/[SceneName]/Entities/[uuid].json`.
4. The Unity package's `FileSystemWatcher` detects the new file and instantiates the entity automatically. No further MCP call needed.

---

### `force_reload`

Forces the Unity package to re-read one or all entity files from disk and re-apply them to the scene hierarchy.

**When to call:** When the `FileSystemWatcher` may have missed a file change event (known reliability issue on macOS), or when the AI needs to confirm that Unity has fully processed a batch of file writes before querying results.

**Why not file CRUD:** Triggering a reload requires executing code inside the Unity Editor. Writing a file alone is not sufficient if FSW missed the event.

**Mechanism:** Writes a command file to `Commands/` (see §2). The package executes the reload and deletes the command file.

**Input:**

| Field | Type | Required | Description |
|---|---|---|---|
| `targetUuid` | string | No | UUID of a single entity to reload. Omit to reload the entire scene. |

**Output:** None. The command file is the signal; execution is fire-and-forget.

---

### `validate_scene`

Triggers the Unity-side scene validator, which checks that all entity files in the scene directory can be fully loaded: prefab paths resolve, parent UUIDs exist, and all `type` values in `customData` resolve to known component types via reflection.

**When to call:** After making a large batch of entity changes, or before marking a scene as ready for review.

**Why not file CRUD:** Validation requires Unity's asset database and reflection system. The JSON Schema file validator (SPEC.md §8) can catch schema errors without Unity, but only the Unity-side validator can confirm that prefab paths, parent references, and component types are actually valid in the project.

**Mechanism:** Writes a command file to `Commands/`. The package runs validation and writes results to `Commands/validate_result.json`. The MCP server reads the result file and returns it to the caller.

**Input:** None (always validates the full scene).

**Output** (read from `Commands/validate_result.json` after the package writes it):

```json
{
  "valid": false,
  "errors": [
    {
      "uuid": "5f3a1b2c-...",
      "field": "prefabPath",
      "message": "Prefab not found at Assets/Prefabs/Door.prefab"
    }
  ]
}
```

---

## 4. What Does NOT Need MCP

The following operations are handled automatically by the Unity package reacting to file system changes. The AI tool only needs to write, read, or delete files.

| Operation | How it works without MCP |
|---|---|
| Create entity | Write `[uuid].json` to `Entities/` → FSW triggers instantiation |
| Update entity | Overwrite `[uuid].json` → FSW triggers live update |
| Delete entity | Delete `[uuid].json` → FSW triggers `DestroyImmediate` |
| Read scene state | Read files in `Entities/` directly |
| Bootstrap / initial load | Triggered automatically by `[InitializeOnLoad]` |
| Play Mode transitions | Handled by `EditorApplication.playModeStateChanged` in package |
| Prefab propagation | Unity's native prefab connection handles it |
| Undo/Redo | Unity's undo system triggers the write pipeline |
| Git rollback | FSW detects file changes from `git checkout` automatically |
| JSON schema validation | Read schema files; validate offline without Unity |

---

## 5. Integration

The MCP server is configured in Claude Code's MCP settings and runs as a local process alongside the Unity editor session.

```json
{
  "mcpServers": {
    "json-scenes-for-unity": {
      "command": "node",
      "args": ["path/to/mcp-server/index.js"]
    }
  }
}
```

> The exact server implementation (language, entry point, package) is not yet specified.
