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
