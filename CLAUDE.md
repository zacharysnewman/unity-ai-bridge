<!-- PACKAGE-ONLY-BEGIN -->
> **Package development note:** After modifying any file in `Tools/`, run `/install-unity-ai-bridge` to propagate changes to the Unity project root. The project's `Tools/` copies are what actually get used.
<!-- PACKAGE-ONLY-END -->

# Unity AI Bridge — Claude Code Guide

This package implements a **bidirectional sync** between JSON files on disk and a Unity scene.
Both representations are always kept in perfect sync — changes can originate from either side.
Unity hot-reloads any JSON file change instantly via FileSystemWatcher.
Any change made in the Unity Editor is automatically written back to the corresponding JSON file.
On conflict (simultaneous edits to both sides), the JSON file wins.

---

## Directory Layout

Each scene maps to a directory you configure in the `SceneDataManager` component:

```
Assets/SceneData/Level_01/
├── manifest.json            # Scene metadata — do not modify schemaVersion
└── Entities/
    ├── <uuid>.json          # One file per entity; filename == uuid field exactly
    └── <uuid>.json
```

---

## CLI Tools

Nine CLI tools are installed in `Tools/` at the project root. Prefer these over manually reading files — they are optimized for their tasks and enforce result caps.

| Tool | When to use |
|---|---|
| `Tools/query-scene <scene> "<filter>"` | Find entities matching field criteria without opening every file |
| `Tools/query-logs <type> [substring]` | Read Unity Editor.log filtered by type |
| `Tools/get-selected-entities [scene]` | Get UUIDs of currently selected entities |
| `Tools/select-entities [scene] <uuid>...` | Set the Unity Editor selection by UUID |
| `Tools/get-scene-path [scene]` | Get the active scene asset path |
| `Tools/get-camera [scene]` | Get scene view camera position and rotation |
| `Tools/get-visible-entities [scene]` | Get UUIDs of entities visible in the scene view frustum |
| `Tools/patch-entities <scene> "<filter>" "<patch>"` | Batch-apply a field mutation to all matching entities |
| `Tools/create-entities <scene> '<spec-json>'` | Create new entities and return their UUIDs |
| `Tools/delete-entities <scene> <uuid>...` | Delete entities by UUID |

### query-scene

```bash
Tools/query-scene Level_A "component == GravityWell AND strength >= 1000"
Tools/query-scene Level_A "transform.pos.y <= 0"
Tools/query-scene Level_A "prefab contains HeavyDoor AND parent == null"
```

The `<scene>` argument is the scene directory name — the path relative to `Assets/SceneData/` (e.g. `Level_A` or `Scenes/Level_A`). Matched by directory basename, so the bare scene name works regardless of nesting.

Prints one matching UUID per line. Read individual entity files for full details.

Filter operators: `==  !=  >=  <=  >  <  contains` — modulo: `field % divisor op value` — logical: `AND  OR`

Index-only fields (fast, no entity files opened): `name`, `prefab`, `parent`, `component`, `siblingIndex`
Entity-file fields: `transform.pos.x/y/z`, `transform.rot.x/y/z`, `transform.scl.x/y/z`, any `customData` field name

### query-logs

```bash
Tools/query-logs Error
Tools/query-logs Warning "NullReference"
Tools/query-logs Log "[UnityAIBridge]"
```

Types: `Error  Warning  Log  Exception  Assert` — type argument is required.

### get-selected-entities / select-entities

```bash
Tools/get-selected-entities                          # returns UUID array for current selection
Tools/get-selected-entities Level_A                  # scoped to a specific scene
Tools/select-entities <uuid> <uuid> ...      # select entities by UUID
Tools/select-entities Level_A <uuid> ...     # scoped to a specific scene
Tools/select-entities                        # clear selection
Tools/select-entities --stdin                # read UUIDs from stdin (JSON array or one per line)
```

`--stdin` makes `select-entities` composable with any UUID-producing tool:

```bash
Tools/get-visible-entities | Tools/select-entities --stdin
Tools/query-scene Level_A "component == Enemy" | Tools/select-entities --stdin
```

### get-scene-path / get-camera / get-visible-entities

```bash
Tools/get-scene-path                         # returns active scene asset path
Tools/get-camera                             # returns scene view camera pos and rot
Tools/get-visible-entities                    # returns UUIDs visible in the frustum
Tools/get-camera Level_A                     # scoped to a specific scene
```

### patch-entities

```bash
patch-entities Level_A "prefab contains Cube" "transform.pos.y += 2"
patch-entities Level_A "component == Enemy" "transform.rot.y = 0"
patch-entities Level_A "name contains Wall" "transform.scl.x *= 2"
patch-entities Level_A "transform.pos.y < 0" "transform.pos.y = 0"
```

Patch operators: `=  +=  -=  *=  /=  %=`

Field paths are the same as `query-scene`. Composes with `--stdin`:

```bash
query-scene Level_A "prefab contains Cube" | patch-entities --stdin Level_A "transform.pos.y += 2"
```

Every patch is automatically recorded in `patch-history.json` (last 10 entries). Use `--history` to review recent changes and `--undo` to revert the most recent patch — works regardless of current selection or camera position:

```bash
patch-entities Level_A --history   # one-line summary per recorded patch
patch-entities Level_A --undo      # revert the most recent patch
```

The `[scene]` argument is the scene directory name — the path relative to `Assets/SceneData/` (e.g. `Level_A` or `Scenes/Level_A`). Matched by directory basename, so the bare scene name works regardless of nesting.

---

## Reading the Current Scene

To understand what's in a scene before making changes, prefer `Tools/query-scene` for filtered lookups, or `/scene-overview` for a full hierarchy view:

```bash
Tools/query-scene Level_01 "name == MyObject"
```

Use `/scene-overview Assets/SceneData/Level_01` for a formatted hierarchy view of all entities.

To find entities by criteria, use `query-scene` — it returns UUIDs. To then inspect a specific entity, read its file directly — no additional tool needed:

```
Assets/SceneData/<SceneName>/Entities/<uuid>.json
```

---

## Making Scene Changes

JSON files are the mechanism for all scene edits. Every file you create, modify, or delete is reflected in Unity instantly — no manual sync, save, or button press is needed.

### Step 1 — Read first

Never modify the scene blind. Start by reading what's already there:

```bash
/scene-overview Assets/SceneData/Level_01
```

Or read individual entity files to understand their current state before editing them.

### Step 2 — Map intent to file operations

| What you want to do | File operation |
|---|---|
| Add a new object | Create `Entities/<new-uuid>.json` |
| Move / rotate / scale an object | Edit `transform.pos`, `transform.rot`, or `transform.scl` |
| Rename an object | Edit `name` |
| Enable / disable an object | Edit `activeSelf` |
| Change the tag | Edit `tag` (must be a tag defined in Project Settings → Tags and Layers) |
| Change the layer | Edit `layer` (layer name string, e.g. `"Default"`, `"UI"`) |
| Mark as static / non-static | Edit `isStatic` (bool — sets or clears all static editor flags) |
| Reparent an object | Edit `parentUuid` to the new parent's UUID |
| Detach from parent (make root-level) | Set `parentUuid` to `null` |
| Change a built-in component value | Edit the relevant field inside `builtInComponents` (use Unity's internal property names, e.g. `m_Size`, `m_IsTrigger`) |
| Change a component value | Edit the relevant field inside `customData` |
| Add a component to an object | Add a new entry to `customData` with the correct `type` and field values |
| Remove a component from an object | Delete the corresponding entry from the `customData` array — the `customData` array is treated as the complete truth; any MonoBehaviour absent from it will be destroyed on the GameObject |
| Delete an object | Delete `Entities/<uuid>.json` — child entities are removed automatically |
| Duplicate an object | Create a new file with a fresh UUID; copy all other fields |
| Change which prefab an object uses | Edit `prefabPath` — Unity will destroy and reinstantiate the object |
| Reorder siblings | Edit `siblingIndex` (zero-based index among siblings under the same parent or scene root) |

### Step 3 — Execute in dependency order

When making multiple changes, order matters:

1. **Create parents before children** — a child's `parentUuid` must refer to an already-existing file
2. **Create referenced entities before referencing ones** — any UUID used in `EntityReference` fields must exist on disk first
3. **Delete children before parents** — though deleting a parent automatically removes children, deleting explicitly is clearer

### When not to edit JSON

- **During Play Mode** — the write pipeline is suspended; changes will be overwritten when Play Mode exits
- **To change built-in component values** (Rigidbody, Collider, MeshRenderer, etc.) — these come from the prefab and are not in the JSON

---

## Entity File Format

```json
{
  "uuid": "550e8400-e29b-41d4-a716-446655440000",
  "name": "HeavyDoor_01",
  "prefabPath": "Assets/Prefabs/Environment/HeavyDoor.prefab",
  "parentUuid": null,
  "transform": {
    "pos": [10.5, 0.0, -2.2],
    "rot": [0.0, 90.0, 0.0],
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
| `name` | Yes | Display name in the Unity hierarchy. |
| `prefabPath` | Yes | Project-relative prefab path, or a primitive identifier (see below). |
| `parentUuid` | No | UUID of the parent entity. Omit or `null` for root-level objects. |
| `transform.pos` | Yes | Local-space position (meters) when parented; world-space at root. |
| `transform.rot` | Yes | Local-space Euler angles (degrees), same values shown in Unity Inspector. |
| `transform.scl` | Yes | Local scale (always local). |
| `tag` | No | Unity tag string. Must be defined in Project Settings → Tags and Layers. |
| `layer` | No | Layer name string (e.g. `"Default"`, `"UI"`). Must be defined in Project Settings. |
| `isStatic` | No | `true` enables all static editor flags; `false` clears them. |
| `activeSelf` | No | Whether the object is active. Maps to `GameObject.SetActive()`. |
| `customData` | No | Array of serialized MonoBehaviour entries (see below). |

---

## UUID Rules

- **Never fabricate or guess a UUID** — always generate one by running the system UUID generator via Bash:
  ```bash
  uuidgen | tr '[:upper:]' '[:lower:]'
  ```
- The filename must equal the `uuid` field value exactly (without `.json`)
- **Never reuse or modify an existing entity's UUID**
- When creating entity A that will be referenced by entity B: create A first, record its UUID, then create B

---

## Primitive Objects

Use reserved `prefabPath` values instead of a real asset path:

| Value | Unity Primitive |
|---|---|
| `"primitive/Cube"` | Cube |
| `"primitive/Sphere"` | Sphere |
| `"primitive/Cylinder"` | Cylinder |
| `"primitive/Capsule"` | Capsule |
| `"primitive/Plane"` | Plane |
| `"primitive/Quad"` | Quad |

---

## Parenting

Set `parentUuid` to the parent's UUID. The parent entity file must exist before the child references it.

**Pattern for creating a parent-child group:**
1. Create the parent entity file → note its UUID
2. Create child entity files with `"parentUuid": "<parent-uuid>"`

---

## Custom MonoBehaviour Data (`customData`)

Only custom `MonoBehaviour` components are serialized. Built-in Unity components (Rigidbody, BoxCollider, MeshRenderer, etc.) come from the prefab and are never written here.

- `type` is the fully-qualified C# class name (e.g. `"MyGame.Environment.DoorScript"`, not `"DoorScript"`)
- All `public` fields and `[SerializeField]` private fields are included
- Multiple components of the same type are supported — Nth entry maps to Nth component instance
- **The array is treated as the complete truth.** Any custom MonoBehaviour present on the GameObject but absent from `customData` will be destroyed when the entity is synced. To remove a component, delete its entry from the array. To add one, append a new entry.

---

## Cross-Entity References

Direct `GameObject` references cannot survive JSON round-trips. Use `EntityReference` instead:

**In the JSON:**
```json
"triggerTarget": { "targetUUID": "8a7b6c5d-..." }
```

**In C#:**
```csharp
[Serializable]
public struct EntityReference { public string targetUUID; }
public EntityReference triggerTarget;

// Resolve at runtime:
var go = SceneDataManager.Instance.GetByUUID(triggerTarget.targetUUID);
```

---

## Creating an Entity

Write a correctly formatted JSON file to `Entities/<uuid>.json`.
Unity detects the new file via FileSystemWatcher and instantiates the entity automatically — no button press needed.

Use `/new-entity` for guided creation with UUID generation.

---

## Updating an Entity

Edit the JSON file. Unity hot-reloads the change automatically. The diff guard prevents unnecessary disk writes if content is unchanged.

---

## Deleting an Entity

Delete the JSON file. Unity destroys the corresponding `GameObject` automatically.

---

## Triggering Validation

To validate the scene (checks prefab paths, parent UUIDs, component type resolution), use `Unity AI Bridge → Validate Scene` from the Unity menu.

---

## VS Code JSON Validation

To enable inline schema validation for entity and manifest files, add this to your Unity project's `.vscode/settings.json`:

```json
{
  "json.schemas": [
    {
      "fileMatch": ["**/Entities/*.json"],
      "url": "./Packages/com.zacharysnewman.unity-ai-bridge/Schemas/entity.schema.json"
    },
    {
      "fileMatch": ["**/SceneData/**/manifest.json"],
      "url": "./Packages/com.zacharysnewman.unity-ai-bridge/Schemas/manifest.schema.json"
    }
  ]
}
```

---

## Editing JSON Safely

### Always anchor edits to the field name

Entity files contain multiple `[x, y, z]` arrays (`pos`, `rot`, `scl`) that can have identical values. When editing, always include the field name in the match context — never match bare numeric lines alone.

**Wrong** — matches the first `0.0` triplet it finds (could be `rot` instead of `pos`):
```
      0.0,
      0.0,
      0.0
```

**Correct** — anchored to the field name, unambiguous:
```json
"pos": [
      4.0,
      0.0,
      0.0
    ],
```

### Transform array layout

`pos`, `rot`, and `scl` are always `[x, y, z]`:
- Index 0 = X
- Index 1 = Y (up)
- Index 2 = Z

See `Schemas/entity.schema.json` for the full entity schema, including transform, customData, and UUID format.

---

## Known Limitations

- **Reparenting lag**: When `parentUuid` is changed in JSON, the hierarchy tree only visually rebuilds when the Unity editor has focus. The actual transform parent is updated immediately — clicking into Unity will show the correct state. This is a Unity editor windowing constraint and cannot be resolved via API.

---

## What NOT to Do

- Do not modify `schemaVersion` in `manifest.json`
- Do not set `hideFlags` on entity GameObjects — entities are normal persistent scene objects
- Do not write entity files during Play Mode — the write pipeline is suspended
- Do not use direct `GameObject` or `MonoBehaviour` references in serialized fields — use `EntityReference`
- Do not assume component order is stable across sessions for same-type multi-components
