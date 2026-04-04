# JSON Scenes for Unity — Claude Code Guide

This package implements a **bidirectional sync** between JSON files on disk and a Unity scene.
JSON files are the source of truth. Unity hot-reloads any file change within ~300ms via FileSystemWatcher.
Any change made in the Unity Editor is automatically written back to the corresponding JSON file.

---

## Directory Layout

Each scene maps to a directory you configure in the `SceneDataManager` component:

```
Assets/SceneData/Level_01/
├── manifest.json            # Scene metadata — do not modify schemaVersion
├── Commands/                # Short-lived command files (auto-deleted after execution)
└── Entities/
    ├── <uuid>.json          # One file per entity; filename == uuid field exactly
    └── <uuid>.json
```

---

## Reading the Current Scene

To understand what's in a scene before making changes:

```bash
ls Assets/SceneData/Level_01/Entities/
cat Assets/SceneData/Level_01/Entities/<uuid>.json
```

Use `/scene-overview Assets/SceneData/Level_01` for a formatted hierarchy view.

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
| `customData` | No | Array of serialized MonoBehaviour entries (see below). |

---

## UUID Rules

- Generate fresh UUID v4 strings: `xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx`
- The filename must equal the `uuid` field value exactly (without `.json`)
- **Never reuse or modify an existing entity's UUID**
- **Never fabricate or guess a UUID** — always generate a fresh one
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

To validate the scene (checks prefab paths, parent UUIDs, component type resolution):

Write to `Commands/validate.json`:
```json
{ "command": "validate_scene" }
```

Unity processes the command and writes the result to `Commands/validate_result.json`.
Read the result file to check `valid` and any `errors`.

Alternatively, use `JSON Scenes → Validate Scene` from the Unity menu.

---

## VS Code JSON Validation

To enable inline schema validation for entity and manifest files, add this to your Unity project's `.vscode/settings.json`:

```json
{
  "json.schemas": [
    {
      "fileMatch": ["**/Entities/*.json"],
      "url": "./Packages/com.zacharysnewman.json-scenes-for-unity/Schemas/entity.schema.json"
    },
    {
      "fileMatch": ["**/SceneData/**/manifest.json"],
      "url": "./Packages/com.zacharysnewman.json-scenes-for-unity/Schemas/manifest.schema.json"
    }
  ]
}
```

---

## What NOT to Do

- Do not modify `schemaVersion` in `manifest.json`
- Do not set `hideFlags` on entity GameObjects — entities are normal persistent scene objects
- Do not write entity files during Play Mode — the write pipeline is suspended
- Do not use direct `GameObject` or `MonoBehaviour` references in serialized fields — use `EntityReference`
- Do not assume component order is stable across sessions for same-type multi-components
