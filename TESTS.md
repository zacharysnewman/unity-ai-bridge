# Unity AI Bridge — Test Cases

## JSON → Scene

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| J1 | Create JSON file | Object spawned in scene | PASSED | |
| J2 | Edit transform | Object moves/rotates/scales | PASSED | |
| J3 | Edit name | Object renamed in hierarchy | PASSED | |
| J4 | Add customData entry | Component added to object | PASSED | |
| J5 | Edit customData field | Component field updated | PASSED | |
| J6 | Remove customData entry | Component removed from object | PASSED | |
| J7 | Edit parentUuid | Object reparented | KNOWN LIMITATION | Hierarchy tree only rebuilds when Unity has focus — Unity editor windowing constraint, not fixable via API |
| J9 | Edit siblingIndex | Object moves to correct position among siblings | KNOWN LIMITATION | Hierarchy reorder only visually updates when Unity has focus — same constraint as J7 |
| J8 | Delete JSON file | Object destroyed | PASSED | Includes child cleanup when parented |

## Editor → JSON

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| E1 | Create object | New JSON file created | PASSED | |
| E2 | Duplicate object | New JSON file with new UUID | PASSED | |
| E3 | Move/rotate/scale object | JSON transform updated | PASSED | |
| E4 | Rename object | JSON name updated | PASSED | |
| E5 | Add component | JSON customData entry added | PASSED | |
| E6 | Modify component field | JSON customData field updated | PASSED | |
| E7 | Remove component | JSON customData entry removed | PASSED | |
| E8 | Reparent object | JSON parentUuid updated | PASSED | |
| E9 | Delete object | JSON file deleted | PASSED | |
| E10 | Drag-reorder siblings in hierarchy | JSON `siblingIndex` updated for affected objects | PASSED | |

## Undo Consistency

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| U1 | Hot-reload adds a component via JSON, then Ctrl+Z | Component removed from object; JSON updated to match | BY DESIGN | Hot-reload changes are not user actions and do not participate in undo |
| U2 | Hot-reload removes a component via JSON, then Ctrl+Z | Component restored on object; JSON updated to match | BY DESIGN | Hot-reload changes are not user actions and do not participate in undo |
| U3 | Hot-reload changes a transform via JSON, then Ctrl+Z | Transform reverts; JSON written back with old value via write pipeline | PASSED | Unity auto-records transform property writes; write pipeline catches the revert via ObjectChangeEvents |
| U4 | Hot-reload reparents via JSON, then Ctrl+Z | Parent reverts; JSON written back with old parentUuid | BY DESIGN | `SetParent` is not auto-recorded by Unity; hot-reload reparents don't participate in undo — scene and JSON remain in sync at the new state |
| U5 | Hot-reload changes siblingIndex via JSON, then Ctrl+Z | Sibling order reverts; JSON written back | BY DESIGN | `SetSiblingIndex` is not auto-recorded by Unity; same as U4 |

## Scene Lifecycle

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| S1 | Delete `.unity` scene file via Project window | Dialog: "Delete associated JSON data at X?" with Delete/Keep options; `.unity` file deleted either way | PASSED | |
| S2 | Rename `.unity` scene file via Project window | Data directory renamed to match; sync continues on new path | PASSED | |
| S3 | Move `.unity` scene file to different folder via Project window | Data directory moves to mirror new folder structure; sync continues | PASSED | |
| S4 | Run "Initialize Scene" on an unsaved scene | Dialog: "Save the scene before initializing" | PASSED | |
| S5 | Rename `.unity` scene file via OS (Finder/terminal) | Data directory not moved (processor not invoked); sync broken until user manually moves data dir and refreshes | KNOWN LIMITATION | `AssetModificationProcessor` only fires for Project-window operations |
| S7 | Duplicate `.unity` scene file via Project window (Ctrl+D) | Data directory contents copied to new scene's data directory; UUIDs preserved; new scene syncs independently | PASSED | |
| S8 | Two scenes with overlapping UUIDs open additively | Each scene resolves UUIDs within its own registry; no collision | PARTIAL | JSON→Unity hot reload correctly routes to the matching scene's manager via path-prefix lookup. Remaining limitation: `SceneDataManager.Instance` is a static singleton — cross-scene `EntityReference` resolution at runtime requires targeting the correct `SceneDataManager` instance directly rather than using `Instance` |
| S6 | Move `.unity` scene file out of a folder, leaving old parent empty in SceneData | Empty parent folder under `Assets/SceneData/` is deleted after the move | PASSED | |
| S9 | Write an entity JSON file for a closed scene while a different scene is open | Entity is NOT spawned in the open scene; console logs "no matching SceneDataManager … skipping hot reload"; entity appears correctly when the target scene is opened | PASSED | `EntityAssetPostprocessor` uses path-prefix matching to find the owning manager — if none matches (scene closed), hot reload is skipped entirely. Bootstrap on scene open is driven by `EditorSceneManager.activeSceneChangedInEditMode` |
| S10 | Open a different scene while the current scene is still bootstrapping | In-flight bootstrap cancels cleanly; new scene bootstraps correctly with no stray root-level objects and no `MissingReferenceException` | PASSED | `CancelBootstrap()` clears the lock and tracked manager; `targetScene` is captured before first yield so objects land in the correct scene even if active scene switches mid-coroutine; null guards before each pass prevent accessing destroyed manager |
| S11 | Run "Initialize Scene" on a scene with many objects | Progress bar displayed throughout; Unity does not hang | PASSED | `MigrateScene` converted to a coroutine — yields after each object in both passes with progress bar; `finally` block guarantees `SuppressWriteEvents` and progress bar are always cleared |
