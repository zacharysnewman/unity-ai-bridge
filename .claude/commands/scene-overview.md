Read all entity files in the scene at the path given in $ARGUMENTS (e.g. `Assets/SceneData/Level_01`).

Steps:
1. List every file in `$ARGUMENTS/Entities/` — these are the entities.
2. Parse each JSON file and collect: uuid, name, prefabPath, parentUuid.
3. Build a hierarchy tree: group children under their parents by matching parentUuid → uuid.
4. Print the tree with indentation showing parent-child relationships. Show `name (prefabPath)` for each node.
5. Print a summary line: total entity count, how many are root-level vs parented.
6. If any entity has a parentUuid that doesn't match any known uuid, flag it as a broken reference.

If no path is given in $ARGUMENTS, look for a SceneDataManager by checking if any `manifest.json` exists under `Assets/SceneData/` and use the first one found.
