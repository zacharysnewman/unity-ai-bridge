# Query Optimization

## The Core Asymmetry

The file-per-entity layout is optimized for **writes**:
- One file touched per change → FileSystemWatcher fires exactly once
- Atomic per-entity — no merge conflicts across entities
- Diffable, git-friendly

But it is structurally hostile to **reads**:
- Every scene-wide query requires scanning all N files
- No index → no targeted lookup
- O(n) file I/O regardless of what you're looking for

With 500 entities this is slow. With 5,000 it is unusable.

---

## Why Grepping Entity Files Directly Doesn't Work

`grep` across N entity files is still O(n) — it's just fast O(n). At scale:
- Shell tools hit `ARG_MAX` limits (observed at 578 files)
- Results from pretty-printed multiline JSON often separate the UUID and matched field onto different lines — you get the value but not the identity, or vice versa
- Token cost to the AI grows linearly with result count

Grep against entity files is a scanner, not a substitute for an index. Grep against the index is the solution.

---

## The Solution: Sidecar Index

A sidecar `index.ndjson` lives alongside the Entities directory, maintained by the same C# pipeline that handles entity writes. The per-file entity format is unchanged — it remains the source of truth for Unity sync.

```
Assets/SceneData/Level_A/
├── manifest.json
├── index.ndjson          ← sidecar: maintained automatically, optimized for reads
└── Entities/
    ├── <uuid>.json       ← source of truth for Unity sync (unchanged)
    └── <uuid>.json
```

### What "Sidecar" Means

The index is a byproduct of normal entity operations — you never create, edit, or delete it manually. It just appears and stays current. Writing entity files works exactly as before; the index is an invisible side-effect.

The three operations that keep it in sync:

| Entity operation | Index operation |
|---|---|
| Entity created | Append its lines to `index.ndjson` |
| Entity updated | Find and replace its lines by UUID |
| Entity deleted | Remove all lines matching its UUID |

On scene load the index is fully regenerated from the Entities directory. This means it self-heals from any drift caused by manual file edits, git operations, or anything else that bypasses the normal pipeline — the index is always recoverable from the entity files.

The index is:
- **Derived** — generated from entity files, never edited directly
- **Updated incrementally** on any entity create/update/delete as a side-effect of the sync pipeline
- **Regenerated in full** on scene load to handle any drift

---

## Index Format: Line-Per-Concern, UUID-Anchored

The index is a **membership and type registry** — it records what exists and what type it is. Transforms, component field values, and all other detail stay in the entity file.

Two line types only:

```
{"uuid":"0eb2de67-...","name":"GravityLauncher_32_2","prefab":"primitive/Cube","parent":"4b0f504e-...","siblingIndex":2}
{"uuid":"0eb2de67-...","component":"ZacharysNewman.PPC.GravityLauncher"}
```

Every line carries the UUID so any grep result is self-contained. Lines are grouped by entity and separated by a blank line for human readability, but each line is independently parseable.

### Why Line-Per-Concern

If every entity were a single line, a grep result would return all data at once — identity and every component type. Separating concerns means:

- A component grep returns only the component line — no identity noise
- A name grep returns only the identity line — no component noise
- A UUID grep returns all lines for that entity when you want the full picture
- Each component line stays compact regardless of how many components an entity has

### Proposed Patterns (Evaluated)

The following patterns were considered during design:

**1. Anchored JSON Lines** — the approach adopted above. Each line is a small JSON object scoped to one concern, UUID on every line. Grep results are always self-contained.

**2. Flattening Transform Arrays** — proposed turning `pos:[x,y,z]` into `pos_x`, `pos_y`, `pos_z` keys for exact coordinate grepping. Not adopted: transforms are not in the index at all. Spatial queries belong to the query tool, which reads entity files directly.

**3. Header and Body with `-A` flag** — proposed using fixed line order so `-A 1` after a name match also returns the transform line. Not adopted: fragile — depends on line position staying stable, breaks if any line is inserted or removed.

**4. Elevating customData** — proposed moving component entries out of the nested `customData` array and onto top-level lines. Adopted in part: each component becomes its own UUID-anchored line, but field values are not included — type name only.

---

## The Three-Layer Query Model

### Layer 1 — Grep the Index
Identity lookups, type queries, and membership checks. Single file, one pass, O(1).

| Query | Command |
|---|---|
| Find entity by name | `grep '"name":"Door' index.ndjson` |
| Find by component type | `grep "GravityLauncher" index.ndjson` |
| Find by prefab | `grep "HeavyDoor.prefab" index.ndjson` |
| Find children of a parent | `grep "<parent-uuid>" index.ndjson` |
| Get all lines for one entity | `grep "<uuid>" index.ndjson` |

### Layer 2 — Query Tool
Value-based filtering and compound conditions — spatial queries, component field values, anything requiring comparison operators. Reads entity files directly. See Tool Proposals below.

### Layer 3 — Read Entity File
When you need transforms or component field values for a specific entity already identified via Layer 1 or 2.

---

## What the Index Does NOT Contain

- Transforms (`pos`, `rot`, `scl`) — spatial queries belong to the query tool
- Component field values (`strength`, `speed`, etc.) — detail queries belong to the query tool or entity file
- Anything not needed to answer "what exists and what type is it"

The entity file remains the write target and source of truth. Never write to the index directly.

---

## Implementation Notes

- Index writer is a side-effect of the normal entity sync pipeline in C#
- On entity create/update: rewrite all lines for that UUID in `index.ndjson`
- On entity delete: remove all lines for that UUID from `index.ndjson`
- On scene load: regenerate the full index from scratch
- `index.ndjson` does not need a `.meta` file and should be excluded from Unity asset import
- `index.ndjson` is not watched by FileSystemWatcher — it is write-only from C#
- Cross-scene indexing is out of scope — one index per scene is sufficient

---

## Tool Proposals

See [TOOLS-SPEC.md](TOOLS-SPEC.md).
