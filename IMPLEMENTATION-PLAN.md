# Implementation Plan

---

## Phase 1 — Sidecar Index

Update the C# sync pipeline to generate and maintain `index.ndjson`. No changes to entity file format. No changes to the FileSystemWatcher or reconciliation logic.

### Index format

Two line types per entity, UUID on every line:

```
{"uuid":"0eb2de67-...","name":"GravityLauncher_32_2","prefab":"primitive/Cube","parent":"4b0f504e-...","siblingIndex":2}
{"uuid":"0eb2de67-...","component":"ZacharysNewman.PPC.GravityLauncher"}
```

Entities separated by a blank line. Each line independently parseable.

### C# changes

- Add `IndexWriter` — responsible for all reads/writes to `index.ndjson`
- On entity created → append lines for that UUID
- On entity updated → find and replace lines for that UUID
- On entity deleted → remove all lines for that UUID
- On scene load → regenerate full index from Entities directory (self-healing)
- Exclude `index.ndjson` from FileSystemWatcher entity sync — it is write-only from C#

### Acceptance criteria

- `index.ndjson` exists and is current after any entity create/update/delete
- Grepping the index by name, prefab, parent, or component type returns correct UUID-anchored results
- Full regeneration on scene load produces identical output to incremental updates
- Index stays excluded from the entity sync pipeline

---

## Phase 2 — Selection Sync

Extend the sync pipeline with bidirectional `selection.json` support. Builds on Phase 1 — both changes touch the same pipeline so they ship together or in immediate sequence.

### File format

```json
["0eb2de67-...", "4b0f504e-..."]
```

Empty array when nothing is selected.

### C# changes

- Hook `Selection.selectionChanged` → write UUID array of selected entities to `selection.json`
- Watch `selection.json` via FileSystemWatcher → resolve UUIDs to GameObjects → call `Selection.objects`
- Only write UUIDs of entities tracked by the sync pipeline — non-entity GameObjects are ignored
- Exclude `selection.json` from entity sync pipeline (not an entity file)

### Acceptance criteria

- Selecting objects in Unity updates `selection.json` within 300ms
- Writing a UUID array to `selection.json` updates the Unity editor selection within 300ms
- An empty array clears the selection
- Non-entity GameObjects selected in Unity do not appear in `selection.json`
- `selection.json` is not treated as an entity file by the sync pipeline

---

## Phase 3 — Query Scene CLI

A CLI script that performs value-based filtering against entity files. Operates independently of Unity — reads files directly.

### Behavior

- Loads `index.ndjson` to find candidate UUIDs matching the filter
- Opens only the matched entity files for detail (not all entity files)
- Returns matched entity data as JSON lines
- Hard internal result cap — cannot be overridden by the caller

### Filter operators

`==`, `!=`, `>=`, `<=`, `>`, `<`, `contains`, `AND`, `OR`

### Examples

```bash
query-scene Level_A "component == GravityLauncher AND strength >= 5000"
query-scene Level_A "pos_y <= 0"
query-scene Level_A "prefab contains HeavyDoor AND parent == null"
```

### Acceptance criteria

- Returns only entities matching the filter expression
- Never opens more entity files than the result cap
- Works with Unity closed
- Compound expressions with AND/OR evaluate correctly
- Unknown fields return no results, not an error

---

## Phase 4 — Query Logs CLI

A CLI script that reads and filters the Unity log file. Operates independently of Unity — reads the log file directly.

### Behavior

- `type` is required — no raw dump possible
- Optional substring filter narrows results further
- Hard internal result cap — cannot be overridden

### Log types

`Error`, `Warning`, `Log`, `Exception`, `Assert`

### Examples

```bash
query-logs Error
query-logs Warning "NullReference"
query-logs Error limit:10
```

### Acceptance criteria

- Refuses to run without a `type` argument
- Returns only log entries matching the specified type
- Optional filter correctly narrows results
- Works with Unity closed as long as the log file exists
- Result cap enforced regardless of `limit` value passed

---

## Phase 5 — Get Selection / Select Objects CLI

Two CLI scripts backed by `selection.json`. Requires Phase 2.

### Get Selection

Reads `selection.json` and returns the UUID array. No entity data — UUIDs only.

```bash
get-selection
# returns: ["0eb2de67-...", "4b0f504e-..."]
```

### Select Objects

Writes a UUID array to `selection.json`. Unity picks it up within ~300ms.

```bash
select-objects "0eb2de67-..." "4b0f504e-..."
```

### Acceptance criteria

- `get-selection` returns current selection as UUID array
- `select-objects` updates Unity editor selection within 300ms
- `select-objects` with no arguments clears the selection
- Both tools fail with a clear message if `selection.json` does not exist (Unity not initialized)
