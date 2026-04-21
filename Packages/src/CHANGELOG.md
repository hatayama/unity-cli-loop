# Changelog

## [2.0.0](https://github.com/hatayama/unity-cli-loop/compare/v1.7.3...v2.0.0) (2026-04-20)


### Features

* execute-dynamic-code now runs more than 6x faster ([#901](https://github.com/hatayama/unity-cli-loop/issues/901)) ([f48cdaa](https://github.com/hatayama/unity-cli-loop/commit/f48cdaaee5e3df0f4035a8281a6b7fe8511df04c))
* Setup Wizard now handles first-time skill installation, grouping skills into a subfolder, target detection, status reporting, and startup behavior more clearly and reliably ([#927](https://github.com/hatayama/unity-cli-loop/issues/927), [#950](https://github.com/hatayama/unity-cli-loop/issues/950), [#951](https://github.com/hatayama/unity-cli-loop/issues/951), [#952](https://github.com/hatayama/unity-cli-loop/issues/952), [#953](https://github.com/hatayama/unity-cli-loop/issues/953), [#954](https://github.com/hatayama/unity-cli-loop/issues/954), [#963](https://github.com/hatayama/unity-cli-loop/issues/963), [#922](https://github.com/hatayama/unity-cli-loop/issues/922))
* Windows users can follow PowerShell-specific PlayMode automation examples ([#947](https://github.com/hatayama/unity-cli-loop/issues/947)) ([59e50b4](https://github.com/hatayama/unity-cli-loop/commit/59e50b4e26f4e49c08761108737ae5a702a22788))


### Bug Fixes

* allow installation without the new Input System package ([#938](https://github.com/hatayama/unity-cli-loop/issues/938)) ([b08d899](https://github.com/hatayama/unity-cli-loop/commit/b08d899856d8c760aae7856b33f6a5bb3cc06d7f))
* compile commands stay more reliable while the Unity server recovers ([#925](https://github.com/hatayama/unity-cli-loop/issues/925)) ([0f63ed5](https://github.com/hatayama/unity-cli-loop/commit/0f63ed5fdd9dc20ddc6b005dc1c7a4d9d1900090))
* Dynamic code commands recover more cleanly after Unity restarts ([#944](https://github.com/hatayama/unity-cli-loop/issues/944)) ([bdbe286](https://github.com/hatayama/unity-cli-loop/commit/bdbe286d710bb2c7415b4f401d3b65e8c98f9e13))
* invalid EditMode test requests now return a clear error during play mode ([#940](https://github.com/hatayama/unity-cli-loop/issues/940)) ([ed0c7ea](https://github.com/hatayama/unity-cli-loop/commit/ed0c7eaebfa5430fbdf95bfbd77f36e3fa500f2c))
* make MCP deprecation easier to notice in the settings window ([#948](https://github.com/hatayama/unity-cli-loop/issues/948)) ([0ace70d](https://github.com/hatayama/unity-cli-loop/commit/0ace70d944fca67172ce24d3bca84b91a76c36a0))
* make the setup and settings windows easier to use ([#932](https://github.com/hatayama/unity-cli-loop/issues/932)) ([6d61a7d](https://github.com/hatayama/unity-cli-loop/commit/6d61a7dccc418b2c8a41cab4132accf27780afd9))
* prevent installation errors when the Unity Test Framework package is missing ([#939](https://github.com/hatayama/unity-cli-loop/issues/939)) ([958e4b5](https://github.com/hatayama/unity-cli-loop/commit/958e4b5050f64a47dff00575c670ebf5a9ad3196))
* `uloop launch` now waits for Unity to finish starting ([#955](https://github.com/hatayama/unity-cli-loop/issues/955)) ([9514cc9](https://github.com/hatayama/unity-cli-loop/commit/9514cc90dc1427452ff3f998bb5b1e956901e86d))

## [1.7.3](https://github.com/hatayama/unity-cli-loop/compare/v1.7.2...v1.7.3) (2026-04-10)


### Bug Fixes

* Avoid false "Unity not running" errors when the editor is still open ([#919](https://github.com/hatayama/unity-cli-loop/issues/919)) ([5376279](https://github.com/hatayama/unity-cli-loop/commit/53762791b70192efcf7fea2fcdf8dc8d91aa0c2d))
* show the setup wizard when the package version changes ([#920](https://github.com/hatayama/unity-cli-loop/issues/920)) ([4f99cbc](https://github.com/hatayama/unity-cli-loop/commit/4f99cbc777d5eb1867815a3f22965205f0e01a69))
* stabilize npm publishing and remove CLI bin warning ([#918](https://github.com/hatayama/unity-cli-loop/issues/918)) ([1b7a7f1](https://github.com/hatayama/unity-cli-loop/commit/1b7a7f12865767f5faf27529c5aa7a8502cb9a42))

## [1.7.2](https://github.com/hatayama/unity-cli-loop/compare/v1.7.1...v1.7.2) (2026-04-09)


### Bug Fixes

* remove deprecation warning from uloop update ([#913](https://github.com/hatayama/unity-cli-loop/issues/913)) ([61ed144](https://github.com/hatayama/unity-cli-loop/commit/61ed144ffa1c0b8e2b6a10cb9eb587dada3fc1c6))

## [1.7.1](https://github.com/hatayama/unity-cli-loop/compare/v1.7.0...v1.7.1) (2026-04-09)


### Bug Fixes

* avoid misleading Unity editor availability errors ([#911](https://github.com/hatayama/unity-cli-loop/issues/911)) ([4c2b7a6](https://github.com/hatayama/unity-cli-loop/commit/4c2b7a63bd4a31f0e84fa0964a28c268b33eb9c3))
* require skills directories for auto-install detection ([#912](https://github.com/hatayama/unity-cli-loop/issues/912)) ([8832050](https://github.com/hatayama/unity-cli-loop/commit/8832050f7f4d9b94072ee33be2be0aeba90edf44))
* unblock TypeScript server dependency updates by resolving npm audit findings ([#907](https://github.com/hatayama/unity-cli-loop/issues/907)) ([ffa3863](https://github.com/hatayama/unity-cli-loop/commit/ffa386365f31a8e908d2059e986218e1b113bd70))

## [1.7.0](https://github.com/hatayama/unity-cli-loop/compare/v1.6.4...v1.7.0) (2026-04-07)


### Features

* Improve execute-dynamic-code performance by pre-resolving using directives ([#889](https://github.com/hatayama/unity-cli-loop/issues/889)) ([ba94c26](https://github.com/hatayama/unity-cli-loop/commit/ba94c268edaa948d53952ae76baa2c99b8ba1d85))


### Bug Fixes

* restore Unity settings after interrupted atomic writes ([#898](https://github.com/hatayama/unity-cli-loop/issues/898)) ([887800d](https://github.com/hatayama/unity-cli-loop/commit/887800dc18caa695ee3926827ac51dfe7a18183a))

## [1.6.4](https://github.com/hatayama/unity-cli-loop/compare/v1.6.3...v1.6.4) (2026-04-04)


### Bug Fixes

* Deduplicate assembly references by name and add architecture overview to CLAUDE.md ([#887](https://github.com/hatayama/unity-cli-loop/issues/887)) ([cb0415e](https://github.com/hatayama/unity-cli-loop/commit/cb0415ed913be4fcf3cb8b90a3d54e3622cc9489))

## [1.6.3](https://github.com/hatayama/unity-cli-loop/compare/v1.6.2...v1.6.3) (2026-04-01)


### Bug Fixes

* Detect and auto-recover from silent MCP server loop exit ([#871](https://github.com/hatayama/unity-cli-loop/issues/871)) ([d0430c8](https://github.com/hatayama/unity-cli-loop/commit/d0430c8e0ff1d71e37dabef9099c654565368f61))
* Hide --port option from help, docs, and skill descriptions ([#873](https://github.com/hatayama/unity-cli-loop/issues/873)) ([eec4831](https://github.com/hatayama/unity-cli-loop/commit/eec48313e3c17d12de424d1eeab3b1a967b1c346))
* Prevent CLI from connecting to wrong Unity instance via stale port ([#875](https://github.com/hatayama/unity-cli-loop/issues/875)) ([2e577c8](https://github.com/hatayama/unity-cli-loop/commit/2e577c834e23706bc692d2303d3db417c6ef6098))

## [1.6.2](https://github.com/hatayama/unity-cli-loop/compare/v1.6.1...v1.6.2) (2026-03-30)


### Bug Fixes

* Prevent SetupWizardWindow from showing on package upgrade ([#861](https://github.com/hatayama/unity-cli-loop/issues/861)) ([358a32f](https://github.com/hatayama/unity-cli-loop/commit/358a32f8b8821d02e529be104762a3fc59de45a6))

## [1.6.1](https://github.com/hatayama/unity-cli-loop/compare/v1.6.0...v1.6.1) (2026-03-30)


### Bug Fixes

* Prevent SetupWizardWindow from showing for existing users on upgrade ([#857](https://github.com/hatayama/unity-cli-loop/issues/857)) ([c9d4346](https://github.com/hatayama/unity-cli-loop/commit/c9d43466dfb52d074e609631eac8b9b1876565e8))

## [1.6.0](https://github.com/hatayama/unity-cli-loop/compare/v1.5.1...v1.6.0) (2026-03-29)


### Features

* Add SetupWizardWindow for step-by-step onboarding ([#855](https://github.com/hatayama/unity-cli-loop/issues/855)) ([a723059](https://github.com/hatayama/unity-cli-loop/commit/a723059b0eebeeaf2b58663ba39293b9453afcca))
* Extract server status/controls UI into standalone ServerEditorWindow ([#853](https://github.com/hatayama/unity-cli-loop/issues/853)) ([7934fba](https://github.com/hatayama/unity-cli-loop/commit/7934fba02f42e5e042f891b25a0322ea310889fd))


### Bug Fixes

* correct skill directory mapping to match CLI target-config ([#852](https://github.com/hatayama/unity-cli-loop/issues/852)) ([4818f52](https://github.com/hatayama/unity-cli-loop/commit/4818f528102def6b7fd4d3071bd88e6400f44a12))

## [1.5.1](https://github.com/hatayama/unity-cli-loop/compare/v1.5.0...v1.5.1) (2026-03-27)


### Bug Fixes

* clarify execute-dynamic-code skill parameters ([#847](https://github.com/hatayama/unity-cli-loop/issues/847)) ([89bbff8](https://github.com/hatayama/unity-cli-loop/commit/89bbff8fbe9107a20981d239a19f7dccf5e5ae34))

## [1.5.0](https://github.com/hatayama/unity-cli-loop/compare/v1.4.0...v1.5.0) (2026-03-25)


### Features

* Remove 3 redundant MCP tools and add Design Philosophy section ([#837](https://github.com/hatayama/unity-cli-loop/issues/837)) ([11412b6](https://github.com/hatayama/unity-cli-loop/commit/11412b6de429023b630a24cd3fbf685ae7274048))


### Bug Fixes

* replay progress bar always appearing at 100% ([#839](https://github.com/hatayama/unity-cli-loop/issues/839)) ([2880453](https://github.com/hatayama/unity-cli-loop/commit/2880453c747fd66092081f97982867368fdb8997))

## [1.4.0](https://github.com/hatayama/unity-cli-loop/compare/v1.3.0...v1.4.0) (2026-03-25)


### Features

* Migrate dynamic code compilation from Roslyn to AssemblyBuilder with enhanced security ([#829](https://github.com/hatayama/unity-cli-loop/issues/829)) ([0ab2b87](https://github.com/hatayama/unity-cli-loop/commit/0ab2b8768ea286e3aecae8bd866ee72e2550ce48))


### Bug Fixes

* refine MCP tool settings UI ([#836](https://github.com/hatayama/unity-cli-loop/issues/836)) ([16c3c87](https://github.com/hatayama/unity-cli-loop/commit/16c3c87e3b34ca1c3750f0923ec2ecbbfe27d82f))
* suppress idle overlay reactivation after mouse release during input replay ([#827](https://github.com/hatayama/unity-cli-loop/issues/827)) ([7133e9c](https://github.com/hatayama/unity-cli-loop/commit/7133e9c1a53473b25bcdf6f5712d5806c2147da6))

## [1.3.0](https://github.com/hatayama/unity-cli-loop/compare/v1.2.1...v1.3.0) (2026-03-23)


### Features

* add mouse input visualization overlay with prefab workflow ([#806](https://github.com/hatayama/unity-cli-loop/issues/806)) ([c531459](https://github.com/hatayama/unity-cli-loop/commit/c531459ac9e40c5e72f8fa250a909ec166ac5b50))
* Input recording/replay system  ([#814](https://github.com/hatayama/unity-cli-loop/issues/814)) ([d7a7f58](https://github.com/hatayama/unity-cli-loop/commit/d7a7f58096020a76caa4fe04392f96558a91f6c0))
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
