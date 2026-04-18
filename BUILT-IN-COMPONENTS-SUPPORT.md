# Built-In Component Support — Implementation Plan

## Goal

Serialize and deserialize built-in Unity components (BoxCollider, Rigidbody, etc.) in entity JSON files, using Unity's `SerializedObject`/`SerializedProperty` API — the same mechanism the Inspector uses. This enables `query-scene` to filter by built-in component properties and allows Claude to read/write those values.

---

## JSON Format

A new top-level array `builtInComponents` alongside `customData`:

```json
{
  "uuid": "...",
  "builtInComponents": [
    {
      "type": "UnityEngine.BoxCollider",
      "m_Size": [1.0, 2.0, 1.0],
      "m_Center": [0.0, 0.0, 0.0],
      "m_IsTrigger": false
    },
    {
      "type": "UnityEngine.Rigidbody",
      "m_Mass": 1.0,
      "m_Drag": 0.0,
      "m_AngularDrag": 0.05,
      "m_UseGravity": true,
      "m_IsKinematic": false
    }
  ],
  "customData": [...]
}
```

Property names use Unity's internal serialized names (`m_Size`, `m_IsTrigger`, etc.) — these are stable across Unity versions and match what `SerializedProperty.name` returns.

Kept separate from `customData` to make the distinction between built-in and custom components clear. `customData` continues to use reflection; `builtInComponents` uses `SerializedObject`.

---

## Component Inclusion Rules

Serialize all components on a GameObject **except**:

| Excluded type | Reason |
|---|---|
| `Transform` | Already handled by the `transform` field |
| `EntitySync` | Internal bridge component |
| Any `MonoBehaviour` subclass | Handled by `customData` |
| `Renderer` subtypes | Material/mesh asset references are complex; skip for now |
| `MeshFilter` | Mesh asset reference; skip for now |

Everything else — `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`, `Rigidbody`, `Rigidbody2D`, `AudioSource`, `Light`, `Camera`, etc. — is included.

---

## Property Serialization

Use `SerializedObject` to iterate top-level properties. Map `SerializedPropertyType` to JSON:

| SerializedPropertyType | JSON representation |
|---|---|
| `Integer`, `LayerMask`, `Enum`, `Character` | `number` |
| `Boolean` | `bool` |
| `Float` | `number` |
| `String` | `string` |
| `Vector2`, `Vector3`, `Vector4` | `[x, y, z]` array |
| `Quaternion` | `[x, y, z, w]` array |
| `Color` | `[r, g, b, a]` array |
| `Rect` | `{ x, y, width, height }` |
| `Bounds` | `{ center: [x,y,z], size: [x,y,z] }` |
| `ObjectReference` | asset path string via `AssetDatabase.GetAssetPath`, or `null` |
| `Generic` (nested) | recurse into child properties |
| `ArraySize` + array elements | JSON array |

**Always skip:**
- `m_Script` property (present on every component, not useful)
- Properties flagged `DontShowInInspector` or `HideInInspector`
- `isExpanded` and other editor-UI-only properties

---

## Deserialization

On JSON → Unity, for each entry in `builtInComponents`:

1. Resolve the type by name (e.g. `UnityEngine.BoxCollider`)
2. Get or add the component on the GameObject
3. Wrap in `SerializedObject`
4. For each property in the JSON: `FindProperty(name)` → set value by type → `ApplyModifiedPropertiesWithoutUndo`

`objectReference` values: resolve via `AssetDatabase.LoadAssetAtPath`. Log a warning if the asset isn't found; leave the property unchanged.

**The array is treated as the complete truth**, consistent with how `customData` works. Built-in components present on a GameObject but absent from `builtInComponents` are removed. Only components explicitly listed survive.

---

## Write Pipeline

In `SerializeEntity`, after the `customData` block:

```csharp
var builtIn = SerializeBuiltInComponents(go);
if (builtIn.Count > 0)
    root["builtInComponents"] = builtIn;
```

`SerializeBuiltInComponents` iterates `go.GetComponents<Component>()`, applies the exclusion rules, and serializes each to a `JObject`.

In `ReconcileComponents` (read pipeline), add a second pass for `builtInComponents` using the same `SerializedObject` deserialization.

---

## New File: `BuiltInComponentSerializer.cs`

Encapsulates all `SerializedObject`/`SerializedProperty` logic in a single Editor class:

```
Editor/BuiltInComponentSerializer.cs
```

Public API:
```csharp
// Serialize one component to a JObject (includes "type" key)
public static JObject Serialize(Component component);

// Apply a JObject to a component via SerializedObject
public static void Deserialize(Component component, JObject data);

// Returns true if this component type should be serialized
public static bool ShouldInclude(Component component);
```

`SceneIO` calls these; no `SerializedObject` logic leaks into `SceneIO.cs`.

---

## query-scene Updates

Extend the `component ==` filter to check both `customData[].type` and `builtInComponents[].type`.

Add property filtering on built-in component fields using the same syntax as `customData`:

```bash
Tools/query-scene Level_A "component == BoxCollider AND m_IsTrigger == true"
Tools/query-scene Level_A "component == Rigidbody AND m_Mass >= 5"
```

The filter engine already handles field-level comparisons on `customData`; extend it to also check matching entries in `builtInComponents`.

---

## Schema Update

Add `builtInComponents` to `Schemas/entity.schema.json` as an optional array with the same shape as `customData` (object array with required `type` string).

---

## CLAUDE.md Update

Add a row to the "What you want to do → File operation" table:

| Change a built-in component value | Edit the relevant field inside `builtInComponents` |

Note that property names use Unity's internal serialized names. `builtInComponents` is treated as the complete truth — consistent with `customData`.

---

## Migration

No special migration needed. On first reconcile after the feature ships, the write pipeline adds `builtInComponents` to every entity file. Until then, entities without the key will have their built-in components removed and immediately re-added during reconcile — acceptable one-time churn. Run "Initialize Scene" once after upgrading to settle all files.

---

## Implementation Order

1. `BuiltInComponentSerializer.cs` — serialize + deserialize, all property types, exclusion rules
2. Wire into `SceneIO.SerializeEntity` and `SceneIO.ReconcileComponents`
3. Update `query-scene` to filter on `builtInComponents`
4. Update `Schemas/entity.schema.json`
5. Update `CLAUDE.md`
6. Add test cases to `TESTS.md`
