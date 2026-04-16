# Tools Spec

Proposed tools to be implemented as part of this package. Each tool is designed with a narrow contract — focused inputs, bounded outputs, no raw dumps — to prevent context overload.

---

## Implementation Architecture

All tools are **file-based** — no HTTP server, no MCP server, no persistent process. This is a direct extension of the existing JSON sync pattern already used for entity files.

### Two implementation surfaces

**CLI scripts** — invoked on demand by Claude via the Bash tool, exit when done. No lifecycle to manage.
- Query Scene — reads `index.ndjson` and entity files directly
- Query Logs — reads the Unity log file directly
- Get Selection — reads `selection.json`
- Select Objects — writes `selection.json`

**Unity Editor C#** — extends the existing sync pipeline with two new behaviors:
- Watch `selection.json` for external writes → update `Selection.objects`
- Hook `Selection.selectionChanged` → write `selection.json`

### File layout

```
Assets/SceneData/Level_A/
├── manifest.json
├── index.ndjson          ← maintained by sync pipeline
├── selection.json        ← bidirectional selection sync
└── Entities/
    └── <uuid>.json
```

### Bidirectional selection sync

The same pattern used for entity files applies to selection:

| Direction | Trigger | Action |
|---|---|---|
| Unity → file | `Selection.selectionChanged` fires | Write UUID array to `selection.json` |
| File → Unity | FileSystemWatcher detects write to `selection.json` | Call `Selection.objects` with resolved GameObjects |

`selection.json` is always current. Claude reads it to get selection, writes it to set selection. No request/response cycle, no open connection.

---

## Query Scene

Value-based filtering and compound conditions against entity files that grep can't express. Handles spatial queries, component field values, and compound conditions.

**Implementation:** CLI script. Reads entity files directly — no Unity connection required.

**Inputs:**
- `scene` — scene name or path
- `filter` — expression string
- `limit` — optional, max results to return; capped at a hard internal maximum that cannot be overridden

**Supported operators:** `==`, `!=`, `>=`, `<=`, `>`, `<`, `contains`, `AND`, `OR`

**Design constraint:** Results are always bounded by a hard internal cap regardless of filter breadth — consistent with the bounded-output principle across all tools.

**Examples:**
```bash
query-scene Level_A "component == GravityLauncher AND strength >= 5000"
query-scene Level_A "pos_y <= 0"
query-scene Level_A "prefab contains HeavyDoor AND parent == null"
```

---

## Query Logs

Returns Unity console log entries filtered by type. No raw log dump is possible — a type filter is always required and results are capped at a hard internal limit that cannot be overridden.

**Implementation:** CLI script. Reads the Unity log file directly — no Unity connection required.

**Inputs:**
- `type` — required, one of: `Error`, `Warning`, `Log`, `Exception`, `Assert`
- `filter` — optional substring to narrow results further
- `limit` — optional, max results to return; capped at the internal maximum regardless of what is passed

**Design constraint:** There is no "return all logs" option. The required `type` parameter and the non-overridable result cap are intentional — unbounded log dumps are a primary source of context overload.

**Examples:**
```
query-logs Error
query-logs Warning "NullReference"
query-logs Error limit:10
```

---

## Get Selection

Returns the UUIDs of all currently selected entities in the Unity Editor. No entity data is returned — UUIDs only. Use the index or entity files to resolve detail after getting the selection.

**Implementation:** CLI script. Reads `selection.json` — requires Unity to be open and the sync pipeline active to reflect current state.

**Inputs:** none

**Returns:** array of UUID strings

```json
["0eb2de67-...", "4b0f504e-..."]
```

---

## Select Objects

Sets the Unity Editor selection to the specified entities.

**Implementation:** CLI script. Writes `selection.json` — Unity's FileSystemWatcher picks it up and updates the editor selection within ~300ms.

**Inputs:**
- `uuids` — array of UUID strings to select

**Behavior:**
- Replaces the current selection entirely
- UUIDs that do not match any entity in the active scene are silently ignored
- An empty array clears the selection
