Read the scene at the path provided in $ARGUMENTS (ask for it if not provided).

Steps:
1. Read `$ARGUMENTS/manifest.json` and note the `sceneName`
2. List all files in `$ARGUMENTS/Entities/` 
3. Read every entity JSON file
4. Build and display a parent-child hierarchy tree using `parentUuid` relationships. Root-level entities (no `parentUuid`) are at the top level; indent children beneath their parent.
5. For each entity show: name, prefabPath (shortened), and UUID (first 8 chars)
6. After the tree, print a summary:
   - Total entity count
   - Unique prefab types used
   - Any entities with `customData` (list their types)
   - Any cross-entity references (`EntityReference` fields) found in customData
7. Flag any issues: missing parent UUIDs, duplicate UUIDs, malformed files
