---
name: scene-overview
description: Read the scene at the path provided in $ARGUMENTS (ask for it if not provided).
---

Read the scene at the path provided in $ARGUMENTS (ask for it if not provided).

Steps:
1. Read `$ARGUMENTS/manifest.json` and note the `sceneName`.

2. **If `$ARGUMENTS/index.ndjson` exists**, use it as the primary data source — it contains all
   entity names, prefabs, parents, sibling indices, and component types without opening entity files:
   - Parse each line as a JSON object.
   - Info lines have `"name"`, `"prefab"`, `"parent"`, `"siblingIndex"` fields.
   - Component lines have a `"component"` field.
   - Build the hierarchy and summary entirely from the index.

   **If `index.ndjson` does not exist**, fall back to reading every file in `$ARGUMENTS/Entities/`.

3. Build and display a parent-child hierarchy tree using `parentUuid` / `"parent"` relationships.
   Root-level entities (parent is null) are at the top level; indent children beneath their parent,
   sorted by `siblingIndex`. For each entity show: name, prefab (shortened), and UUID (first 8 chars).

4. After the tree, print a summary:
   - Total entity count
   - Unique prefab types used
   - Any entities with components (list name → component types)
   - Any cross-entity references (`EntityReference` fields) found in customData

5. Flag any issues: missing parent UUIDs, duplicate UUIDs, malformed files.
