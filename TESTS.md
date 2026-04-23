# Unity AI Bridge — Test Cases

## Built-In Components

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| B1 | Object with BoxCollider initialized — run "Initialize Scene" | `builtInComponents` array with `UnityEngine.BoxCollider` entry written to JSON | PASSED | |
| B2 | Edit `m_IsTrigger` in `builtInComponents` JSON | BoxCollider `isTrigger` flag updated on object | PASSED | |
| B3 | Edit `m_Size` in `builtInComponents` JSON | BoxCollider size updated on object | PASSED | |
| B4 | Add Rigidbody `builtInComponents` entry to JSON for object that has none | Rigidbody component added to object with specified field values | PASSED | |
| B5 | Remove entry from `builtInComponents` | Component destroyed on object | PASSED | |
| B6 | `query-scene Level_A "component == BoxCollider"` | Returns UUIDs of entities with BoxCollider | PASSED | |
| B7 | `query-scene Level_A "component == BoxCollider AND m_IsTrigger == true"` | Returns only trigger colliders | PASSED | |
| B8 | `query-scene Level_A "component == Rigidbody AND m_Mass >= 5"` | Returns only heavy rigidbodies | PASSED | |
| B9 | Add component in Unity Editor | JSON `builtInComponents` updated via write pipeline | PASSED | |

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
| J10 | Edit `tag` in JSON to a valid tag string | GameObject tag updated | PASSED | |
| J11 | Edit `layer` in JSON to a valid layer name | GameObject layer updated | PASSED | |
| J12 | Edit `isStatic` in JSON to `true` | GameObject marked static | PASSED | |
| J13 | Edit `activeSelf` in JSON to `false` | GameObject deactivated | PASSED | |
| J14 | Edit `tag` in JSON to an undefined tag name | Warning logged, tag unchanged on object | PASSED | |
| J15 | Edit `layer` in JSON to an undefined layer name | Warning logged, layer unchanged on object | PASSED | |

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
| E11 | Change tag on object in Inspector | JSON `tag` field updated | PASSED | |
| E12 | Change layer on object in Inspector | JSON `layer` field updated with layer name string | PASSED | |
| E13 | Toggle Static checkbox in Inspector | JSON `isStatic` updated to `true`/`false` | PASSED | |
| E14 | Deactivate object via hierarchy eye icon or Inspector checkbox | JSON `activeSelf` updated to `false` | PASSED | |

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

## Renderer & MeshFilter (Phase 3)

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| R1 | Object with MeshRenderer using a project material — run "Initialize Scene" | `builtInComponents` includes a `UnityEngine.MeshRenderer` entry; `m_Materials` is an array containing the material's asset path | NOT TESTED | |
| R2 | Primitive object (Cube) — run "Initialize Scene" | `builtInComponents` includes `UnityEngine.MeshRenderer` and `UnityEngine.MeshFilter`; `m_Mesh` in MeshFilter entry is absent or null (built-in mesh has no asset path) | NOT TESTED | |
| R3 | Edit `m_Materials` asset path in JSON to a different project material | MeshRenderer material updates on object immediately | NOT TESTED | |
| R4 | Remove `UnityEngine.MeshRenderer` entry from `builtInComponents` | MeshRenderer component destroyed on object | NOT TESTED | Expected by "array is complete truth" semantics |
| R5 | Change material on object in Inspector | JSON `m_Materials` updated with new asset path via write pipeline | NOT TESTED | |
| R6 | `patch-entities Level_A "component == MeshRenderer" "m_CastShadows = 0"` | All MeshRenderers in scene have shadow casting disabled | NOT TESTED | Validates Phase 2 + Phase 3 together |

## AnimationCurve & Gradient (Phase 1)

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| AC1 | Object with component having an `AnimationCurve` field — run "Initialize Scene" | JSON entry contains an array of keyframe objects with `time`, `value`, `inTangent`, `outTangent`, `tangentMode` keys | NOT TESTED | |
| AC2 | Edit a keyframe `value` in the JSON AnimationCurve array | AnimationCurve on component updated with new keyframe value | NOT TESTED | |
| AC3 | Add a keyframe object to the JSON array | AnimationCurve gains the extra keyframe | NOT TESTED | |
| AC4 | Edit AnimationCurve in Unity Inspector | JSON array updated with all keyframes via write pipeline | NOT TESTED | |
| GR1 | Object with component having a `Gradient` field — run "Initialize Scene" | JSON entry contains `{ "mode", "colorKeys": [...], "alphaKeys": [...] }` | NOT TESTED | |
| GR2 | Edit a `colorKeys` entry in JSON | Gradient color key updates on component | NOT TESTED | |
| GR3 | Edit Gradient in Unity Inspector | JSON `colorKeys`/`alphaKeys` updated via write pipeline | NOT TESTED | |

## ManagedReference / [SerializeReference] (Phase 4)

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| MR1 | Object with component having a `[SerializeReference]` field set to a concrete instance — run "Initialize Scene" | JSON entry contains `{ "__type": "assemblyName TypeFullName", <fields> }` | NOT TESTED | Concrete type must have a parameterless constructor |
| MR2 | Edit a field value inside the `__type` envelope in JSON | Field on the managed reference instance updates on component | NOT TESTED | |
| MR3 | Change `__type` in JSON to a different concrete type | Component's managed reference replaced with new type instance; fields applied | NOT TESTED | |
| MR4 | Set `[SerializeReference]` field to `null` in Inspector | JSON entry serializes as `null` | NOT TESTED | |
| MR5 | Set managed reference JSON value to `null` | Component's `[SerializeReference]` field set to null | NOT TESTED | |
| MR6 | Set `__type` to a type that lacks a parameterless constructor | Warning logged; field left unchanged | NOT TESTED | |

## Direct GameObject / Component References (Phase 5)

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| DR1 | Object A has a component with `public GameObject target` set to object B (both managed entities) — run "Initialize Scene" | JSON for A contains `"target": { "targetUUID": "<B's uuid>" }` | NOT TESTED | |
| DR2 | Object A has `public MyComponent target` set to a component on object B — run "Initialize Scene" | JSON for A contains `"target": { "targetUUID": "<B's uuid>" }` | NOT TESTED | |
| DR3 | Edit `targetUUID` in JSON to point to a different entity | Component's target field resolves to the new entity's GameObject/Component on hot-reload | NOT TESTED | |
| DR4 | Set `targetUUID` to a UUID that doesn't exist in the scene | Warning logged; field left null | NOT TESTED | |
| DR5 | JSON sets a `targetUUID` ref → save the scene → close and reopen | Reference is still correctly set after Unity deserializes the saved `.unity` file | NOT TESTED | **Key persistence test** — validates that `field.SetValue` on a `GameObject` ref is persisted by Unity's scene serializer, not just held in memory |
| DR6 | Object A refs object B; both JSON files written simultaneously (e.g. by a tool) | Both load correctly; A's reference resolves to B | NOT TESTED | Bootstrap Pass 3 runs after all entities registered in Pass 1, so forward refs should resolve |
| DR7 | Object with `public GameObject target` set to a non-managed GameObject (no EntitySync) — run "Initialize Scene" | Warning logged; field serialized as `null` | NOT TESTED | |
| DR8 | Set `target` field to `null` in Inspector | JSON entry serializes as `null` | NOT TESTED | |

## patch-entities — builtInComponents Write (Phase 2)

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| P1 | `patch-entities Level_A "component == BoxCollider" "m_IsTrigger = true"` | All BoxCollider entries in matching entity files have `m_IsTrigger` set to `true`; Unity hot-reloads and updates colliders | NOT TESTED | |
| P2 | `patch-entities Level_A "component == Rigidbody" "m_Mass = 5"` | All Rigidbody entries updated; mass changes reflected in Inspector | NOT TESTED | |
| P3 | `patch-entities Level_A "component == BoxCollider" "m_IsTrigger = true"` then `patch-entities Level_A --undo` | `m_IsTrigger` reverted to previous values on all affected entities | NOT TESTED | Undo reads `before_value` captured via `get_entity_field` which already searched builtInComponents |
| P4 | `patch-entities Level_A "component == BoxCollider" "m_Mass = 5"` (field not present on BoxCollider) | Warning logged for each entity: field not found | NOT TESTED | |

## CLI Tools

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| T3 | `Tools/select-objects Level_A <uuid>...` | Objects become selected in Unity hierarchy | PASSED | |
| T1 | `query-logs Log \| tail -2` | Returns the two most recent Unity console log entries | PASSED | Output is ordered chronologically; most recent entries are last. `tail -2` gives the final 2 lines — works cleanly for single-line entries; for entries with stack traces use a larger tail |
| T2 | `query-logs Log "[UnityAIBridge]"` after triggering a hot-reload | Returns entries from the current editor session | PASSED | `LogWriter.cs` registers `Application.logMessageReceived` and appends structured entries to `Logs/unity-ai-bridge.log`; file is cleared on `ExitingEditMode`; `query-logs` reads that file instead of `Editor.log` |
| T4 | `query-scene Level_A "tag == Player"` | Returns UUIDs of entities with tag `Player` | PASSED | |
| T8 | `query-scene Level_A "transform.pos.x % 1 != 0"` | Returns UUIDs of entities whose X position is not on a 1-unit grid | PASSED | |
| T9 | `query-scene Level_A "transform.pos.x % 1 != 0 OR transform.pos.z % 1 != 0"` | Returns UUIDs of entities misaligned on either X or Z axis | PASSED | |
| T10 | `query-scene Level_A "transform.pos.y % 0.5 == 0"` | Returns UUIDs of entities whose Y is a multiple of 0.5 | PASSED | |
| T5 | `query-scene Level_A "layer == UI"` | Returns UUIDs of entities on the UI layer | PASSED | |
| T6 | `query-scene Level_A "isStatic == true"` | Returns UUIDs of static entities | PASSED | |
| T7 | `query-scene Level_A "activeSelf == false"` | Returns UUIDs of inactive entities | PASSED | |
