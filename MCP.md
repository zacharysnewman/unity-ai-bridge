# JSON Scenes for Unity — MCP Server Specification

## 1. Overview

The MCP (Model Context Protocol) server is a lightweight companion to the Unity package. Its scope is intentionally narrow: it handles operations that cannot be reduced to creating, reading, updating, or deleting text files.

Everything else — reading entity files, writing entity files, hot-reloading the scene, bootstrapping, deleting entities — is handled entirely by the Unity package reacting to file system changes. The MCP server is not involved in those workflows.

---

## 2. Why a Separate MCP Server

The Unity package owns the file system and the Unity editor runtime. AI tools (e.g., Claude Code) interact with the scene by reading and writing JSON files directly. The MCP server exists only to provide capabilities that:

- Cannot be done by writing a file (e.g., generating a cryptographically random UUID)
- Must not be fabricated by an AI (e.g., entity identifiers that must be globally unique)

---

## 3. Tools

### `generate_uuid`

Generates a single UUID v4 value.

**When to call:** Before writing any new entity JSON file to disk. Must be called for both `Create` and `Duplicate` operations (see SPEC.md §5.2). Never skip this call and substitute a manually written or guessed UUID string.

**Input:** None.

**Output:**

```json
{
  "uuid": "5f3a1b2c-3d4e-5f6a-7b8c-9d0e1f2a3b4c"
}
```

**Usage in entity creation workflow:**

1. Call `generate_uuid` → receive `uuid`.
2. Use `uuid` as both the JSON filename (`[uuid].json`) and the `uuid` field value inside the file.
3. Write the entity JSON to `Assets/SceneData/[SceneName]/Entities/[uuid].json`.
4. The Unity package's `FileSystemWatcher` detects the new file and instantiates the entity automatically.

---

## 4. Integration

The MCP server is configured in Claude Code's MCP settings. It runs as a local process alongside the Unity editor session.

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

---

## 5. Non-Goals

The following are explicitly **not** MCP server responsibilities:

- Writing, reading, or deleting entity or manifest JSON files — handled by the AI tool directly via file system access.
- Triggering Unity editor refreshes or reloads — handled automatically by the package's `FileSystemWatcher`.
- Bootstrapping or initializing the scene — handled by the Unity package on startup.
- Validating JSON schema — handled by the JSON Schema files shipped with the package (readable by any schema-aware tool without executing code).
- Any operation on Unity's asset database or editor state — out of scope entirely.
