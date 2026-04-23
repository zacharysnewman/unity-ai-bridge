# Evaluation: Current Implementation vs. Bi-directional Sync Spec

This document evaluates how well the current codebase aligns with the architecture described in `BIDIRECTIONAL_SYNC_SPEC.md`, identifies gaps, and lists what needs to change or be added.

---

## Summary Verdict

The current implementation is a **well-built system** that handles the bidirectional sync flows correctly at the mechanism level. The file watching, write pipeline, debounce, diff guard, UUID identity, and component serialization are all solid. The persistent-objects model is correctly implemented — entities are normal saved scene objects included in player builds.

---

## 1. Persistent GameObjects — Resolved ✅

### What the spec requires
The spec's Play Mode guarantee is: *"Unity just runs the scene natively. It doesn't need to read the JSON at all because the scene is already guaranteed to be an exact reflection of the data."*

The spec's Build Mode guarantee is: *"The raw JSON files are completely excluded from the final build... The game has no idea the JSON ever existed."*

Both guarantees rest on a single assumption: **GameObjects in the scene are real, saved, persistent Unity objects**. The JSON and the scene are perfect mirrors. Either can be treated as the source at any moment.

### Current behavior (implemented correctly)

Entity GameObjects are normal persistent scene objects — no `HideFlags.DontSave`. This means:
- Entities are saved into the `.unity` scene file by Unity's standard serialization.
- Player builds include all entities.
- Entities survive Play Mode; the scene is not empty during play.
- `ReconcileScene` (the non-destructive bootstrap) updates entities in-place rather than destroy-and-rebuild.

| Guarantee | Spec Requirement | Current Behavior |
|---|---|---|
| Play Mode | Scene is already correct; Unity runs natively | ✅ Entities persist through play; sync pipelines suspend during play |
| Build Mode | Standard Unity build with all objects intact | ✅ Entities saved in `.unity` and included in builds |
| Mirror invariant | Unity scene = JSON at all times | ✅ In-place reconciliation maintains the invariant |

---

## 2. Alignment: What Works Well

These components of the current implementation align with the spec and require no changes to their core logic.

### 2.1 UUID-Based Object Identity ✅
`EntitySync` holds the UUID on every entity GameObject. Entity files are named `[uuid].json`. The UUID is the stable shared key. This is exactly what the spec requires. The component could be renamed `DataLink` for clarity but this is cosmetic.

### 2.2 Flow A: JSON → Unity ✅
`LiveSyncController` uses `FileSystemWatcher` to detect changes in `Entities/`. Changes are marshalled from background threads to the main Unity thread. `SceneIO.HotReloadEntity` locates the matching `GameObject` by UUID and applies the new state. New files trigger instantiation; deleted files trigger `DestroyImmediate`. This is correct.

### 2.3 Flow B: Unity → JSON ✅
`LiveSyncController` hooks `ObjectChangeEvents.changesPublished` to detect Editor changes. Dirty entities are queued, debounced at 300ms, and written via `SceneIO.WriteEntity`. The diff guard prevents spurious writes when content is unchanged. This is correct and more granular than the `AssetModificationProcessor` approach mentioned in the spec.

### 2.4 Debounce and Diff Guard ✅
300ms debounce prevents thrashing during drag operations. The diff guard in `SceneIO` skips writes when the serialized output is identical to the existing file. Both are correct.

### 2.5 Per-Entity Files ✅
One JSON file per entity. Git-friendly. AI can grep and edit individual files. The directory structure (`Entities/`, `Commands/`, `manifest.json`) is correct.

### 2.6 Component Serialization ✅
Only `MonoBehaviour` subclasses are serialized. Built-in Unity components (MeshRenderer, Rigidbody, etc.) are excluded — they live in the prefab. Public fields and `[SerializeField]` private fields are serialized via reflection. Multi-component-by-type index matching is implemented.

### 2.7 EntityReference ✅
Cross-entity references use `EntityReference` struct wrapping a `targetUUID` string. Direct `GameObject`/`MonoBehaviour` references are prohibited. Runtime resolution via `SceneDataManager.Instance.GetByUUID`.

### 2.8 Assembly Isolation ✅
Editor scripts (`LiveSyncController`, `SceneIO`, `EntityAssetPostprocessor`) are in the Editor assembly and stripped from builds. Runtime scripts (`SceneDataManager`, `EntitySync`, `EntityReference`) are in the Runtime assembly. Correct.

### 2.9 EntityAssetPostprocessor ✅
Handles human-initiated file creation (drag-drop, Ctrl+D copy). UUID injection and filename normalization are correct.

### 2.10 Three-Pass Bootstrap Logic ✅
The bootstrap sequence (instantiate → wire hierarchy → apply transforms) is correct and necessary. The ordering ensures local-space transforms are applied only after the parent hierarchy is established. This logic should be preserved even as bootstrap transitions from "mandatory on every open" to "optional recovery operation."

### 2.11 Domain Reload Handling ✅
`AssemblyReloadEvents` stop/restart the `FileSystemWatcher` cleanly. The `[InitializeOnLoad]` cycle restarts correctly after script recompiles.

---

## 3. Minor Gaps

These are smaller issues that should be addressed but are not blocking.

### 3.1 Play Mode Validation (Optional, Not Implemented)
The spec mentions an optional startup check: compare JSON entity count / UUID set against the live scene before entering Play Mode, warning if a desync is detected. This is not implemented. Low priority — since entities are persistent, desyncs will be rare. Worth adding as a `[RuntimeInitializeOnLoadMethod]` or Editor pre-play callback.

### 3.2 Build Postprocessor (Safety Net, Not Implemented)
No build postprocessor explicitly verifies that JSON files are excluded from builds. Currently, files in `Assets/SceneData/` are not in `Resources/` or `StreamingAssets/` so they are excluded by default. A `BuildPlayerProcessor` could add an explicit check/warning. Low priority.

### 3.3 EntitySync in Runtime Assembly Exposes isDirty to Builds
`EntitySync` (Runtime assembly) has an `isDirty` flag that is Editor-only behavior. In a build, this flag is dead weight. Projects that don't use `EntityReference` also carry the `uuid` field on every entity `GameObject`. This is a minor overhead. Consider a `#if UNITY_EDITOR` guard on `isDirty`, or splitting into `DataLink` (uuid only, runtime-safe) and an Editor-only extension for the dirty flag.

### 3.4 "Sync to JSON" Toolbar Button
The spec mentions a manual **"Sync to JSON"** button as an explicit sync trigger. The current `Setup Window` has a "Force Reload" (JSON → Unity) but no explicit "Sync to JSON" (Unity → JSON) button. The automatic write pipeline handles this in practice, but an explicit button (which serializes all registered entities to JSON immediately) would be a useful manual recovery tool. Low priority.

### 3.5 AssetModificationProcessor Not Used
The spec mentions `AssetModificationProcessor` (catches scene saves) as one mechanism for Flow B. The current implementation uses `ObjectChangeEvents` instead, which is more granular and real-time. `ObjectChangeEvents` is the better choice and should be kept. The spec mention of `AssetModificationProcessor` is illustrative, not prescriptive. No change needed — this is worth documenting as an explicit decision.

### 3.6 Naming: EntitySync vs DataLink
The spec uses "DataLink" as the name for the UUID-carrying component. The current implementation uses "EntitySync." Functionally equivalent. Renaming is a cosmetic change but would align naming with the spec's language. Consider renaming in a future refactor pass.

---

## 4. What Needs to Change — Prioritized

### Priority 1 (Blocking — Core Architecture) — All Resolved ✅

| Item | Status |
|---|---|
| **Remove `HideFlags.DontSave` from entities** | ✅ Done — entities are normal persistent GameObjects |
| **Remove destroy-on-enter-play logic** | ✅ Done — entities persist through Play Mode; no destroy/bootstrap cycle |
| **Remove mandatory bootstrap on every domain reload** | ✅ Done — `ReconcileScene` is non-destructive; existing entities updated in-place |
| **Update `SceneDataManagerWindow` setup flow** | ✅ Done — bootstrap is a recovery/initial-setup operation, not mandatory |

### Priority 2 (Important — Correctness)

| Item | Change Required |
|---|---|
| **Verify write pipeline writes through to `.unity` on save** | With persistent objects, Unity's scene save (`Ctrl+S`) will capture entity state. Ensure the write pipeline (JSON ← Unity) fires correctly and that the `.unity` file and JSON remain in sync after a scene save. |
| **Ensure HotReload updates in-place without re-instantiation** | The current hot-reload correctly updates in-place for existing UUIDs. Verify this still works correctly now that objects are persistent (no re-instantiation needed unless the object was genuinely absent). |

### Priority 3 (Nice to Have)

| Item | Change Required |
|---|---|
| **Add pre-play scene validation** | Compare JSON UUID set vs live scene entity UUIDs before entering Play Mode. Log a warning if they differ. Implement as an Editor pre-play callback. |
| **Add explicit "Sync to JSON" button** | Add a button to `SceneDataManagerWindow` that immediately serializes all entities to JSON (Unity → JSON full flush). Useful recovery tool. |
| **Build postprocessor** | Add a `BuildPlayerProcessor` that asserts no JSON files are included in the build. Warn if `Assets/SceneData/` has been inadvertently added to `StreamingAssets/`. |
| **Guard `isDirty` with `#if UNITY_EDITOR`** | In `EntitySync`, wrap the `isDirty` field in `#if UNITY_EDITOR` to avoid dead weight in builds. |

---

## 5. Unchanged Current SPEC.md Sections

The following sections of the current `SPEC.md` remain correct and should be retained as-is:

- §3.3 Entity JSON Schema (uuid, name, prefabPath, parentUuid, transform, customData)
- §3.4 Primitive Objects (`primitive/Cube`, etc.)
- §4.3 `LiveSyncController` description
- §4.4 `SceneIO` description
- §4.5 `EntityAssetPostprocessor`
- §5.2 Unity Editor → JSON write pipeline table
- §5.3 JSON → Unity hot-reload pipeline
- §5.5 Prefab Propagation
- §6.1 Component Serialization rules
- §6.2 Reference Handling (`EntityReference`)
- §6.3 UUID Generation
- §6.4 Version Control & Reversions
- §6.5 Schema Versioning
- §7 Package Structure
- §8 Validation

The sections that previously required revision — **§5.4 Play Mode** and the **§2 Core Principles** "Shell Scene" entry — have been updated to reflect the persistent-objects model.
