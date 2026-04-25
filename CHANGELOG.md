## [1.4.5](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.4.4...v1.4.5) (2026-04-25)


### Bug Fixes

* serialize sub-asset ObjectReferences in builtInComponents as {path, name} ([363c071](https://github.com/zacharysnewman/unity-ai-bridge/commit/363c071d4f49781d142f0bcb4f8188640f0d64dd))

## [1.4.4](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.4.3...v1.4.4) (2026-04-25)


### Bug Fixes

* patch-entities object/array values, create-entities JSON array input, improve CLAUDE.md tooling guidance ([b988d39](https://github.com/zacharysnewman/unity-ai-bridge/commit/b988d39efed372dd36db487ef34797037beaa481))

## [1.4.3](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.4.2...v1.4.3) (2026-04-25)


### Bug Fixes

* skip unhandled Unity namespace types instead of writing empty objects ([53756e6](https://github.com/zacharysnewman/unity-ai-bridge/commit/53756e63b80d8a7a74935d5105880c3be7249f1f))

## [1.4.2](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.4.1...v1.4.2) (2026-04-25)


### Bug Fixes

* omit runtime Unity objects from serialization instead of writing null ([c5ff66d](https://github.com/zacharysnewman/unity-ai-bridge/commit/c5ff66d422ec1b9bb843eee5c2d60456a5913c79))

## [1.4.1](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.4.0...v1.4.1) (2026-04-25)


### Bug Fixes

* warn when a Unity type falls through to empty-object serialization ([e313be2](https://github.com/zacharysnewman/unity-ai-bridge/commit/e313be2c75e9888992dc0620f980882acb486e1c))

# [1.4.0](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.3.3...v1.4.0) (2026-04-25)


### Features

* expand UnityMathConverter to cover all common Unity value types ([215773b](https://github.com/zacharysnewman/unity-ai-bridge/commit/215773b894d81907b0cb0da91c34d2ee9a3d5bee))

## [1.3.3](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.3.2...v1.3.3) (2026-04-25)


### Bug Fixes

* skip UnityEngine/UnityEditor type properties in JToken.FromObject ([d29e3ad](https://github.com/zacharysnewman/unity-ai-bridge/commit/d29e3ad881e351803ff981002eee1644ed352763))

## [1.3.2](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.3.1...v1.3.2) (2026-04-25)


### Bug Fixes

* forward Unity console errors to init log and wrap all field paths ([9bdba54](https://github.com/zacharysnewman/unity-ai-bridge/commit/9bdba5446ee770cda995ee36e0e0e87e189e8da9))

## [1.3.1](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.3.0...v1.3.1) (2026-04-25)


### Bug Fixes

* handle arrays and lists of UnityEngine.Object in field serialization ([33db873](https://github.com/zacharysnewman/unity-ai-bridge/commit/33db8731797db590eeaa6b1692babdd1fa2d2ab8))

# [1.3.0](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.2.0...v1.3.0) (2026-04-25)


### Features

* add per-component and per-field logging to init log ([d70c590](https://github.com/zacharysnewman/unity-ai-bridge/commit/d70c59030221987b96b8183cf3b081e7de980197))

# [1.2.0](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.7...v1.2.0) (2026-04-25)


### Features

* add crash-safe init log for diagnosing migration hangs ([4d2e2e1](https://github.com/zacharysnewman/unity-ai-bridge/commit/4d2e2e112de8952aa9437a47ddd77a81a5787200))

## [1.1.7](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.6...v1.1.7) (2026-04-24)


### Bug Fixes

* add MaxDepth to FieldSerializer to prevent hang on complex Unity objects ([e91b38e](https://github.com/zacharysnewman/unity-ai-bridge/commit/e91b38e6c1d9c4caf9e3d3dfd3a98a247c82598f))

## [1.1.6](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.5...v1.1.6) (2026-04-24)


### Bug Fixes

* replace ForceSynchronousImport with targeted manifest ImportAsset ([d266bc1](https://github.com/zacharysnewman/unity-ai-bridge/commit/d266bc1a9041d02a8da15f8e88b8317bcdc3da06))

## [1.1.5](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.4...v1.1.5) (2026-04-24)


### Bug Fixes

* remove yield-in-try-catch CS1626 errors in MigrateScene ([50ad74d](https://github.com/zacharysnewman/unity-ai-bridge/commit/50ad74d10448b20c689b9eedca0bd46b377196ee))

## [1.1.4](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.3...v1.1.4) (2026-04-24)


### Bug Fixes

* prevent migration hang from per-entity AssetDatabase.ImportAsset ([01a05cb](https://github.com/zacharysnewman/unity-ai-bridge/commit/01a05cb04392d3be5f132ee88dd159ac2f731dfd))

## [1.1.3](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.2...v1.1.3) (2026-04-24)


### Bug Fixes

* skip SetDirty on hot-reload apply failure to prevent JSON corruption ([30c1825](https://github.com/zacharysnewman/unity-ai-bridge/commit/30c182563da6053d92ee34fe4f3fcb234156a093))

## [1.1.2](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.1...v1.1.2) (2026-04-24)


### Bug Fixes

* catch per-entity apply failures during bootstrap and hot-reload ([76ae76f](https://github.com/zacharysnewman/unity-ai-bridge/commit/76ae76fc70304a0cf9ca0190feff74d774f08652))

## [1.1.1](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.1.0...v1.1.1) (2026-04-24)


### Bug Fixes

* add missing meta file for ClaudeIntegration directory ([5dee148](https://github.com/zacharysnewman/unity-ai-bridge/commit/5dee148f3dd31341bcac47d5e86c851d042341e1))

# [1.1.0](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.0.1...v1.1.0) (2026-04-24)


### Features

* reorganize installable assets into ClaudeIntegration/ mirror directory ([4bf7068](https://github.com/zacharysnewman/unity-ai-bridge/commit/4bf7068739be45287168f04fb15223912e1fa0db))

## [1.0.1](https://github.com/zacharysnewman/unity-ai-bridge/compare/v1.0.0...v1.0.1) (2026-04-24)


### Bug Fixes

* add missing meta file for CHANGELOG.md ([e5613f5](https://github.com/zacharysnewman/unity-ai-bridge/commit/e5613f5bd08ecbd30e67f368ac16ab2333d025cc))

# 1.0.0 (2026-04-24)


### Bug Fixes

* add missing .meta files for renamed/new Tools scripts ([549d64e](https://github.com/zacharysnewman/unity-ai-bridge/commit/549d64e839324e919564df2fe467d8c3fd501049)), closes [#12](https://github.com/zacharysnewman/unity-ai-bridge/issues/12)
* safe multi-scene bootstrap, path-prefix manager routing, and coroutine migration ([25d3121](https://github.com/zacharysnewman/unity-ai-bridge/commit/25d3121be9c908eef61991ec94183f321a69f212))
* skip UnityEngine.Object fields, add ScriptableObject asset-path support, add UnityMathConverter ([f335d51](https://github.com/zacharysnewman/unity-ai-bridge/commit/f335d51f823b311bb2e87fe65143a90b72355804))


### Features

* add semantic-release CI workflow and config ([763a5fe](https://github.com/zacharysnewman/unity-ai-bridge/commit/763a5fe2276db9895bad158d7042616532f51bcd))
* built-in component serialization (Colliders, Rigidbody, etc.) ([6bfc070](https://github.com/zacharysnewman/unity-ai-bridge/commit/6bfc0700f130b97194103c889f370b2e3fa2423b))
* create-entities and delete-entities tools with shared undo history ([79c0bd8](https://github.com/zacharysnewman/unity-ai-bridge/commit/79c0bd8bc1e8b3739ba8e77dd4586e1269762931))
* editor state sync — scene path, camera, visible objects ([fcdbefd](https://github.com/zacharysnewman/unity-ai-bridge/commit/fcdbefdde756be935046c290ab62405757024941))
* fill scene-syncing gaps — AnimationCurve, Gradient, ManagedReference, scene-object refs, Renderer/MeshFilter, builtIn patching ([1079729](https://github.com/zacharysnewman/unity-ai-bridge/commit/10797292c27b231f4d39e199b925c74e7e2d059a))
* patch-entities — batch field mutations on matched entities ([ae41fd7](https://github.com/zacharysnewman/unity-ai-bridge/commit/ae41fd7ccbd2af65312e268c6a3f8db15b59a4af))
* query-scene --stdin for scoped UUID filtering ([9a60b87](https://github.com/zacharysnewman/unity-ai-bridge/commit/9a60b876660f3ec373c1f17b76cc2d12c6647a72))
* scene lifecycle management, sibling index serialization, scene duplication ([693b7a3](https://github.com/zacharysnewman/unity-ai-bridge/commit/693b7a359b50c87f640dff834504f4957ee849db))
* select-objects --stdin for tool composition ([9a7b1e7](https://github.com/zacharysnewman/unity-ai-bridge/commit/9a7b1e7b081e1f2fc7cccfa4ddb82d62960f2549))
* serialize all UnityEngine.Object asset fields in customData as asset paths ([81fba55](https://github.com/zacharysnewman/unity-ai-bridge/commit/81fba554818100b79fdd7e7efd8ae72a681fa1bd))
* structured log file for query-logs; fix Vector3 self-ref loop ([68ff890](https://github.com/zacharysnewman/unity-ai-bridge/commit/68ff89010c5da73a06f6b68a33729e7eca0c2561))
* sub-asset identity encoding and array cap removal ([573a96d](https://github.com/zacharysnewman/unity-ai-bridge/commit/573a96da9995f3f7300a4432180d1939f0c9c38a))
* tag/layer/isStatic/activeSelf sync, patch undo/history, log fixes ([cfa80c5](https://github.com/zacharysnewman/unity-ai-bridge/commit/cfa80c57fd336c64197e698139d7a9704c843919))
