# Scene Syncing Gaps — Implementation Plan

Track progress here: mark each item `[ ]` → `[x]` as it completes.

---

## Gaps

1. **MeshFilter + Renderer support** — hardcoded excluded; asset-path serialization already works for project assets
2. **AnimationCurve serialization** — falls through to `null` in `SerializeProp`/`ApplyProp`
3. **Gradient serialization** — falls through to `null` in `SerializeProp`/`ApplyProp`
4. **ManagedReference (`[SerializeReference]`)** — falls through to `null`; needs type-discriminated encoding
5. **Direct GameObject/MonoBehaviour refs in custom scripts** — silently skipped; need UUID-based encoding
6. **`patch-entities` can't write to `builtInComponents`** — `set_entity_field` only searches `customData`

---

## Phase 1 — AnimationCurve & Gradient  
*File: `Editor/BuiltInComponentSerializer.cs`*

**AnimationCurve** — `SerializedPropertyType.AnimationCurve`

Serialize as a JSON array of keyframe objects:
```json
[{ "time": 0.0, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0, "tangentMode": 0 }, ...]
```

Deserialize: clear existing keys, add each keyframe, call `prop.animationCurveValue = curve`.

**Gradient** — `SerializedPropertyType.Gradient`

Serialize as:
```json
{
  "mode": 0,
  "colorKeys": [{ "r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0, "time": 0.0 }],
  "alphaKeys": [{ "alpha": 1.0, "time": 0.0 }]
}
```

Deserialize: reconstruct `GradientColorKey[]` and `GradientAlphaKey[]`, assign `prop.gradientValue`.

**Tasks:**
- [x] Add `SerializedPropertyType.AnimationCurve` case to `SerializeProp`
- [x] Add `SerializedPropertyType.Gradient` case to `SerializeProp`
- [x] Add `SerializedPropertyType.AnimationCurve` case to `ApplyProp`
- [x] Add `SerializedPropertyType.Gradient` case to `ApplyProp`

---

## Phase 2 — `patch-entities` builtInComponents write support
*File: `Tools/patch-entities`*

`get_entity_field` already reads from both `builtInComponents` and `customData` (used for filter evaluation). `set_entity_field` only writes to `customData`. Fix: extend the write search to include `builtInComponents`.

The `before_value` capture via `get_entity_field` (already correct) means undo continues to work automatically.

**Scope note:** only flat field values are supported (same as `customData`). Nested object patching (e.g. `m_Center.x`) is out of scope for this phase.

**Tasks:**
- [x] Update `set_entity_field` to search `builtInComponents` in addition to `customData`
- [x] Update inline docstring / help text to reflect new capability

---

## Phase 3 — MeshFilter + Renderer
*File: `Editor/BuiltInComponentSerializer.cs`*

Remove the hardcoded exclusions in `ShouldInclude`. The existing `ObjectReference` handler already serializes project assets as path strings and returns `null` for built-in Unity assets (empty path or `Resources/` prefix), which are already filtered out.

**Edge cases to handle:**
- Primitive GameObjects: the `MeshFilter.m_Mesh` references a built-in mesh → no asset path → `null` → field omitted. No conflict with `GetPrefabPath` primitive detection.
- `Renderer.m_Materials` is an array of `Material` ObjectReferences → already handled by `SerializeGeneric` array path + `ObjectReference` case.
- Sub-assets (e.g. embedded meshes): `AssetDatabase.GetAssetPath` returns the parent asset path; the sub-asset is not independently addressable. These will serialize the parent path only — noted as a known limitation.

**Tasks:**
- [x] Remove `if (component is Renderer) return false;` from `ShouldInclude`
- [x] Remove `if (component is MeshFilter) return false;` from `ShouldInclude`
- [x] Verify `ObjectReference` deserialization uses `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>` (already done) — no change needed
- [x] Verify null return for built-in assets doesn't produce spurious JSON keys — confirm `if (token != null)` guard in `Serialize` (already present on line 54)

---

## Phase 4 — ManagedReference (`[SerializeReference]`)
*File: `Editor/BuiltInComponentSerializer.cs`*

`SerializedPropertyType.ManagedReference` provides `prop.managedReferenceValue` (the boxed object) and `prop.managedReferenceFullTypename` (assembly-qualified type string).

**Serialize** as a type-discriminated envelope:
```json
{
  "__type": "MyGame.Behaviours.SpeedModifier, Assembly-CSharp",
  "speed": 3.5,
  "enabled": true
}
```
Null managed references serialize as `null`.

Recursion strategy: use `SerializeGeneric` via the existing `prop.Copy()` child iteration — no new recursion needed since `ManagedReference` properties expose children the same way as structs.

**Deserialize:**
1. Read `__type` string.
2. Resolve type: split on `,` to get type name + assembly hint; scan `AppDomain.CurrentDomain.GetAssemblies()` (same pattern as `ResolveType`).
3. Set `prop.managedReferenceValue = Activator.CreateInstance(type)` (requires a parameterless constructor — document this requirement).
4. Iterate children and call `ApplyProp` recursively (same as `ApplyGeneric` struct path).

**Known limitation:** types without a parameterless constructor cannot be deserialized. Log a warning and skip.

**Tasks:**
- [x] Add `SerializedPropertyType.ManagedReference` case to `SerializeProp`
- [x] Add `SerializedPropertyType.ManagedReference` case to `ApplyProp`
- [x] Add `ResolveManagedReferenceType` helper (parses assembly-qualified string)

---

## Phase 5 — Direct GameObject/MonoBehaviour refs in customData
*File: `Editor/SceneIO.cs`*

Currently `SerializeComponentFields` skips all `UnityEngine.Object` fields that aren't `ScriptableObject`. `DeserializeComponentFields` similarly skips them.

**Serialize side** (`SerializeComponentFields`):
- After the `ScriptableObject` check, add a check: if field type is `GameObject` or assignable from `Component`:
  - Cast value to `UnityEngine.Object`
  - Try `AssetDatabase.GetAssetPath(obj)` — if non-empty, it's an asset reference; skip (assets in customData are unsupported and uncommon)
  - Otherwise look up `EntitySync` on the referenced object to get its UUID → write `{ "targetUUID": "uuid" }`
  - If no `EntitySync` found (e.g. non-managed object), write `null` and log a warning

**Deserialize side** (`DeserializeComponentFields`):
- After `ScriptableObject` check, if field type is `GameObject` or `Component` and the token is a `JObject` with a `targetUUID` key:
  - Call `manager.GetByUUID(uuid)` to get the `GameObject`
  - If field type is `GameObject`, assign directly
  - If field type is a `Component` subtype, call `go.GetComponent(field.FieldType)` and assign
  - If UUID not yet resolved (entity not yet loaded), log a warning — no deferred pass in this phase

**Signature change:** `DeserializeComponentFields` needs access to the `SceneDataManager`. Thread the `manager` parameter through:
- `DeserializeComponentFields(MonoBehaviour, JObject)` → `DeserializeComponentFields(MonoBehaviour, JObject, SceneDataManager)`
- `ReconcileCustomData` (caller) already receives `manager` — pass it through

**Known limitation:** if entity A references entity B, and B is loaded after A in the reconciliation pass, the reference on A will be null until the next hot-reload. A future deferred-resolution pass could fix this, but it is out of scope here.

**Tasks:**
- [x] Add GameObject/Component branch to `SerializeComponentFields`
- [x] Add `manager` parameter to `DeserializeComponentFields` and update all call sites
- [x] Add GameObject/Component branch to `DeserializeComponentFields`

---

## Phase 6 — Update SPEC.md
*File: `SPEC.md`*

- Remove "not supported" entries for Renderer, MeshFilter, AnimationCurve, Gradient, ManagedReference, direct GameObject/Component refs
- Add JSON format documentation for AnimationCurve, Gradient, ManagedReference, scene-object refs
- Update `patch-entities` documentation to include `builtInComponents` field patching
- Add sub-asset limitation note for Renderer/MeshFilter
- Add parameterless-constructor requirement for ManagedReference

**Tasks:**
- [x] Update SPEC.md

---

## Plan Evaluation

**Phase 1 (AnimationCurve/Gradient):** Straightforward additions. Low risk. Both property types have clean C# API accessors (`prop.animationCurveValue`, `prop.gradientValue`).

**Phase 2 (patch-entities builtIn write):** One-line Python change in `set_entity_field`. Low risk. The read side already works; undo history works automatically since `get_entity_field` is used for `before_value` capture.

**Phase 3 (MeshFilter/Renderer):** Removing two lines. The existing ObjectReference and array serialization paths already handle what's needed. Main risk is unexpected behavior with sub-assets or generated meshes — documented as a known limitation. **Note:** `GetPrefabPath` uses `MeshFilter` directly already (line 573 of SceneIO.cs) — removing the BuiltInComponentSerializer exclusion does not affect that code path.

**Phase 4 (ManagedReference):** Most complex. The child-iteration recursion reuses existing `SerializeGeneric` patterns, which reduces risk. The main constraint (parameterless constructor) is a real limitation but matches how Unity's own serialization works. Risk: medium. Implement last.

**Phase 5 (scene-object refs):** Medium complexity. The signature change to `DeserializeComponentFields` is contained; callers are in the same file. The known ordering limitation (forward references) is acceptable since the `EntityReference` workaround already documents this pattern. Risk: medium.

**Phase 6 (SPEC.md):** Documentation only. No risk.

**Recommended order:** 1 → 2 → 3 → 4 → 5 → 6

---

## Progress Summary

| Phase | Status |
|---|---|
| 1 — AnimationCurve & Gradient | [x] Complete |
| 2 — patch-entities builtIn write | [x] Complete |
| 3 — MeshFilter + Renderer | [x] Complete |
| 4 — ManagedReference | [x] Complete |
| 5 — Scene-object refs in customData | [x] Complete |
| 6 — Update SPEC.md | [x] Complete |
