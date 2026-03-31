# Implementation Progress

## Status: Initial Implementation Complete

All core package components have been implemented based on [SPEC.md](SPEC.md) and [MCP.md](MCP.md).

---

## Completed Features

### 1. Package Structure & Assembly Definitions
**Files:** `package.json`, `Runtime/*.asmdef`, `Editor/*.asmdef`

- UPM package manifest (`com.zacharysnewman.json-scenes-for-unity` v1.0.0)
- Runtime assembly definition (safe for player builds)
- Editor assembly definition (editor-only, references Runtime + Newtonsoft.Json)
- Dependency: `com.unity.nuget.newtonsoft-json`

---

### 2. `EntityReference` (Runtime)
**File:** `Runtime/EntityReference.cs`

- `[Serializable]` struct wrapping a `targetUUID` string
- Used in custom MonoBehaviour fields instead of direct `GameObject`/`MonoBehaviour` references
- Resolves at runtime via `SceneDataManager.Instance.GetByUUID(targetUUID)`

---

### 3. `EntitySync` (Runtime)
**File:** `Runtime/EntitySync.cs`

- `MonoBehaviour` attached dynamically to every spawned entity
- Holds the entity's stable `uuid` string
- Maintains an `isDirty` flag used by the write pipeline to gate disk writes
- Both fields hidden in Inspector to avoid clutter

---

### 4. `SceneDataManager` (Runtime)
**File:** `Runtime/SceneDataManager.cs`

- The only persistent (non-DontSave) object in the shell `.unity` scene file
- Holds `sceneDataPath` (project-relative path to scene data directory)
- Owns the `Dictionary<string, GameObject>` UUID→GameObject lookup
- Exposes `Register`, `Unregister`, `ClearRegistry`, `GetByUUID`, `GetAllUUIDs`
- Static `Instance` accessor for single-scene use; per-instance for additive scenes
- Runtime/Editor assembly boundary respected — never references `SceneIO`

---

### 5. `SceneIO` (Editor)
**File:** `Editor/SceneIO.cs`

Full serialization engine:

- **Bootstrap** (`BootstrapScene`): async 3-pass IEnumerator with Unity progress bar
  - Pass 1: instantiate all entity prefabs/primitives, attach `EntitySync`, apply `DontSave`
  - Pass 2: wire `transform.SetParent` hierarchy (must precede transform application)
  - Pass 3: apply `transform` (pos/rot/scl) and `customData` via reflection
- **Write pipeline** (`WriteEntity`, `SerializeEntity`): serializes GameObject→JSON with diff-guard (skips write if content unchanged)
- **Hot-reload** (`HotReloadEntity`): applies external file changes; treats `Created` as update-or-instantiate per spec
- **Destroy** (`DestroyEntity`): `DestroyImmediate` + registry unregister
- **Validation** (`ValidateScene`): checks prefab paths, parent UUIDs, component type resolution
- Primitive path support (`primitive/Cube`, etc.) via `PrimitiveLookup` table
- Component serialization: public fields + `[SerializeField]` private fields via reflection; excludes `EntitySync`; multi-component-by-type index matching

---

### 6. `EntityAssetPostprocessor` (Editor)
**File:** `Editor/EntityAssetPostprocessor.cs`

- `AssetPostprocessor.OnPostprocessAllAssets` implementation
- Only processes `.json` files inside `/Entities/` directories
- UUID injection rule enforcement:
  - Missing/empty `uuid` → `Guid.NewGuid()`, write field, rename file via `AssetDatabase.MoveAsset`
  - `uuid` ≠ filename → duplicate detected → new UUID, update field, rename
- Safety net for human-created entity files (AI uses MCP `create_entity` instead)

---

### 7. `LiveSyncController` (Editor)
**File:** `Editor/LiveSyncController.cs`

- `[InitializeOnLoad]` static constructor wires up all hooks
- **FileSystemWatcher**: separate watchers for `Entities/` and `Commands/` directories
- **Hot-reload pipeline**: FSW events marshalled from background threads to main Unity thread; routes to `SceneIO.HotReloadEntity` or `SceneIO.DestroyEntity`
- **Command dispatch**: reads command files from `Commands/`, executes `force_reload` or `validate_scene`, deletes command file immediately
- **Write pipeline**: `ObjectChangeEvents.changesPublished` integration for property changes, hierarchy changes, transform parenting, create, and destroy events
- **Debounce**: 300 ms timer per UUID; only final state at expiry is written
- **Play Mode**: suspends write pipeline and watcher on `ExitingEditMode`; re-bootstraps and restarts watcher on `EnteredEditMode`
- **Menu items**: `JSON Scenes/Force Reload Scene` and `JSON Scenes/Validate Scene`
- `EditorCoroutineRunner`: minimal in-package editor coroutine runner (no external dependency)

---

### 8. JSON Schema Validation Files
**Files:** `Schemas/entity.schema.json`, `Schemas/manifest.schema.json`

- JSON Schema Draft-07 schemas for offline validation by AI tools and VS Code
- `entity.schema.json`: validates `uuid` (UUID v4 pattern), `name`, `prefabPath`, `parentUuid`, `transform` (vec3 arrays), `customData` (typed component entries)
- `manifest.schema.json`: validates `schemaVersion` (integer ≥ 1) and `sceneName`
- No MCP dependency — usable entirely offline

---

### 9. MCP Server (Node.js)
**Files:** `mcp-server/package.json`, `mcp-server/index.js`

ES module Node.js server using `@modelcontextprotocol/sdk`:

- **`create_entity`**: generates UUID v4 via `crypto.randomUUID()`, writes complete entity JSON, returns `{ uuid, filePath }` — AI has UUID before writing any cross-referencing files
- **`force_reload`**: writes a command file to `Commands/` (fire-and-forget); supports per-entity or full-scene reload
- **`validate_scene`**: writes validate command file, polls for Unity's `validate_result.json` (15 s timeout, 200 ms interval), returns result JSON

---

## Known Limitations & Open Items

| Item | Status |
|---|---|
| Undo/Redo via `ObjectChangeEvents` | Unverified — needs Unity Editor testing to confirm Undo fires the same change events as direct edits (per SPEC.md §6.4) |
| FileSystemWatcher reliability on macOS | Known issue — mitigated by `force_reload` MCP tool |
| Domain Reload disabled incompatibility | Not supported — requires Unity's default domain reload enabled (per SPEC.md §5.4) |
| Same-type multi-component reordering | Accepted trade-off — component index maps by order, reordering same-type components breaks index alignment |
| MCP server `@modelcontextprotocol/sdk` version | Requires `npm install` before first use |

---

## File Tree

```
com.zacharysnewman.json-scenes-for-unity/
├── package.json
├── SPEC.md
├── MCP.md
├── PROGRESS.md
├── Runtime/
│   ├── com.zacharysnewman.json-scenes-for-unity.Runtime.asmdef
│   ├── SceneDataManager.cs
│   ├── EntitySync.cs
│   └── EntityReference.cs
├── Editor/
│   ├── com.zacharysnewman.json-scenes-for-unity.Editor.asmdef
│   ├── LiveSyncController.cs
│   ├── SceneIO.cs
│   └── EntityAssetPostprocessor.cs
├── Schemas/
│   ├── entity.schema.json
│   └── manifest.schema.json
└── mcp-server/
    ├── package.json
    └── index.js
```
