# Changelog

## [1.3.0](https://github.com/hatayama/unity-cli-loop/compare/v1.2.1...v1.3.0) (2026-03-21)


### Features

* add mouse input visualization overlay with prefab workflow ([#806](https://github.com/hatayama/unity-cli-loop/issues/806)) ([c531459](https://github.com/hatayama/unity-cli-loop/commit/c531459ac9e40c5e72f8fa250a909ec166ac5b50))
* Replace runtime overlay generation with Prefab-based architecture and improve visualization ([#811](https://github.com/hatayama/unity-cli-loop/issues/811)) ([3ff8b09](https://github.com/hatayama/unity-cli-loop/commit/3ff8b090041360483b6637eae5157e4084121a9c))


### Bug Fixes

* clean up keyboard overlay preview badges ([#813](https://github.com/hatayama/unity-cli-loop/issues/813)) ([4caf06a](https://github.com/hatayama/unity-cli-loop/commit/4caf06a3b7860cd63f0285aae2d39054a3ea7269))
* Remove HideFlags.DontSave from overlay canvas to fix PlayMode exit cleanup ([#812](https://github.com/hatayama/unity-cli-loop/issues/812)) ([3957641](https://github.com/hatayama/unity-cli-loop/commit/395764134e80c131b6d70a63ce7806c2fd578032))

## [1.2.1](https://github.com/hatayama/unity-cli-loop/compare/v1.2.0...v1.2.1) (2026-03-19)


### Bug Fixes

* narrow EditorDialog preprocessor guard to UNITY_6000_3_OR_NEWER ([#804](https://github.com/hatayama/unity-cli-loop/issues/804)) ([65fce9e](https://github.com/hatayama/unity-cli-loop/commit/65fce9e0ce67fc40cbc818322c175de0aeefd889))
* Remove redundant .meta files causing import warnings ([#802](https://github.com/hatayama/unity-cli-loop/issues/802)) ([7d662cd](https://github.com/hatayama/unity-cli-loop/commit/7d662cd9d5de78204339903ee8e0c7521cba40dc))

## [1.2.0](https://github.com/hatayama/unity-cli-loop/compare/v1.1.0...v1.2.0) (2026-03-18)


### Features

* add simulate-mouse-input tool and split simulate-mouse into UI/Input System tools ([#799](https://github.com/hatayama/unity-cli-loop/issues/799)) ([a465640](https://github.com/hatayama/unity-cli-loop/commit/a465640ffa16a0d136956937e25dedaa61998b7a))


### Bug Fixes

* Use informational dialog icon for skill installation success on Unity 6+ ([#797](https://github.com/hatayama/unity-cli-loop/issues/797)) ([0326b38](https://github.com/hatayama/unity-cli-loop/commit/0326b38b1bd3e0089d17744a043b1fb38a7943fa))

## [1.1.0](https://github.com/hatayama/unity-cli-loop/compare/v1.0.2...v1.1.0) (2026-03-17)


### Features

* keyboard simulation ([#783](https://github.com/hatayama/unity-cli-loop/issues/783)) ([8d632c4](https://github.com/hatayama/unity-cli-loop/commit/8d632c4fa55b03b72fdf4ce75feca11e3ffb1060))


### Bug Fixes

* Fix submenu misrender in SkillsTarget dropdown ([#787](https://github.com/hatayama/unity-cli-loop/issues/787)) ([28731df](https://github.com/hatayama/unity-cli-loop/commit/28731df6c48681d445e47e9bc38eb521b9fd7b23))
* Windows CLI build support ([#794](https://github.com/hatayama/unity-cli-loop/issues/794)) ([ba7d382](https://github.com/hatayama/unity-cli-loop/commit/ba7d3823762b8bc11ebe57e64caf84d1b86a5faa))

## [1.0.2](https://github.com/hatayama/unity-cli-loop/compare/v1.0.1...v1.0.2) (2026-03-16)


### Bug Fixes

* update repository URLs from uLoopMCP to unity-cli-loop ([#779](https://github.com/hatayama/unity-cli-loop/issues/779)) ([35e56a0](https://github.com/hatayama/unity-cli-loop/commit/35e56a0751f0ec0a57b025f9a539d5918328ba0b))

## [1.0.1](https://github.com/hatayama/unity-cli-loop/compare/v1.0.0...v1.0.1) (2026-03-16)


### Bug Fixes

* Add missing .meta files for playmode skill references ([#773](https://github.com/hatayama/unity-cli-loop/issues/773)) ([19dcf5d](https://github.com/hatayama/unity-cli-loop/commit/19dcf5d0b3aa9eeef7de62c9f7003b3457beab7a))

## [0.70.1](https://github.com/hatayama/uLoopMCP/compare/v0.70.0...v0.70.1) (2026-03-15)


### Bug Fixes

* Classify csc.rsp compiler diagnostics correctly in get-logs LogType filter ([#761](https://github.com/hatayama/uLoopMCP/issues/761)) ([#767](https://github.com/hatayama/uLoopMCP/issues/767)) ([7069c5f](https://github.com/hatayama/uLoopMCP/commit/7069c5f5a0b7d646060200373515d65fa8d24809))

## [0.70.0](https://github.com/hatayama/uLoopMCP/compare/v0.69.6...v0.70.0) (2026-03-15)


### Features

* Add simulate-mouse tool for PlayMode UI interaction ([#759](https://github.com/hatayama/uLoopMCP/issues/759)) ([5679f34](https://github.com/hatayama/uLoopMCP/commit/5679f342932a67aecefbff804bd7265fdd6ec00d))

## [0.69.6](https://github.com/hatayama/uLoopMCP/compare/v0.69.5...v0.69.6) (2026-03-06)


### Bug Fixes

* apply context: fork to uloop-execute-dynamic-code skill and add wiring references ([#743](https://github.com/hatayama/uLoopMCP/issues/743)) ([4ab452b](https://github.com/hatayama/uLoopMCP/commit/4ab452ba9efde76704b18d58d403761df2128941))

## [0.69.5](https://github.com/hatayama/uLoopMCP/compare/v0.69.4...v0.69.5) (2026-03-06)


### Bug Fixes

* isolate Roslyn dependencies via shared registry ([#741](https://github.com/hatayama/uLoopMCP/issues/741)) ([c96a3a2](https://github.com/hatayama/uLoopMCP/commit/c96a3a2e06a268cc4daa5cb0eb9936b638abf5bb))

## [0.69.4](https://github.com/hatayama/uLoopMCP/compare/v0.69.3...v0.69.4) (2026-03-05)


### Bug Fixes

* Standardize SKILL.md descriptions and reduce verbosity ([#733](https://github.com/hatayama/uLoopMCP/issues/733)) ([a743607](https://github.com/hatayama/uLoopMCP/commit/a743607a9bfa6ecdf5c31946f3776f662f4e0e15))
* Sync lint-staged version in package.json with package-lock.json ([#735](https://github.com/hatayama/uLoopMCP/issues/735)) ([60eff43](https://github.com/hatayama/uLoopMCP/commit/60eff43cf8762f1075b3cb8e214d483aaf55af6e))

## [0.69.3](https://github.com/hatayama/uLoopMCP/compare/v0.69.2...v0.69.3) (2026-03-05)


### Bug Fixes

* Handle Win32Exception in skills install process start ([#730](https://github.com/hatayama/uLoopMCP/issues/730)) ([cd7a4a3](https://github.com/hatayama/uLoopMCP/commit/cd7a4a3a19ab1d4830d1131074caeab4e85969e1))
* Update hono and @hono/node-server to resolve high severity vulnerabilities ([#731](https://github.com/hatayama/uLoopMCP/issues/731)) ([135044a](https://github.com/hatayama/uLoopMCP/commit/135044ae761ef50144fb3a23c5118d6e53367ffe))

## [0.69.2](https://github.com/hatayama/uLoopMCP/compare/v0.69.1...v0.69.2) (2026-03-03)


### Bug Fixes

* Prevent rename migration from shadowing legacy settings migration ([#725](https://github.com/hatayama/uLoopMCP/issues/725)) ([b03337b](https://github.com/hatayama/uLoopMCP/commit/b03337be8892cc807e8f6a47beab023c7d985bc9))

## [0.69.1](https://github.com/hatayama/uLoopMCP/compare/v0.69.0...v0.69.1) (2026-03-03)


### Bug Fixes

* Add GetVersionTool to core package and improve CLI error handling ([#723](https://github.com/hatayama/uLoopMCP/issues/723)) ([21afcc8](https://github.com/hatayama/uLoopMCP/commit/21afcc899d7b8857737a7bda9480ccb447f9083c))

## [0.69.0](https://github.com/hatayama/uLoopMCP/compare/v0.68.3...v0.69.0) (2026-03-03)


### Features

* Add delete button for MCP configuration ([#718](https://github.com/hatayama/uLoopMCP/issues/718)) ([3e4c4fb](https://github.com/hatayama/uLoopMCP/commit/3e4c4fb907b1fc446e19f3eba98bf6afc174a3ee))


### Bug Fixes

* Prevent CLI from sending commands to wrong Unity instance ([#719](https://github.com/hatayama/uLoopMCP/issues/719)) ([d0e47b3](https://github.com/hatayama/uLoopMCP/commit/d0e47b392677af36dfba740e9c7e80579877bad8))
* Prevent toggle ChangeEvent from collapsing parent Foldout ([#721](https://github.com/hatayama/uLoopMCP/issues/721)) ([f6caf9b](https://github.com/hatayama/uLoopMCP/commit/f6caf9b7066f5f4587788c7e3b2d9e8ce0062aa3))
* Rename settings.security.json to settings.permissions.json ([#713](https://github.com/hatayama/uLoopMCP/issues/713)) ([ae1a21a](https://github.com/hatayama/uLoopMCP/commit/ae1a21af2a575a94fd96476feb2fe05bfbb77f88))

## [0.68.3](https://github.com/hatayama/uLoopMCP/compare/v0.68.2...v0.68.3) (2026-03-02)


### Bug Fixes

* Group help commands by category in uloop -h ([#708](https://github.com/hatayama/uLoopMCP/issues/708)) ([dbfe539](https://github.com/hatayama/uLoopMCP/commit/dbfe5394172a80f550304b95787d36d4046d0404))

## [0.68.2](https://github.com/hatayama/uLoopMCP/compare/v0.68.1...v0.68.2) (2026-03-01)


### Bug Fixes

* Hide disabled tools from CLI help and shell completion output ([#705](https://github.com/hatayama/uLoopMCP/issues/705)) ([3f49750](https://github.com/hatayama/uLoopMCP/commit/3f4975078e324b56e4363c510c6081b7373afa63))

## [0.68.1](https://github.com/hatayama/uLoopMCP/compare/v0.68.0...v0.68.1) (2026-03-01)


### Bug Fixes

* stabilize Tool Settings toggle interaction during UI refresh ([#702](https://github.com/hatayama/uLoopMCP/issues/702)) ([6b4ba25](https://github.com/hatayama/uLoopMCP/commit/6b4ba25d2d7821abecf0a990c7021a9c5de64518))

## [0.68.0](https://github.com/hatayama/uLoopMCP/compare/v0.67.5...v0.68.0) (2026-02-28)


### Features

* Add per-tool enable/disable toggle for MCP tools ([#698](https://github.com/hatayama/uLoopMCP/issues/698)) ([5ca8b4c](https://github.com/hatayama/uLoopMCP/commit/5ca8b4ca99ed618148a93d74a9dd76fb6f0cff60))
* Migrate security settings to project-scoped .uloop/settings.security.json ([#696](https://github.com/hatayama/uLoopMCP/issues/696)) ([222d46e](https://github.com/hatayama/uLoopMCP/commit/222d46e485b5b0006f8ed9e74e19e191938f2010))


### Bug Fixes

* Improve tool settings UI message to mention context window benefit ([#700](https://github.com/hatayama/uLoopMCP/issues/700)) ([040c8bd](https://github.com/hatayama/uLoopMCP/commit/040c8bd8687d8925219285f3b401c3d6900a5f3e))
* Rename capture-window MCP tool to screenshot ([#701](https://github.com/hatayama/uLoopMCP/issues/701)) ([b178bc8](https://github.com/hatayama/uLoopMCP/commit/b178bc899a87c9ad4fce86adc27611c56a9f2811))

## [0.67.5](https://github.com/hatayama/uLoopMCP/compare/v0.67.4...v0.67.5) (2026-02-26)


### Bug Fixes

* add meta ([#692](https://github.com/hatayama/uLoopMCP/issues/692)) ([34b29de](https://github.com/hatayama/uLoopMCP/commit/34b29de1ea497e8755f6c8cf711eebf7b1378f15))

## [0.67.4](https://github.com/hatayama/uLoopMCP/compare/v0.67.3...v0.67.4) (2026-02-26)


### Bug Fixes

* correct SKILL.md parameter docs to match C# implementations ([#689](https://github.com/hatayama/uLoopMCP/issues/689)) ([e76ab29](https://github.com/hatayama/uLoopMCP/commit/e76ab29372c28f6513016d11b9fb7cb18e6f025f))
* Fix incomplete property serialization in ComponentPropertySerializer ([#691](https://github.com/hatayama/uLoopMCP/issues/691)) ([2db6eb3](https://github.com/hatayama/uLoopMCP/commit/2db6eb3f63865ab6e56c41cabbbe3a37d183b164))

## [0.67.3](https://github.com/hatayama/uLoopMCP/compare/v0.67.2...v0.67.3) (2026-02-26)


### Bug Fixes

* update READMEs with --project-path option and comprehensive CLI reference ([#687](https://github.com/hatayama/uLoopMCP/issues/687)) ([7829c9d](https://github.com/hatayama/uLoopMCP/commit/7829c9dc6e21bc117b353251b71eab7bbe95b942))
