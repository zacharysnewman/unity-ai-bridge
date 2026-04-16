---
name: new-entity
description: Create a new entity JSON file in the scene at the path given in $ARGUMENTS (e.g. `Assets/SceneData/Level_01`).
---

Create a new entity JSON file in the scene at the path given in $ARGUMENTS (e.g. `Assets/SceneData/Level_01`).

Steps:
1. Generate a UUID using the system UUID generator — never fabricate one:
   ```bash
   uuidgen | tr '[:upper:]' '[:lower:]'
   ```
2. Ask the user (or infer from context) for:
   - `name` — display name for the hierarchy
   - `prefabPath` — project-relative prefab path (e.g. `Assets/Prefabs/Rock.prefab`) or a primitive (`primitive/Cube`, `primitive/Sphere`, etc.)
   - `parentUuid` — UUID of the parent entity, or null for root-level
   - `transform.pos` — position as [x, y, z], default [0, 0, 0]
   - `transform.rot` — Euler rotation as [x, y, z], default [0, 0, 0]
   - `transform.scl` — scale as [x, y, z], default [1, 1, 1]
3. If `parentUuid` is provided, confirm that a file `$ARGUMENTS/Entities/<parentUuid>.json` exists before proceeding.
4. Write the file to `$ARGUMENTS/Entities/<uuid>.json` with this exact structure:

```json
{
  "uuid": "<generated-uuid>",
  "name": "<name>",
  "prefabPath": "<prefabPath>",
  "parentUuid": <null or "parent-uuid">,
  "transform": {
    "pos": [x, y, z],
    "rot": [x, y, z],
    "scl": [x, y, z]
  }
}
```

5. Report the UUID back so it can be referenced by other entities.

Note: Unity will detect the new file via FileSystemWatcher and instantiate the entity automatically — no button press needed.
