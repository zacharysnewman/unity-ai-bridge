# Implementation Progress

## Status: Core Complete + Domain Reload & Setup Window

All core package components implemented. Three additional gaps patched based on review of CoplayDev/unity-mcp.

---

## Completed Features

### 1. Package Structure & Assembly Definitions
**Files:** `package.json`, `Runtime/*.asmdef`, `Editor/*.asmdef`

- UPM package manifest (`com.zacharysnewman.unity-ai-bridge` v1.0.0)
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
- Safety net for human-created entity files (AI writes files directly)

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
- **Menu items**: `Unity AI Bridge/Force Reload Scene` and `Unity AI Bridge/Validate Scene`
- `EditorCoroutineRunner`: minimal in-package editor coroutine runner (no external dependency)

---

### 8. JSON Schema Validation Files
**Files:** `Schemas/entity.schema.json`, `Schemas/manifest.schema.json`

- JSON Schema Draft-07 schemas for offline validation by AI tools and VS Code
- `entity.schema.json`: validates `uuid` (UUID v4 pattern), `name`, `prefabPath`, `parentUuid`, `transform` (vec3 arrays), `customData` (typed component entries)
- `manifest.schema.json`: validates `schemaVersion` (integer ≥ 1) and `sceneName`
---

### 10. Domain Reload Handling (`LiveSyncController`)
**File:** `Editor/LiveSyncController.cs` (patched)

- `AssemblyReloadEvents.beforeAssemblyReload` → `StopWatcher()` + sets `_isReloading = true` so the update loop and write queue don't process stale events
- `AssemblyReloadEvents.afterAssemblyReload` → clears flag, schedules `TryBootstrap` via `delayCall`
- FSW no longer silently dies after script recompiles — restarts cleanly on the next `[InitializeOnLoad]` cycle

---

### 11. `force_reload` + `AssetDatabase.Refresh`
**File:** `Editor/LiveSyncController.cs` (patched)

- `ExecuteForceReload` and `ForceReloadScene` menu item now call `AssetDatabase.Refresh(ForceUpdate | ForceSynchronousImport)` before re-reading entity files
- Ensures Unity picks up file changes that FSW may have missed on macOS before the scene is re-bootstrapped

---

### 12. Setup Editor Window
**File:** `Editor/SceneDataManagerWindow.cs`

Minimal setup UI accessible via `Unity AI Bridge → Setup Window`:
- **Manager section**: detects whether `SceneDataManager` exists in the scene; shows `sceneDataPath` field with live validation (directory/manifest checks); buttons to create `SceneDataManager`, directory structure, and `manifest.json` automatically
- **Actions section**: Force Reload Scene and Validate Scene buttons (delegates to existing `LiveSyncController` menu items)

---

## Completed Features (continued)

### 13. Computed `sceneDataPath` + Scene Lifecycle Handling

**Motivation:** `sceneDataPath` is currently a manually-configured serialized string on `SceneDataManager`. If the `.unity` scene file is renamed or deleted via the Project window, the stored string becomes stale and sync breaks silently.

**Changes:**

- **`SceneDataManager`** — remove the serialized `sceneDataPath` string field. Replace with a computed property that derives the path from `gameObject.scene.path` at runtime using the convention `Assets/Scenes/Level_01.unity` → `Assets/SceneData/Scenes/Level_01`. Display-only (read-only) in the Inspector. Guard: return `null` and log a warning when `scene.path` is empty (unsaved scene).

- **New `SceneAssetModificationProcessor`** — `AssetModificationProcessor` subclass (Editor assembly):
  - `OnWillMoveAsset`: when a `.unity` file is renamed/moved, compute old and new data directory paths and call `AssetDatabase.MoveAsset` to rename the data directory atomically. Return `DidNotMove` so Unity handles the `.unity` file itself.
  - `OnWillDeleteAsset`: when a `.unity` file is deleted, compute the data directory path and prompt the user to delete it. Return `DidNotDelete` regardless of choice.

- **`InitializeScene`** — remove the `sceneDataPath` defaulting logic (no longer needed; path is always derived).

- **`SceneDataManagerWindow`** — update the Inspector/setup window to display the derived path as read-only instead of an editable field.

**Caveats to resolve during implementation:**
- Verify `AssetDatabase.MoveAsset` is safe to call from within `OnWillMoveAsset`. If not, fall back to `EditorApplication.delayCall` (one-frame racy but functionally correct).
- OS-level file moves (Finder, `mv`) bypass `AssetModificationProcessor` — document as a known limitation; user must move the data directory manually and call `AssetDatabase.Refresh`.

**Files:** `Runtime/SceneDataManager.cs`, `Editor/LiveSyncController.cs`, `Editor/SceneAssetModificationProcessor.cs` (new)

**Spec:** §4.1, §4.5, §5.5

---

### 14. Sibling Index Serialization

**Motivation:** Without sibling index, hierarchy child order is non-deterministic after load (UUID-alphabetical). This breaks `transform.GetChild(index)`, UI layout groups, canvas render order, and any script that iterates children positionally.

**Changes:**

- **`SerializeEntity`** — writes `siblingIndex` as `go.transform.GetSiblingIndex()`
- **`BootstrapScene` / `ReconcileScene`** — added Pass 2b after all `SetParent` calls: iterates entities and calls `SetSiblingIndex`. Must run after Pass 2 so all siblings exist before ordering
- **`HotReloadEntity`** — applies `SetSiblingIndex` after parent wiring
- **`entity.schema.json`** — added `siblingIndex` as optional integer ≥ 0
- **SPEC.md** — added to field reference table; removed from non-goals

**Files:** `Editor/SceneIO.cs`, `Schemas/entity.schema.json`

**Spec:** §3.3

---

## Known Limitations & Open Items

| Item | Status |
|---|---|
| Undo/Redo via `ObjectChangeEvents` | Unverified — needs Unity Editor testing to confirm Undo fires the same change events as direct edits (per SPEC.md §6.4) |
| FileSystemWatcher reliability on macOS | Known issue — mitigated by `AssetDatabase.Refresh` on force reload |
| Domain Reload disabled incompatibility | Not supported — requires Unity's default domain reload enabled (per SPEC.md §5.4) |
| Same-type multi-component reordering | Accepted trade-off — component index maps by order, reordering same-type components breaks index alignment |
| OS-level scene rename/move | `AssetModificationProcessor` only intercepts Project-window operations; Finder/`mv` renames require manual data directory move + `AssetDatabase.Refresh` |

---

## File Tree

```
com.zacharysnewman.unity-ai-bridge/
├── package.json
├── SPEC.md
├── PROGRESS.md
├── Runtime/
│   ├── com.zacharysnewman.unity-ai-bridge.Runtime.asmdef
│   ├── SceneDataManager.cs
│   ├── EntitySync.cs
│   └── EntityReference.cs
├── Editor/
│   ├── com.zacharysnewman.unity-ai-bridge.Editor.asmdef
│   ├── LiveSyncController.cs
│   ├── SceneIO.cs
│   └── EntityAssetPostprocessor.cs
├── Schemas/
│   ├── entity.schema.json
│   └── manifest.schema.json
```
