# Bi-directional Sync Architecture — Guiding Specification

**System Model:** Model-View-Controller (MVP)
- **Model:** JSON files on disk
- **View:** Unity Scene hierarchy
- **Controller:** The sync system (Editor scripts + FileSystemWatcher)

---

## 1. Core Philosophy

This system is **not** a runtime scene compiler. It is a **Bi-directional Synchronization System** that keeps two representations of the same data in perfect agreement at all times during development.

The goal is not to bypass Unity's systems — it is to ensure the JSON and the Unity Scene are **perfect mirrors** of each other during development. Unity's native scene objects, prefab system, baked lighting, and build pipeline are preserved intact. The JSON layer is a development-time overlay, invisible to the shipped game.

This is an AI-first workflow. Because the JSON is plain text, AI tools can read, reason about, and modify scene data without needing a running Unity process. Because the Unity Scene is kept in perfect sync, the developer always sees an accurate visual representation of whatever the AI has written.

---

## 2. The Invariant

> At any point in Editor Mode, the state of every `GameObject` in the scene is exactly the data described by its corresponding JSON entity file, and vice versa.

This invariant must be maintained automatically. Developers and AI tools should never have to manually trigger a sync; the system enforces the invariant continuously.

---

## 3. Object Identity: GUIDs

For bidirectional sync to work, Unity and the JSON must share a stable identity system. You cannot rely on:
- Object names (non-unique, mutable)
- Sibling index (changes on reorder)
- Unity's built-in `GetInstanceID` (non-persistent across sessions)

**Every entity in the JSON must have a UUID (GUID v4).** Every corresponding `GameObject` in the Unity Scene must carry a small `DataLink` component storing that exact same UUID. The UUID is the single shared key that lets either side locate its counterpart.

```csharp
// DataLink: the shared key between JSON and Unity
public class DataLink : MonoBehaviour
{
    [HideInInspector] public string uuid;
}
```

UUID v4 is used because it is:
- Universally unique without coordination
- Readable in JSON and version control diffs
- Supported natively in C# (`System.Guid.NewGuid()`) and Node.js (`crypto.randomUUID()`)

---

## 4. Editor Mode: The Bidirectional Sync Hub

Editor Mode is where all sync activity occurs. The system operates as a continuous synchronizer, not a one-time importer.

### 4.1 Flow A — JSON → Unity (AI or External Edit)

When an AI tool, text editor, or version control operation modifies entity JSON:

1. A `FileSystemWatcher` on the `Entities/` directory detects the file change.
2. The changed file is parsed. The `uuid` field is extracted.
3. The scene is searched for a `GameObject` with a `DataLink` component matching that UUID.
4. If found: the `GameObject`'s transform and component data are updated in-place to reflect the new JSON values.
5. If not found (new UUID): the specified prefab is instantiated, `DataLink` is attached with the UUID, and the object is placed in the hierarchy.
6. If a UUID that was previously present is now absent from the `Entities/` directory (file deleted): the corresponding `GameObject` is destroyed.

**This flow is automatic.** No developer action required.

### 4.2 Flow B — Unity → JSON (Human Edit)

When a developer moves an object, tweaks a light intensity, or modifies any Inspector value:

1. Unity's `ObjectChangeEvents.changesPublished` callback fires with the modified object list.
2. Alternatively, a scene save event via `AssetModificationProcessor` can serve as a coarser sync trigger.
3. Alternatively, a manual **"Sync to JSON"** button in the Editor toolbar provides an explicit one-shot sync.
4. For each modified `GameObject`, the system reads its `DataLink` UUID, serializes its current state (transform, MonoBehaviour fields), and overwrites the corresponding JSON entity file.
5. A **debounce timer** (≈300 ms) gates writes: only the final state when the timer expires is written. Intermediate states during drag operations are not written.
6. A **diff guard** prevents writing if the serialized output is identical to what is already on disk.

**This flow is automatic.** The developer does not need to press "save" — changes propagate to JSON continuously.

### 4.3 Conflict Resolution

If a JSON file is modified externally at the same moment a developer is editing the same object in Unity, the external file always wins. This rule ("External Writes Win") ensures the AI's output is never silently discarded.

---

## 5. Play Mode: Native Execution

Because the Unity Scene is kept in perfect sync with the JSON during Editor Mode, Play Mode is entirely transparent to this system.

**When the developer presses Play:**
- Unity runs the scene natively.
- No JSON parsing occurs.
- No bootstrapping occurs.
- The scene is already an accurate reflection of the JSON data.
- All Unity systems (physics, animation, lighting, etc.) operate on standard `GameObject`s with standard components, exactly as in any normal Unity project.

**Optional validation on Play:** A startup script may perform a quick sanity check (entity count match, UUID set comparison) between the JSON directory and the active scene, logging a warning if a desync is detected before entering Play Mode.

**The JSON write pipeline is suspended during Play Mode.** Changes made during play (enemy positions, spawned objects, etc.) do not propagate back to JSON, because Play Mode changes are intentionally ephemeral.

---

## 6. Build Mode: Standard Native Build

Because the Unity Scene contains real, saved `GameObject`s (not ephemeral objects that must be bootstrapped at runtime), the build process is entirely standard.

**Build workflow:**
1. Developer builds the game normally via Unity's Build Settings.
2. Unity packages the scene with all its `GameObject`s, components, prefab connections, baked lighting, and navigation meshes — exactly as in any normal project.
3. The raw JSON entity files are **not included** in the build. They live in `Assets/SceneData/` which is not a `Resources/` or `StreamingAssets/` folder, so Unity excludes them automatically. A build postprocessor can enforce this explicitly if needed.
4. The shipped game has no awareness that JSON files ever existed. No JSON parsing occurs at runtime.

**Result:** 100% native Unity performance. The JSON layer is a development-time tool with zero runtime cost.

---

## 7. The Sync Mechanism Summary

| Action | Direction | Mechanism | Trigger |
|---|---|---|---|
| AI generates or modifies entity | JSON → Unity | Parse file, locate `GameObject` by UUID, update transform + components | `FileSystemWatcher` `Changed`/`Created` |
| AI creates new entity | JSON → Unity | Parse file, instantiate prefab, attach `DataLink`, set UUID | `FileSystemWatcher` `Created` |
| AI deletes entity | JSON → Unity | Locate `GameObject` by UUID, `DestroyImmediate` | `FileSystemWatcher` `Deleted` |
| Human moves or edits object | Unity → JSON | Serialize `GameObject`, write UUID-named file | `ObjectChangeEvents` → debounce → disk write |
| Human creates object (drop prefab) | Unity → JSON | Attach `DataLink`, generate UUID, write new entity file | `ObjectChangeEvents` `ObjectCreated` |
| Human deletes object | Unity → JSON | Read UUID from `DataLink`, delete `[uuid].json` | `ObjectChangeEvents` `ObjectDestroyed` |
| Playtesting | N/A | Unity runs native scene; JSON parsing entirely bypassed | Play button |
| Version control revert | JSON → Unity | Git modifies files on disk; `FileSystemWatcher` snaps scene to reverted state | `FileSystemWatcher` |

---

## 8. UUID Generation Rules

Two generation paths:

| Initiator | Path | Mechanism |
|---|---|---|
| **AI tool** | Direct file write | AI generates a fresh UUID v4, writes the complete entity file, then uses that UUID in any subsequent cross-referencing files. |
| **Human (drag-drop, Ctrl+D)** | `AssetPostprocessor` or scene creation hook | `System.Guid.NewGuid()` called automatically; UUID injected into file and `DataLink` component. |

AI tools must **never fabricate or guess UUID strings**. Always generate a fresh UUID v4 before writing a file.

---

## 9. What This System Is Not

- **Not a runtime scene loader.** JSON files are not read at runtime.
- **Not a replacement for Unity's prefab system.** Prefabs are still used as the source for all entity types.
- **Not a replacement for Unity's native serialization.** Built-in components (MeshRenderer, Rigidbody, Collider, etc.) are not serialized to JSON — they are defined in the prefab and propagated normally.
- **Not a build dependency.** The JSON files have zero presence in shipped builds.
- **Not an Addressables replacement.** Asset loading, bundles, and memory management are untouched.

---

## 10. Why This Architecture

| Property | Benefit |
|---|---|
| JSON as a readable Model | AI tools can read, diff, and write scene data without a running Unity process |
| Unity Scene as the View | Developers see accurate visual feedback instantly; all Unity tooling works normally |
| Per-entity files | Git diffs are per-object; no merge conflicts from a monolithic scene file |
| UUID identity | Stable cross-session, cross-machine object identity without relying on fragile name or index matching |
| Editor-only sync machinery | Zero runtime cost; sync code is stripped from builds entirely |
| Standard Unity build | Baked lighting, physics baking, prefab connections, LOD groups — all function normally |
