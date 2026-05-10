# Changelog

## [3.0.0-beta.5](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.4...v3.0.0-beta.5) (2026-05-10)


### ⚠ BREAKING CHANGES

* Rebuild execute-dynamic-code with shared Roslyn compilation and layered architecture ([#901](https://github.com/hatayama/unity-cli-loop/issues/901))

### Features

* (capture-window): rename from capture-unity-window and add MatchMode parameter ([#504](https://github.com/hatayama/unity-cli-loop/issues/504)) ([1614fc5](https://github.com/hatayama/unity-cli-loop/commit/1614fc564a0680c5f9b68a10c6a741a29b953ff6))
* add --project-path option to CLI for targeting Unity instances by path ([#658](https://github.com/hatayama/unity-cli-loop/issues/658)) ([c2073d5](https://github.com/hatayama/unity-cli-loop/commit/c2073d5eb53394bbad434a8e8aedc53cbd5d9b74))
* add -d/--delete-recovery option to launch command ([#653](https://github.com/hatayama/unity-cli-loop/issues/653)) ([7c2eabe](https://github.com/hatayama/unity-cli-loop/commit/7c2eabe7b019de7039543fbe68dfe1965b6fb342))
* add automatic using directive resolution for CS0246 errors ([#599](https://github.com/hatayama/unity-cli-loop/issues/599)) ([aed000f](https://github.com/hatayama/unity-cli-loop/commit/aed000fe8c122b7a4e9c6fdb1d86feaf0527d79a))
* Add delete button for MCP configuration ([#718](https://github.com/hatayama/unity-cli-loop/issues/718)) ([3e4c4fb](https://github.com/hatayama/unity-cli-loop/commit/3e4c4fb907b1fc446e19f3eba98bf6afc174a3ee))
* Add domain-reload wait option for compile tool with file-based recovery ([#650](https://github.com/hatayama/unity-cli-loop/issues/650)) ([5536b1e](https://github.com/hatayama/unity-cli-loop/commit/5536b1e2a8105710045d4b8320a1d8a944ab589c))
* Add Go CLI linking command and refresh boolean flag guidance ([#1031](https://github.com/hatayama/unity-cli-loop/issues/1031)) ([9ac9df1](https://github.com/hatayama/unity-cli-loop/commit/9ac9df1905878679233cf5fa123da401cb035408))
* add mouse input visualization overlay with prefab workflow ([#806](https://github.com/hatayama/unity-cli-loop/issues/806)) ([c531459](https://github.com/hatayama/unity-cli-loop/commit/c531459ac9e40c5e72f8fa250a909ec166ac5b50))
* Add per-tool enable/disable toggle for MCP tools ([#698](https://github.com/hatayama/unity-cli-loop/issues/698)) ([5ca8b4c](https://github.com/hatayama/unity-cli-loop/commit/5ca8b4ca99ed618148a93d74a9dd76fb6f0cff60))
* Add Prefab Stage support and ObjectReference serialization to find-game-objects ([#636](https://github.com/hatayama/unity-cli-loop/issues/636)) ([f667aa8](https://github.com/hatayama/unity-cli-loop/commit/f667aa81ec69327b383c28722173f846085a3dc3))
* Add Selected mode to FindGameObjects tool ([#569](https://github.com/hatayama/unity-cli-loop/issues/569)) ([d2f50cd](https://github.com/hatayama/unity-cli-loop/commit/d2f50cd4c4a9b375dc1ce7088648e55f25c0ee20))
* Add SetupWizardWindow for step-by-step onboarding ([#855](https://github.com/hatayama/unity-cli-loop/issues/855)) ([a723059](https://github.com/hatayama/unity-cli-loop/commit/a723059b0eebeeaf2b58663ba39293b9453afcca))
* Add simulate-mouse tool for PlayMode UI interaction ([#759](https://github.com/hatayama/unity-cli-loop/issues/759)) ([5679f34](https://github.com/hatayama/unity-cli-loop/commit/5679f342932a67aecefbff804bd7265fdd6ec00d))
* add simulate-mouse-input tool and split simulate-mouse into UI/Input System tools ([#799](https://github.com/hatayama/unity-cli-loop/issues/799)) ([a465640](https://github.com/hatayama/unity-cli-loop/commit/a465640ffa16a0d136956937e25dedaa61998b7a))
* Add UseSelection mode to GetHierarchy tool ([#575](https://github.com/hatayama/unity-cli-loop/issues/575)) ([1bdb079](https://github.com/hatayama/unity-cli-loop/commit/1bdb0791a321b819800b52f87d1016f21698e782))
* Add version mismatch diagnostic for timeout and connection errors ([#576](https://github.com/hatayama/unity-cli-loop/issues/576)) ([a4c9125](https://github.com/hatayama/unity-cli-loop/commit/a4c9125830e253442c9fd464533dd48585f077e7))
* add Windows npm install permission pre-check and error classification ([#677](https://github.com/hatayama/unity-cli-loop/issues/677)) ([d73077c](https://github.com/hatayama/unity-cli-loop/commit/d73077c3ee771162043557a4172783bacd2f24a6))
* align Codex classification and add regression tests after [#609](https://github.com/hatayama/unity-cli-loop/issues/609) ([#614](https://github.com/hatayama/unity-cli-loop/issues/614)) ([4eb2432](https://github.com/hatayama/unity-cli-loop/commit/4eb24325a5fa7a6a37d498294eadb9f961533efd))
* Boolean CLI options no longer require true or false values ([#1029](https://github.com/hatayama/unity-cli-loop/issues/1029)) ([91606cf](https://github.com/hatayama/unity-cli-loop/commit/91606cffbf1be9f933e1a0ac0331b3a5aefe6478))
* CLI setup can clean up legacy npm installs ([#1032](https://github.com/hatayama/unity-cli-loop/issues/1032)) ([519359a](https://github.com/hatayama/unity-cli-loop/commit/519359a2aa1ad3c8c25f5db6638b7997d35685f6))
* **cli:** Add launch command to open Unity projects ([#577](https://github.com/hatayama/unity-cli-loop/issues/577)) ([d91c153](https://github.com/hatayama/unity-cli-loop/commit/d91c153f5f2985c06d522e96253879630c5f757f))
* **cli:** auto-sync tools cache when CLI version changes ([#562](https://github.com/hatayama/unity-cli-loop/issues/562)) ([cdb014d](https://github.com/hatayama/unity-cli-loop/commit/cdb014d955077ca21c981b112f3c679533e66ae9))
* **cli:** search child directories for Unity projects with uLoopMCP ([#510](https://github.com/hatayama/unity-cli-loop/issues/510)) ([b83d12a](https://github.com/hatayama/unity-cli-loop/commit/b83d12a316cd685093c107da7a9b5fb63a4bc72d))
* **compile:** Respect Unity's Script Changes While Playing setting ([#582](https://github.com/hatayama/unity-cli-loop/issues/582)) ([d2e0659](https://github.com/hatayama/unity-cli-loop/commit/d2e06596e86ae47ac20dff3e0c21ba1378b8f9b9))
* **execute-dynamic-code:** wrap execution in single Undo group ([#507](https://github.com/hatayama/unity-cli-loop/issues/507)) ([3402d41](https://github.com/hatayama/unity-cli-loop/commit/3402d4132e93021507e39ad2a92f56561f697e2e))
* Extract server status/controls UI into standalone ServerEditorWindow ([#853](https://github.com/hatayama/unity-cli-loop/issues/853)) ([7934fba](https://github.com/hatayama/unity-cli-loop/commit/7934fba02f42e5e042f891b25a0322ea310889fd))
* Git Bash can install and complete uloop on Windows ([#1055](https://github.com/hatayama/unity-cli-loop/issues/1055)) ([7f7c78d](https://github.com/hatayama/unity-cli-loop/commit/7f7c78dddc67a4fbc092aed47fb88864b2df1d3a))
* Implement focus-window at OS level using launch-unity package ([#580](https://github.com/hatayama/unity-cli-loop/issues/580)) ([978896d](https://github.com/hatayama/unity-cli-loop/commit/978896db7188d81b0d99fe692529fc699d6dadfa))
* improve CLI integration UI and replace Auto Start with server state restoration ([#664](https://github.com/hatayama/unity-cli-loop/issues/664)) ([c9ca4d7](https://github.com/hatayama/unity-cli-loop/commit/c9ca4d79cb08390e0a07273b107c277b685b3eb7))
* improve EditorWindow UI — merge server sections, compact controls, add CLI/MCP tabs ([#662](https://github.com/hatayama/unity-cli-loop/issues/662)) ([3301d8c](https://github.com/hatayama/unity-cli-loop/commit/3301d8cb3551f748ab6f5658ed1a006c3a148eac))
* Improve execute-dynamic-code performance by pre-resolving using directives ([#889](https://github.com/hatayama/unity-cli-loop/issues/889)) ([ba94c26](https://github.com/hatayama/unity-cli-loop/commit/ba94c268edaa948d53952ae76baa2c99b8ba1d85))
* improve first-time Setup Wizard skill installation ([#927](https://github.com/hatayama/unity-cli-loop/issues/927)) ([cc65f80](https://github.com/hatayama/unity-cli-loop/commit/cc65f801dc55c2cf09df3af41ccffa5263341c50))
* Improve native CLI maintainability and local validation ([#1042](https://github.com/hatayama/unity-cli-loop/issues/1042)) ([9ab932b](https://github.com/hatayama/unity-cli-loop/commit/9ab932b9c1e104c69682b298c687ec4c2c83efc0))
* improve skill discoverability with better naming and descriptions ([#534](https://github.com/hatayama/unity-cli-loop/issues/534)) ([0c9c2d5](https://github.com/hatayama/unity-cli-loop/commit/0c9c2d5a02793e9b4ee7b6f7e4043a6045fbd8c3))
* Improve uloop skill installation and command help accuracy ([#1028](https://github.com/hatayama/unity-cli-loop/issues/1028)) ([f32ce61](https://github.com/hatayama/unity-cli-loop/commit/f32ce61cbd81610a665ad709e76f190f0a11d1d2))
* Input recording/replay system  ([#814](https://github.com/hatayama/unity-cli-loop/issues/814)) ([d7a7f58](https://github.com/hatayama/unity-cli-loop/commit/d7a7f58096020a76caa4fe04392f96558a91f6c0))
* keyboard simulation ([#783](https://github.com/hatayama/unity-cli-loop/issues/783)) ([8d632c4](https://github.com/hatayama/unity-cli-loop/commit/8d632c4fa55b03b72fdf4ce75feca11e3ffb1060))
* Make Unity tools easier to extend and maintain ([#1063](https://github.com/hatayama/unity-cli-loop/issues/1063)) ([c50f54c](https://github.com/hatayama/unity-cli-loop/commit/c50f54c3afe7cb94985c26eb6eaec910f1ae2cce))
* Migrate dynamic code compilation from Roslyn to AssemblyBuilder with enhanced security ([#829](https://github.com/hatayama/unity-cli-loop/issues/829)) ([0ab2b87](https://github.com/hatayama/unity-cli-loop/commit/0ab2b8768ea286e3aecae8bd866ee72e2550ce48))
* Migrate security settings to project-scoped .uloop/settings.security.json ([#696](https://github.com/hatayama/unity-cli-loop/issues/696)) ([222d46e](https://github.com/hatayama/unity-cli-loop/commit/222d46e485b5b0006f8ed9e74e19e191938f2010))
* migrate skills to Agent Skills specification structure ([#538](https://github.com/hatayama/unity-cli-loop/issues/538)) ([64dea12](https://github.com/hatayama/unity-cli-loop/commit/64dea12d0887e1f1366a7b8ee8a82852fc57fb2d))
* Native CLI errors are easier for agents to recover from ([#1026](https://github.com/hatayama/unity-cli-loop/issues/1026)) ([cb3a563](https://github.com/hatayama/unity-cli-loop/commit/cb3a56327484d25274bf25503713ea2cbee38f17))
* Rebrand to Unity CLI Loop, bump to v1.0.0 ([#769](https://github.com/hatayama/unity-cli-loop/issues/769)) ([34081c5](https://github.com/hatayama/unity-cli-loop/commit/34081c58e8206e68264720c4a8b63683c71b8a0d))
* Rebuild execute-dynamic-code with shared Roslyn compilation and layered architecture ([#901](https://github.com/hatayama/unity-cli-loop/issues/901)) ([f48cdaa](https://github.com/hatayama/unity-cli-loop/commit/f48cdaaee5e3df0f4035a8281a6b7fe8511df04c))
* Remove 3 redundant MCP tools and add Design Philosophy section ([#837](https://github.com/hatayama/unity-cli-loop/issues/837)) ([11412b6](https://github.com/hatayama/unity-cli-loop/commit/11412b6de429023b630a24cd3fbf685ae7274048))
* Replace runtime overlay generation with Prefab-based architecture and improve visualization ([#811](https://github.com/hatayama/unity-cli-loop/issues/811)) ([3ff8b09](https://github.com/hatayama/unity-cli-loop/commit/3ff8b090041360483b6637eae5157e4084121a9c))
* Run uloop commands through project-local CLI ([ede3836](https://github.com/hatayama/unity-cli-loop/commit/ede38362939ebbd3ff2a009bc7ef944886ba8015))
* Setup can install the bundled CLI without downloading it ([#1034](https://github.com/hatayama/unity-cli-loop/issues/1034)) ([11f5882](https://github.com/hatayama/unity-cli-loop/commit/11f5882d604f86e76d58b3455efb7d101c08e689))
* Setup keeps packages smaller while installing Dispatcher on demand ([#1081](https://github.com/hatayama/unity-cli-loop/issues/1081)) ([2896147](https://github.com/hatayama/unity-cli-loop/commit/289614797c011f0c545bfbaf3eeab5dbb51c1e13))
* Setup updates the dispatcher only when projects require it ([#1053](https://github.com/hatayama/unity-cli-loop/issues/1053)) ([20aab00](https://github.com/hatayama/unity-cli-loop/commit/20aab004b6a360573416ddf4648cd0728010aa8d))
* skill installation state is clearer in setup and settings ([#951](https://github.com/hatayama/unity-cli-loop/issues/951)) ([9c4e36c](https://github.com/hatayama/unity-cli-loop/commit/9c4e36cf9902ea9b622535103e73205f80380f3d))
* **skills:** add progressive disclosure and expand execute-dynamic-code examples ([#506](https://github.com/hatayama/unity-cli-loop/issues/506)) ([b63fd13](https://github.com/hatayama/unity-cli-loop/commit/b63fd134b93cbad9d660d7db738b15e6aec99044))
* support CS0103 auto-using resolution ([#641](https://github.com/hatayama/unity-cli-loop/issues/641)) ([a05edf7](https://github.com/hatayama/unity-cli-loop/commit/a05edf7e36e055d76b154df5f35ceb691f724a20))
* Switch CLI skills to dynamic package loading ([#537](https://github.com/hatayama/unity-cli-loop/issues/537)) ([4e48593](https://github.com/hatayama/unity-cli-loop/commit/4e48593f81d76f62c764a88bffbfde56c0dafa35))
* UI automation can see targets clearly and bypass blocked raycasts ([#996](https://github.com/hatayama/unity-cli-loop/issues/996)) ([fe43abe](https://github.com/hatayama/unity-cli-loop/commit/fe43abea6b8e2ce02cc540b3d553b8d07da1ddc0))
* **ui:** migrate McpEditorWindow from IMGUI to UI Toolkit ([#503](https://github.com/hatayama/unity-cli-loop/issues/503)) ([c901024](https://github.com/hatayama/unity-cli-loop/commit/c9010249f4d4ad33930adbcc08a331c20d4ed701))
* uloop runs from native Go binaries ([#1022](https://github.com/hatayama/unity-cli-loop/issues/1022)) ([4e4383d](https://github.com/hatayama/unity-cli-loop/commit/4e4383d95f7f9269f59f3622f3d4f6a384821b4f))
* Unity Menu Commands Consolidated to Dynamic Code Execution ([#994](https://github.com/hatayama/unity-cli-loop/issues/994)) ([ea6c95b](https://github.com/hatayama/unity-cli-loop/commit/ea6c95bb0240872f41a8ab63761657fb7f3d4fc4))
* Unity startup recovery does less blocking work ([#990](https://github.com/hatayama/unity-cli-loop/issues/990)) ([63ce4db](https://github.com/hatayama/unity-cli-loop/commit/63ce4db9ff4c6fa15d0a2a11f99c1b0387fc8a1b))
* Unity tools now connect without port management ([#1018](https://github.com/hatayama/unity-cli-loop/issues/1018)) ([4c76e95](https://github.com/hatayama/unity-cli-loop/commit/4c76e95f2e7a60b18cca312d3e5048619b115c29))
* upgrade launch-unity to v0.15.0 and use orchestrateLaunch() ([#601](https://github.com/hatayama/unity-cli-loop/issues/601)) ([70c64d7](https://github.com/hatayama/unity-cli-loop/commit/70c64d75f73f4ab0f693fb27a5eb6231d74345ee))
* Windows users can follow PowerShell-specific PlayMode automation examples ([#947](https://github.com/hatayama/unity-cli-loop/issues/947)) ([59e50b4](https://github.com/hatayama/unity-cli-loop/commit/59e50b4e26f4e49c08761108737ae5a702a22788))
* Windows users can run terminal-driven E2E checks ([#1054](https://github.com/hatayama/unity-cli-loop/issues/1054)) ([8648984](https://github.com/hatayama/unity-cli-loop/commit/864898472e25bdd485b5f91c481485848ffac441))


### Bug Fixes

* Add GetVersionTool to core package and improve CLI error handling ([#723](https://github.com/hatayama/unity-cli-loop/issues/723)) ([21afcc8](https://github.com/hatayama/unity-cli-loop/commit/21afcc899d7b8857737a7bda9480ccb447f9083c))
* add meta ([#692](https://github.com/hatayama/unity-cli-loop/issues/692)) ([34b29de](https://github.com/hatayama/unity-cli-loop/commit/34b29de1ea497e8755f6c8cf711eebf7b1378f15))
* Add missing .meta files for playmode skill references ([#773](https://github.com/hatayama/unity-cli-loop/issues/773)) ([19dcf5d](https://github.com/hatayama/unity-cli-loop/commit/19dcf5d0b3aa9eeef7de62c9f7003b3457beab7a))
* add README for uloop-cli npm package ([#541](https://github.com/hatayama/unity-cli-loop/issues/541)) ([b8ec5b5](https://github.com/hatayama/unity-cli-loop/commit/b8ec5b5f1418847a32bc8d9b6764b85230adf24a))
* Add uloop fix suggestion to error messages ([#593](https://github.com/hatayama/unity-cli-loop/issues/593)) ([ccca327](https://github.com/hatayama/unity-cli-loop/commit/ccca327ca13996602c3fbe2d4e1c20dc2a8f0ab4))
* AI selects the right tool for selected GameObject inspection ([#1003](https://github.com/hatayama/unity-cli-loop/issues/1003)) ([1621d1a](https://github.com/hatayama/unity-cli-loop/commit/1621d1a2d508bf46878340e6c5df76731be872b9))
* AI-generated uloop commands avoid unnecessary project path arguments ([#929](https://github.com/hatayama/unity-cli-loop/issues/929)) ([be8e350](https://github.com/hatayama/unity-cli-loop/commit/be8e350915fb9c8dc112f195502fbbf2a9c6731a))
* allow installation without the new Input System package ([#938](https://github.com/hatayama/unity-cli-loop/issues/938)) ([b08d899](https://github.com/hatayama/unity-cli-loop/commit/b08d899856d8c760aae7856b33f6a5bb3cc06d7f))
* apply context: fork to uloop-execute-dynamic-code skill and add wiring references ([#743](https://github.com/hatayama/unity-cli-loop/issues/743)) ([4ab452b](https://github.com/hatayama/unity-cli-loop/commit/4ab452ba9efde76704b18d58d403761df2128941))
* Auto-save NUnit XML on test failure ([#600](https://github.com/hatayama/unity-cli-loop/issues/600)) ([defa488](https://github.com/hatayama/unity-cli-loop/commit/defa488b6ee3acf3a15a7558c40ac7ac69bf1b99))
* Avoid false "Unity not running" errors when the editor is still open ([#919](https://github.com/hatayama/unity-cli-loop/issues/919)) ([5376279](https://github.com/hatayama/unity-cli-loop/commit/53762791b70192efcf7fea2fcdf8dc8d91aa0c2d))
* avoid misleading Unity editor availability errors ([#911](https://github.com/hatayama/unity-cli-loop/issues/911)) ([4c2b7a6](https://github.com/hatayama/unity-cli-loop/commit/4c2b7a63bd4a31f0e84fa0964a28c268b33eb9c3))
* change CLI boolean options to value format for MCP consistency ([#559](https://github.com/hatayama/unity-cli-loop/issues/559)) ([3ad62e6](https://github.com/hatayama/unity-cli-loop/commit/3ad62e677f92e39f50bf55606f8aef539678d083))
* change IncludeStackTrace default value from true to false in get-logs ([#557](https://github.com/hatayama/unity-cli-loop/issues/557)) ([8609dbc](https://github.com/hatayama/unity-cli-loop/commit/8609dbc71ca9d37fa11053e95450a6702d89cf3a))
* clarify execute-dynamic-code skill parameters ([#847](https://github.com/hatayama/unity-cli-loop/issues/847)) ([89bbff8](https://github.com/hatayama/unity-cli-loop/commit/89bbff8fbe9107a20981d239a19f7dccf5e5ae34))
* Classify csc.rsp compiler diagnostics correctly in get-logs LogType filter ([#761](https://github.com/hatayama/unity-cli-loop/issues/761)) ([#767](https://github.com/hatayama/unity-cli-loop/issues/767)) ([7069c5f](https://github.com/hatayama/unity-cli-loop/commit/7069c5f5a0b7d646060200373515d65fa8d24809))
* clean up keyboard overlay preview badges ([#813](https://github.com/hatayama/unity-cli-loop/issues/813)) ([4caf06a](https://github.com/hatayama/unity-cli-loop/commit/4caf06a3b7860cd63f0285aae2d39054a3ea7269))
* CLI connections no longer depend on stale local settings ([#1024](https://github.com/hatayama/unity-cli-loop/issues/1024)) ([d20f13c](https://github.com/hatayama/unity-cli-loop/commit/d20f13c38255b28733a23ba3f0371ec1de2e9427))
* CLI lint now gets past existing prettier formatting issues ([#967](https://github.com/hatayama/unity-cli-loop/issues/967)) ([b02b05d](https://github.com/hatayama/unity-cli-loop/commit/b02b05d90e5bf8bfba41ecd4340950751f7fb46c))
* CLI options and skill sync work reliably across platforms ([#1039](https://github.com/hatayama/unity-cli-loop/issues/1039)) ([f080a87](https://github.com/hatayama/unity-cli-loop/commit/f080a87253db8384cbc5e5865eda414ceb0b1acf))
* CLI setup avoids legacy npm checks and shared-install uninstalls ([#1040](https://github.com/hatayama/unity-cli-loop/issues/1040)) ([ec3d91f](https://github.com/hatayama/unity-cli-loop/commit/ec3d91f616b1351c2a886051918f809d985e89a1))
* **cli:** resolve lint warnings and type errors ([#524](https://github.com/hatayama/unity-cli-loop/issues/524)) ([ffe0df6](https://github.com/hatayama/unity-cli-loop/commit/ffe0df616a204991895c5940f593606c23bdd128))
* **cli:** update tsx to 4.21.0 to fix esbuild vulnerability ([#496](https://github.com/hatayama/unity-cli-loop/issues/496)) ([e7240e2](https://github.com/hatayama/unity-cli-loop/commit/e7240e221fa1771d7bb7c28e835f61c2f6e9b2b4))
* Codexのプロジェクト単位設定をサポート ([#609](https://github.com/hatayama/unity-cli-loop/issues/609)) ([3e99d97](https://github.com/hatayama/unity-cli-loop/commit/3e99d97b967e04a8541d50c2b619f9817a11194b))
* compile and log commands recover the Unity server more reliably ([#925](https://github.com/hatayama/unity-cli-loop/issues/925)) ([0f63ed5](https://github.com/hatayama/unity-cli-loop/commit/0f63ed5fdd9dc20ddc6b005dc1c7a4d9d1900090))
* **compile:** report duplicate asmdef and avoid silent hangs ([#529](https://github.com/hatayama/unity-cli-loop/issues/529)) ([f181aaa](https://github.com/hatayama/unity-cli-loop/commit/f181aaaecfdc28c148bbbc371bbf5d969cb96081))
* **config:** preserve non-uLoopMCP servers when saving MCP configuration ([#518](https://github.com/hatayama/unity-cli-loop/issues/518)) ([635ece7](https://github.com/hatayama/unity-cli-loop/commit/635ece7d7096834cb2e6b88ed488aee25e3e0dfc))
* consolidate serverPort and customPort into single customPort field ([#666](https://github.com/hatayama/unity-cli-loop/issues/666)) ([1ffcbdf](https://github.com/hatayama/unity-cli-loop/commit/1ffcbdfc042431a759b6eef8c9e8901ff067d07d))
* convert UTF-8 byte position to character position in get-logs ([#555](https://github.com/hatayama/unity-cli-loop/issues/555)) ([04aaaff](https://github.com/hatayama/unity-cli-loop/commit/04aaaffef518ad1447ca2de6a48234d0cc0eb899))
* correct skill directory mapping to match CLI target-config ([#852](https://github.com/hatayama/unity-cli-loop/issues/852)) ([4818f52](https://github.com/hatayama/unity-cli-loop/commit/4818f528102def6b7fd4d3071bd88e6400f44a12))
* correct SKILL.md file format ([#535](https://github.com/hatayama/unity-cli-loop/issues/535)) ([79ce5d4](https://github.com/hatayama/unity-cli-loop/commit/79ce5d4cd1936f53a3c5f84a21e886f8a925eabd))
* correct SKILL.md parameter docs to match C# implementations ([#689](https://github.com/hatayama/unity-cli-loop/issues/689)) ([e76ab29](https://github.com/hatayama/unity-cli-loop/commit/e76ab29372c28f6513016d11b9fb7cb18e6f025f))
* Deduplicate assembly references by name and add architecture overview to CLAUDE.md ([#887](https://github.com/hatayama/unity-cli-loop/issues/887)) ([cb0415e](https://github.com/hatayama/unity-cli-loop/commit/cb0415ed913be4fcf3cb8b90a3d54e3622cc9489))
* Delete lock files when server startup is skipped ([#595](https://github.com/hatayama/unity-cli-loop/issues/595)) ([9621cc2](https://github.com/hatayama/unity-cli-loop/commit/9621cc21522f0cf43de6013a5d69c4e3891b4d12))
* Delete Unnecessary Files ([#543](https://github.com/hatayama/unity-cli-loop/issues/543)) ([cec1615](https://github.com/hatayama/unity-cli-loop/commit/cec1615b1d1ad9672f98ca0e731d0fe6f607939a))
* **deps:** bump launch-unity to 0.15.1 ([c25653a](https://github.com/hatayama/unity-cli-loop/commit/c25653a590597c2702e13943e9595d71c1b51e73))
* **deps:** upgrade eslint to v10 and resolve minimatch ReDoS vulnerability ([#660](https://github.com/hatayama/unity-cli-loop/issues/660)) ([11e4b8b](https://github.com/hatayama/unity-cli-loop/commit/11e4b8b0cbd80454f52f5ea9586c52a139cb73d9))
* Detect and auto-recover from silent MCP server loop exit ([#871](https://github.com/hatayama/unity-cli-loop/issues/871)) ([d0430c8](https://github.com/hatayama/unity-cli-loop/commit/d0430c8e0ff1d71e37dabef9099c654565368f61))
* disable Skills subsection when CLI is not installed ([#681](https://github.com/hatayama/unity-cli-loop/issues/681)) ([3814154](https://github.com/hatayama/unity-cli-loop/commit/381415449281f955e7c302d56c2a52e2db97dc2b))
* distinguish Downgrade CLI from Update CLI in version mismatch display ([#668](https://github.com/hatayama/unity-cli-loop/issues/668)) ([28c8ead](https://github.com/hatayama/unity-cli-loop/commit/28c8eadf877114a8993c73f104a64a3258f5656d))
* Dynamic code commands recover more cleanly after Unity restarts ([#944](https://github.com/hatayama/unity-cli-loop/issues/944)) ([bdbe286](https://github.com/hatayama/unity-cli-loop/commit/bdbe286d710bb2c7415b4f401d3b65e8c98f9e13))
* Dynamic code execution stays fast with system .NET 6 installed ([#988](https://github.com/hatayama/unity-cli-loop/issues/988)) ([81d4d02](https://github.com/hatayama/unity-cli-loop/commit/81d4d02ebf36f6bbbcea6a01dd9aba845426f13f))
* Dynamic code starts reliably on Windows PCs ([#1006](https://github.com/hatayama/unity-cli-loop/issues/1006)) ([68893da](https://github.com/hatayama/unity-cli-loop/commit/68893da351df60be7af3300f668d5ab111cd5be2))
* ensure server recovery after domain reload when instance is null ([#551](https://github.com/hatayama/unity-cli-loop/issues/551)) ([ad5abc6](https://github.com/hatayama/unity-cli-loop/commit/ad5abc62410ed8e8503d8fbecbf9332f6f6ecba8))
* Ensure server.bundle.js is committed on all PRs ([#597](https://github.com/hatayama/unity-cli-loop/issues/597)) ([7f55387](https://github.com/hatayama/unity-cli-loop/commit/7f5538717284300f59eab845e9294a1f3833693d))
* Fix CLI port resolution when serverPort is invalid ([#619](https://github.com/hatayama/unity-cli-loop/issues/619)) ([8ddc4a5](https://github.com/hatayama/unity-cli-loop/commit/8ddc4a5222f624571fc10912269f18f8b2f86bae))
* Fix incomplete property serialization in ComponentPropertySerializer ([#691](https://github.com/hatayama/unity-cli-loop/issues/691)) ([2db6eb3](https://github.com/hatayama/unity-cli-loop/commit/2db6eb3f63865ab6e56c41cabbbe3a37d183b164))
* Fix submenu misrender in SkillsTarget dropdown ([#787](https://github.com/hatayama/unity-cli-loop/issues/787)) ([28731df](https://github.com/hatayama/unity-cli-loop/commit/28731df6c48681d445e47e9bc38eb521b9fd7b23))
* **focus-window:** remove platform-specific preprocessor directives ([#513](https://github.com/hatayama/unity-cli-loop/issues/513)) ([bcf1894](https://github.com/hatayama/unity-cli-loop/commit/bcf18945d4c7164375dcc304bcc3197753e7339e))
* Group help commands by category in uloop -h ([#708](https://github.com/hatayama/unity-cli-loop/issues/708)) ([dbfe539](https://github.com/hatayama/unity-cli-loop/commit/dbfe5394172a80f550304b95787d36d4046d0404))
* handle Dictionary type as object in schema and CLI value conversion ([#564](https://github.com/hatayama/unity-cli-loop/issues/564)) ([2047ba9](https://github.com/hatayama/unity-cli-loop/commit/2047ba93febbf90f7a41b228ad8631f6ddf3ca91))
* handle JSON array format in CLI default values ([#560](https://github.com/hatayama/unity-cli-loop/issues/560)) ([e194347](https://github.com/hatayama/unity-cli-loop/commit/e1943478d7faea0a5f01adc4d8b8f0b63a75cd5e))
* Handle Win32Exception in skills install process start ([#730](https://github.com/hatayama/unity-cli-loop/issues/730)) ([cd7a4a3](https://github.com/hatayama/unity-cli-loop/commit/cd7a4a3a19ab1d4830d1131074caeab4e85969e1))
* Hide --port option from help, docs, and skill descriptions ([#873](https://github.com/hatayama/unity-cli-loop/issues/873)) ([eec4831](https://github.com/hatayama/unity-cli-loop/commit/eec48313e3c17d12de424d1eeab3b1a967b1c346))
* Hide disabled tools from CLI help and shell completion output ([#705](https://github.com/hatayama/unity-cli-loop/issues/705)) ([3f49750](https://github.com/hatayama/unity-cli-loop/commit/3f4975078e324b56e4363c510c6081b7373afa63))
* improve CLI connection error message for better user experience ([#544](https://github.com/hatayama/unity-cli-loop/issues/544)) ([ac62293](https://github.com/hatayama/unity-cli-loop/commit/ac622936a574bd15d599478c61614d8b93fc3d5d))
* Improve code execution responsiveness after Unity recompiles ([#1070](https://github.com/hatayama/unity-cli-loop/issues/1070)) ([7b972b4](https://github.com/hatayama/unity-cli-loop/commit/7b972b4cc9c40ea10e31f7d808d761fe6a9f6b3f))
* improve server recovery fallback when saved port is unavailable ([#639](https://github.com/hatayama/unity-cli-loop/issues/639)) ([1ee5e6c](https://github.com/hatayama/unity-cli-loop/commit/1ee5e6ce97b2f88668d61743d9037c6d6664ffbf))
* improve skill descriptions for better AI recognition ([#553](https://github.com/hatayama/unity-cli-loop/issues/553)) ([3febc3a](https://github.com/hatayama/unity-cli-loop/commit/3febc3affb9e0c63dc98294ec508fa909ed8bb9b))
* improve timeout error message to prevent AI from killing Unity processes ([#567](https://github.com/hatayama/unity-cli-loop/issues/567)) ([208c0e0](https://github.com/hatayama/unity-cli-loop/commit/208c0e0167e6c44ff5a72da82c612321b5ec162c))
* Improve tool settings UI message to mention context window benefit ([#700](https://github.com/hatayama/unity-cli-loop/issues/700)) ([040c8bd](https://github.com/hatayama/unity-cli-loop/commit/040c8bd8687d8925219285f3b401c3d6900a5f3e))
* Improve uloop skill guidance and hide internal CLI metadata ([#993](https://github.com/hatayama/unity-cli-loop/issues/993)) ([ec2ba7a](https://github.com/hatayama/unity-cli-loop/commit/ec2ba7a09a9484d4869b1d7aff5338dca9a272d6))
* improve Windows error message clarity and remove CLI install success dialog ([#678](https://github.com/hatayama/unity-cli-loop/issues/678)) ([9052eba](https://github.com/hatayama/unity-cli-loop/commit/9052ebaea8afa0c580295302a7ae75d190ebc3ee))
* invalid EditMode test requests now return a clear error during play mode ([#940](https://github.com/hatayama/unity-cli-loop/issues/940)) ([ed0c7ea](https://github.com/hatayama/unity-cli-loop/commit/ed0c7eaebfa5430fbdf95bfbd77f36e3fa500f2c))
* isolate Roslyn dependencies via shared registry ([#741](https://github.com/hatayama/unity-cli-loop/issues/741)) ([c96a3a2](https://github.com/hatayama/unity-cli-loop/commit/c96a3a2e06a268cc4daa5cb0eb9936b638abf5bb))
* keep launch startup feedback visible while Unity becomes ready ([#975](https://github.com/hatayama/unity-cli-loop/issues/975)) ([fce0d09](https://github.com/hatayama/unity-cli-loop/commit/fce0d090bd7f5c4506325ba4f6ba58803fe68986))
* Launch now waits cleanly without false startup warnings ([#974](https://github.com/hatayama/unity-cli-loop/issues/974)) ([f873afc](https://github.com/hatayama/unity-cli-loop/commit/f873afcd24ef0fd5c4a061809b2e4b51849377a7))
* Launch now waits for the correct Unity project after startup ([#965](https://github.com/hatayama/unity-cli-loop/issues/965)) ([1ebbf62](https://github.com/hatayama/unity-cli-loop/commit/1ebbf62dcf0cf5fb1c97b3acb71e80694e004cf4))
* Launch progress now stays visible without leaving spinner artifacts ([#973](https://github.com/hatayama/unity-cli-loop/issues/973)) ([97b4450](https://github.com/hatayama/unity-cli-loop/commit/97b445066d78ae49efba8e607b10c2f7fa0032f6))
* Make dynamic code compilation easier to maintain ([#1007](https://github.com/hatayama/unity-cli-loop/issues/1007)) ([b966dba](https://github.com/hatayama/unity-cli-loop/commit/b966dbac38bbdaaea87c0d1a62493d9c2b08e195))
* make MCP deprecation easier to notice in the settings window ([#948](https://github.com/hatayama/unity-cli-loop/issues/948)) ([0ace70d](https://github.com/hatayama/unity-cli-loop/commit/0ace70d944fca67172ce24d3bca84b91a76c36a0))
* make the setup and settings windows easier to use ([#932](https://github.com/hatayama/unity-cli-loop/issues/932)) ([6d61a7d](https://github.com/hatayama/unity-cli-loop/commit/6d61a7dccc418b2c8a41cab4132accf27780afd9))
* narrow EditorDialog preprocessor guard to UNITY_6000_3_OR_NEWER ([#804](https://github.com/hatayama/unity-cli-loop/issues/804)) ([65fce9e](https://github.com/hatayama/unity-cli-loop/commit/65fce9e0ce67fc40cbc818322c175de0aeefd889))
* normalize get-logs error filtering and harden CLI e2e parsing ([#643](https://github.com/hatayama/unity-cli-loop/issues/643)) ([cc42ba9](https://github.com/hatayama/unity-cli-loop/commit/cc42ba9f98e4c01ba82b7e7e79e39a826d674c30))
* prevent background Unity processes from mutating server state ([#642](https://github.com/hatayama/unity-cli-loop/issues/642)) ([cdf1285](https://github.com/hatayama/unity-cli-loop/commit/cdf1285f41165fec080227139b777f6b6fff83a8))
* Prevent CLI from connecting to wrong Unity instance via stale port ([#875](https://github.com/hatayama/unity-cli-loop/issues/875)) ([2e577c8](https://github.com/hatayama/unity-cli-loop/commit/2e577c834e23706bc692d2303d3db417c6ef6098))
* Prevent CLI from sending commands to wrong Unity instance ([#719](https://github.com/hatayama/unity-cli-loop/issues/719)) ([d0e47b3](https://github.com/hatayama/unity-cli-loop/commit/d0e47b392677af36dfba740e9c7e80579877bad8))
* prevent installation errors when the Unity Test Framework package is missing ([#939](https://github.com/hatayama/unity-cli-loop/issues/939)) ([958e4b5](https://github.com/hatayama/unity-cli-loop/commit/958e4b5050f64a47dff00575c670ebf5a9ad3196))
* prevent JSON corruption from concurrent SaveSettings writes ([#603](https://github.com/hatayama/unity-cli-loop/issues/603)) ([763addc](https://github.com/hatayama/unity-cli-loop/commit/763addcf088731fd05bc7997705d1a53acfb8d54))
* Prevent rename migration from shadowing legacy settings migration ([#725](https://github.com/hatayama/unity-cli-loop/issues/725)) ([b03337b](https://github.com/hatayama/unity-cli-loop/commit/b03337be8892cc807e8f6a47beab023c7d985bc9))
* Prevent SetupWizardWindow from showing for existing users on upgrade ([#857](https://github.com/hatayama/unity-cli-loop/issues/857)) ([c9d4346](https://github.com/hatayama/unity-cli-loop/commit/c9d43466dfb52d074e609631eac8b9b1876565e8))
* Prevent SetupWizardWindow from showing on package upgrade ([#861](https://github.com/hatayama/unity-cli-loop/issues/861)) ([358a32f](https://github.com/hatayama/unity-cli-loop/commit/358a32f8b8821d02e529be104762a3fc59de45a6))
* Prevent toggle ChangeEvent from collapsing parent Foldout ([#721](https://github.com/hatayama/unity-cli-loop/issues/721)) ([f6caf9b](https://github.com/hatayama/unity-cli-loop/commit/f6caf9b7066f5f4587788c7e3b2d9e8ce0062aa3))
* prioritize .cmd/.exe over extensionless shims in Windows executable detection ([#669](https://github.com/hatayama/unity-cli-loop/issues/669)) ([25fc66b](https://github.com/hatayama/unity-cli-loop/commit/25fc66bb2462fb1fb6643b84e449de3c0be1a20e))
* README pages now show the refreshed logo ([#943](https://github.com/hatayama/unity-cli-loop/issues/943)) ([d47c7b1](https://github.com/hatayama/unity-cli-loop/commit/d47c7b1d2c76c5444cdba8c4afa2f3098776312d))
* refine MCP tool settings UI ([#836](https://github.com/hatayama/unity-cli-loop/issues/836)) ([16c3c87](https://github.com/hatayama/unity-cli-loop/commit/16c3c87e3b34ca1c3750f0923ec2ecbbfe27d82f))
* remove deprecation warning from uloop update ([#913](https://github.com/hatayama/unity-cli-loop/issues/913)) ([61ed144](https://github.com/hatayama/unity-cli-loop/commit/61ed144ffa1c0b8e2b6a10cb9eb587dada3fc1c6))
* Remove HideFlags.DontSave from overlay canvas to fix PlayMode exit cleanup ([#812](https://github.com/hatayama/unity-cli-loop/issues/812)) ([3957641](https://github.com/hatayama/unity-cli-loop/commit/395764134e80c131b6d70a63ce7806c2fd578032))
* Remove redundant .meta files causing import warnings ([#802](https://github.com/hatayama/unity-cli-loop/issues/802)) ([7d662cd](https://github.com/hatayama/unity-cli-loop/commit/7d662cd9d5de78204339903ee8e0c7521cba40dc))
* Rename capture-window MCP tool to screenshot ([#701](https://github.com/hatayama/unity-cli-loop/issues/701)) ([b178bc8](https://github.com/hatayama/unity-cli-loop/commit/b178bc899a87c9ad4fce86adc27611c56a9f2811))
* Rename settings.security.json to settings.permissions.json ([#713](https://github.com/hatayama/unity-cli-loop/issues/713)) ([ae1a21a](https://github.com/hatayama/unity-cli-loop/commit/ae1a21af2a575a94fd96476feb2fe05bfbb77f88))
* replay progress bar always appearing at 100% ([#839](https://github.com/hatayama/unity-cli-loop/issues/839)) ([2880453](https://github.com/hatayama/unity-cli-loop/commit/2880453c747fd66092081f97982867368fdb8997))
* require manual server start on first launch ([#547](https://github.com/hatayama/unity-cli-loop/issues/547)) ([fb29ddb](https://github.com/hatayama/unity-cli-loop/commit/fb29ddb85abf9f1159e5733489e9de20effe66c3))
* require skills directories for auto-install detection ([#912](https://github.com/hatayama/unity-cli-loop/issues/912)) ([8832050](https://github.com/hatayama/unity-cli-loop/commit/8832050f7f4d9b94072ee33be2be0aeba90edf44))
* restore separate skill install targets for Claude, Cursor, Gemini, Codex, and .agents ([#950](https://github.com/hatayama/unity-cli-loop/issues/950)) ([8f583b9](https://github.com/hatayama/unity-cli-loop/commit/8f583b9f09326cf4b34bae0066369d07aaf98449))
* restore Unity settings after interrupted atomic writes ([#898](https://github.com/hatayama/unity-cli-loop/issues/898)) ([887800d](https://github.com/hatayama/unity-cli-loop/commit/887800dc18caa695ee3926827ac51dfe7a18183a))
* restructure READMEs to CLI-first layout with streamlined content ([#615](https://github.com/hatayama/unity-cli-loop/issues/615)) ([05788e9](https://github.com/hatayama/unity-cli-loop/commit/05788e98d1a73182dd9d6b71828d2f45c6ef9656))
* **server:** enable auto-start without opening EditorWindow ([#500](https://github.com/hatayama/unity-cli-loop/issues/500)) ([5766bdb](https://github.com/hatayama/unity-cli-loop/commit/5766bdba683fec2f1fe385c0e8d747e5e3e1d066))
* Settings no longer shows an unnecessary third-party tools permission ([#1020](https://github.com/hatayama/unity-cli-loop/issues/1020)) ([1779170](https://github.com/hatayama/unity-cli-loop/commit/1779170b8e1b5807fd28dd10a2505d26e7c89530))
* Settings no longer shows obsolete connected client details ([#1060](https://github.com/hatayama/unity-cli-loop/issues/1060)) ([b66790f](https://github.com/hatayama/unity-cli-loop/commit/b66790fb3b2bc89512c8cb595a2698f5bfc7d68b))
* Settings opens faster and tool toggles stay scoped ([#992](https://github.com/hatayama/unity-cli-loop/issues/992)) ([26745ad](https://github.com/hatayama/unity-cli-loop/commit/26745ade840f6cdd6322719fa83cc4de473b1b65))
* Setup and Settings now clean up grouped skill folders and avoid rerunning skill checks after CLI updates ([#978](https://github.com/hatayama/unity-cli-loop/issues/978)) ([51fe1f5](https://github.com/hatayama/unity-cli-loop/commit/51fe1f5384a7596823fceaaa1c583e788a352196))
* Setup and Settings now install skills directly under skills/ ([#977](https://github.com/hatayama/unity-cli-loop/issues/977)) ([65670c5](https://github.com/hatayama/unity-cli-loop/commit/65670c53d0ef050375d1df2bbe1cec13689b7a02))
* Setup no longer shows first-time install steps after updates ([#968](https://github.com/hatayama/unity-cli-loop/issues/968)) ([348a7d4](https://github.com/hatayama/unity-cli-loop/commit/348a7d40cb23ca03de377911c07d1ea8d1f5b419))
* Setup no longer shows unconfigured skill targets as missing ([#963](https://github.com/hatayama/unity-cli-loop/issues/963)) ([6921a88](https://github.com/hatayama/unity-cli-loop/commit/6921a88ba416c080ec5626022cbc440f7f57e8ed))
* Setup now keeps third-party skills when switching folder layouts ([#980](https://github.com/hatayama/unity-cli-loop/issues/980)) ([adfc628](https://github.com/hatayama/unity-cli-loop/commit/adfc62852b8f79b46ca02c6ed6f0fc64c1d000db))
* Setup now recognizes existing skill folders instead of showing them as missing ([#954](https://github.com/hatayama/unity-cli-loop/issues/954)) ([1860e1d](https://github.com/hatayama/unity-cli-loop/commit/1860e1d3658c2305c6931dee22601914de348d32))
* Setup now upgrades to the native CLI cleanly ([#1084](https://github.com/hatayama/unity-cli-loop/issues/1084)) ([5d43688](https://github.com/hatayama/unity-cli-loop/commit/5d43688fd3272bd1bfad109dd217762b1077b67a))
* Setup updates no longer show the first-run skills screen ([#964](https://github.com/hatayama/unity-cli-loop/issues/964)) ([1ea7733](https://github.com/hatayama/unity-cli-loop/commit/1ea773337b930aac8b9634320f4946dee4f2ed40))
* setup wizard no longer gets stuck checking skills ([#952](https://github.com/hatayama/unity-cli-loop/issues/952)) ([eedc24c](https://github.com/hatayama/unity-cli-loop/commit/eedc24c38f04bd759f58041eceb8e2ace71237ae))
* setup wizard no longer reappears or installs skills too eagerly ([#922](https://github.com/hatayama/unity-cli-loop/issues/922)) ([1515486](https://github.com/hatayama/unity-cli-loop/commit/151548673ea20895fb92d45c6153a487d488dd7b))
* Setup Wizard now shows skill status during CLI updates ([#953](https://github.com/hatayama/unity-cli-loop/issues/953)) ([a861a3b](https://github.com/hatayama/unity-cli-loop/commit/a861a3b973652ab211ab344b604669d7b025e241))
* show launch progress while Unity is starting ([#955](https://github.com/hatayama/unity-cli-loop/issues/955)) ([9514cc9](https://github.com/hatayama/unity-cli-loop/commit/9514cc90dc1427452ff3f998bb5b1e956901e86d))
* show the setup wizard when the package version changes ([#920](https://github.com/hatayama/unity-cli-loop/issues/920)) ([4f99cbc](https://github.com/hatayama/unity-cli-loop/commit/4f99cbc777d5eb1867815a3f22965205f0e01a69))
* simplify timeout error message by removing verbose AI instructions ([#670](https://github.com/hatayama/unity-cli-loop/issues/670)) ([97780a4](https://github.com/hatayama/unity-cli-loop/commit/97780a4489ad7fcbbd191f7d18aec28758136260))
* Skill improvements for selected Hierarchy inspection ([#1002](https://github.com/hatayama/unity-cli-loop/issues/1002)) ([626f67c](https://github.com/hatayama/unity-cli-loop/commit/626f67c1841c74650185b37c3d94efe6c2ce4367))
* Skip auto-sync for version/help flags and update command ([#624](https://github.com/hatayama/unity-cli-loop/issues/624)) ([43db6e5](https://github.com/hatayama/unity-cli-loop/commit/43db6e5d524673ae030cc4136a1e592b0cd306ea))
* Skip auto-sync when running launch command ([#621](https://github.com/hatayama/unity-cli-loop/issues/621)) ([b3e4ee8](https://github.com/hatayama/unity-cli-loop/commit/b3e4ee88d158f0b4716153a3b32dc2dd0c5cac75))
* stabilize npm publishing and remove CLI bin warning ([#918](https://github.com/hatayama/unity-cli-loop/issues/918)) ([1b7a7f1](https://github.com/hatayama/unity-cli-loop/commit/1b7a7f12865767f5faf27529c5aa7a8502cb9a42))
* stabilize Tool Settings toggle interaction during UI refresh ([#702](https://github.com/hatayama/unity-cli-loop/issues/702)) ([6b4ba25](https://github.com/hatayama/unity-cli-loop/commit/6b4ba25d2d7821abecf0a990c7021a9c5de64518))
* stabilize Windows native CLI install flow ([#1038](https://github.com/hatayama/unity-cli-loop/issues/1038)) ([d829c16](https://github.com/hatayama/unity-cli-loop/commit/d829c16f14b6841b8d9b4761b08b1f2a6a330ef7))
* Standardize SKILL.md descriptions and reduce verbosity ([#733](https://github.com/hatayama/unity-cli-loop/issues/733)) ([a743607](https://github.com/hatayama/unity-cli-loop/commit/a743607a9bfa6ecdf5c31946f3776f662f4e0e15))
* suppress idle overlay reactivation after mouse release during input replay ([#827](https://github.com/hatayama/unity-cli-loop/issues/827)) ([7133e9c](https://github.com/hatayama/unity-cli-loop/commit/7133e9c1a53473b25bcdf6f5712d5806c2147da6))
* suppress unnecessary 'Multiple Unity projects' warning for non-project commands ([#671](https://github.com/hatayama/unity-cli-loop/issues/671)) ([36bc1ca](https://github.com/hatayama/unity-cli-loop/commit/36bc1cae5b5a9b59c266f4e76ef5011c5dbd5983))
* Sync lint-staged version in package.json with package-lock.json ([#735](https://github.com/hatayama/unity-cli-loop/issues/735)) ([60eff43](https://github.com/hatayama/unity-cli-loop/commit/60eff43cf8762f1075b3cb8e214d483aaf55af6e))
* Test runs avoid unsaved editor-change prompts ([#998](https://github.com/hatayama/unity-cli-loop/issues/998)) ([1db7c37](https://github.com/hatayama/unity-cli-loop/commit/1db7c376d8bf78a50e41abb4a4ead4b5bee40077))
* **ui:** replace hardcoded package paths with dynamic resolution ([#516](https://github.com/hatayama/unity-cli-loop/issues/516)) ([18becbc](https://github.com/hatayama/unity-cli-loop/commit/18becbc7221c87dbb2da0ae49b11a5cf21f8a86c))
* uloop launch docs now explain Unity startup waiting ([#997](https://github.com/hatayama/unity-cli-loop/issues/997)) ([bef8f2f](https://github.com/hatayama/unity-cli-loop/commit/bef8f2fa8cfd69cea58b66c0060baa8801dd1d45))
* unblock TypeScript server dependency updates by resolving npm audit findings ([#907](https://github.com/hatayama/unity-cli-loop/issues/907)) ([ffa3863](https://github.com/hatayama/unity-cli-loop/commit/ffa386365f31a8e908d2059e986218e1b113bd70))
* Unity launch and compile waits now finish reliably ([#1027](https://github.com/hatayama/unity-cli-loop/issues/1027)) ([9c94ec4](https://github.com/hatayama/unity-cli-loop/commit/9c94ec41373ea9dd972114d44ca5b021797f21b9))
* Update hono and @hono/node-server to resolve high severity vulnerabilities ([#731](https://github.com/hatayama/unity-cli-loop/issues/731)) ([135044a](https://github.com/hatayama/unity-cli-loop/commit/135044ae761ef50144fb3a23c5118d6e53367ffe))
* update qs to 6.14.1 to fix DoS vulnerability ([#501](https://github.com/hatayama/unity-cli-loop/issues/501)) ([04a9adc](https://github.com/hatayama/unity-cli-loop/commit/04a9adca6b92893f795b0ac23c9ca398ead680cd))
* update READMEs with --project-path option and comprehensive CLI reference ([#687](https://github.com/hatayama/unity-cli-loop/issues/687)) ([7829c9d](https://github.com/hatayama/unity-cli-loop/commit/7829c9dc6e21bc117b353251b71eab7bbe95b942))
* update repository URLs from uLoopMCP to unity-cli-loop ([#779](https://github.com/hatayama/unity-cli-loop/issues/779)) ([35e56a0](https://github.com/hatayama/unity-cli-loop/commit/35e56a0751f0ec0a57b025f9a539d5918328ba0b))
* update skill documentation for new Skill folder structure ([#539](https://github.com/hatayama/unity-cli-loop/issues/539)) ([45f1bd4](https://github.com/hatayama/unity-cli-loop/commit/45f1bd4cd007b9457fb6eab3c9f319698ce16aea))
* Use informational dialog icon for skill installation success on Unity 6+ ([#797](https://github.com/hatayama/unity-cli-loop/issues/797)) ([0326b38](https://github.com/hatayama/unity-cli-loop/commit/0326b38b1bd3e0089d17744a043b1fb38a7943fa))
* use interactive login shell for executable detection to match terminal environment ([#667](https://github.com/hatayama/unity-cli-loop/issues/667)) ([13ac67a](https://github.com/hatayama/unity-cli-loop/commit/13ac67a001cf57454ac95c363fa0c07361d8288e))
* Windows CLI build support ([#794](https://github.com/hatayama/unity-cli-loop/issues/794)) ([ba7d382](https://github.com/hatayama/unity-cli-loop/commit/ba7d3823762b8bc11ebe57e64caf84d1b86a5faa))

## [3.0.0-beta.4](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.3...v3.0.0-beta.4) (2026-05-10)


### Bug Fixes

* Setup now upgrades to the native CLI cleanly ([#1084](https://github.com/hatayama/unity-cli-loop/issues/1084)) ([5d43688](https://github.com/hatayama/unity-cli-loop/commit/5d43688fd3272bd1bfad109dd217762b1077b67a))

## [3.0.0-beta.3](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.2...v3.0.0-beta.3) (2026-05-09)


### Features

* Setup keeps packages smaller while installing Dispatcher on demand ([#1081](https://github.com/hatayama/unity-cli-loop/issues/1081)) ([2896147](https://github.com/hatayama/unity-cli-loop/commit/289614797c011f0c545bfbaf3eeab5dbb51c1e13))

## [3.0.0-beta.2](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.1...v3.0.0-beta.2) (2026-05-08)


### Features

* Git Bash can install and complete uloop on Windows ([#1055](https://github.com/hatayama/unity-cli-loop/issues/1055)) ([7f7c78d](https://github.com/hatayama/unity-cli-loop/commit/7f7c78dddc67a4fbc092aed47fb88864b2df1d3a))
* Improve native CLI maintainability and local validation ([#1042](https://github.com/hatayama/unity-cli-loop/issues/1042)) ([9ab932b](https://github.com/hatayama/unity-cli-loop/commit/9ab932b9c1e104c69682b298c687ec4c2c83efc0))
* Make Unity tools easier to extend and maintain ([#1063](https://github.com/hatayama/unity-cli-loop/issues/1063)) ([c50f54c](https://github.com/hatayama/unity-cli-loop/commit/c50f54c3afe7cb94985c26eb6eaec910f1ae2cce))
* Setup updates the dispatcher only when projects require it ([#1053](https://github.com/hatayama/unity-cli-loop/issues/1053)) ([20aab00](https://github.com/hatayama/unity-cli-loop/commit/20aab004b6a360573416ddf4648cd0728010aa8d))
* Windows users can run terminal-driven E2E checks ([#1054](https://github.com/hatayama/unity-cli-loop/issues/1054)) ([8648984](https://github.com/hatayama/unity-cli-loop/commit/864898472e25bdd485b5f91c481485848ffac441))


### Bug Fixes

* Improve code execution responsiveness after Unity recompiles ([#1070](https://github.com/hatayama/unity-cli-loop/issues/1070)) ([7b972b4](https://github.com/hatayama/unity-cli-loop/commit/7b972b4cc9c40ea10e31f7d808d761fe6a9f6b3f))
* Settings no longer shows obsolete connected client details ([#1060](https://github.com/hatayama/unity-cli-loop/issues/1060)) ([b66790f](https://github.com/hatayama/unity-cli-loop/commit/b66790fb3b2bc89512c8cb595a2698f5bfc7d68b))

## [3.0.0-beta.1](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.0...v3.0.0-beta.1) (2026-05-03)


### Features

* Setup can install the bundled CLI without downloading it ([#1034](https://github.com/hatayama/unity-cli-loop/issues/1034)) ([11f5882](https://github.com/hatayama/unity-cli-loop/commit/11f5882d604f86e76d58b3455efb7d101c08e689))


### Bug Fixes

* CLI options and skill sync work reliably across platforms ([#1039](https://github.com/hatayama/unity-cli-loop/issues/1039)) ([f080a87](https://github.com/hatayama/unity-cli-loop/commit/f080a87253db8384cbc5e5865eda414ceb0b1acf))
* CLI setup avoids legacy npm checks and shared-install uninstalls ([#1040](https://github.com/hatayama/unity-cli-loop/issues/1040)) ([ec3d91f](https://github.com/hatayama/unity-cli-loop/commit/ec3d91f616b1351c2a886051918f809d985e89a1))
* stabilize Windows native CLI install flow ([#1038](https://github.com/hatayama/unity-cli-loop/issues/1038)) ([d829c16](https://github.com/hatayama/unity-cli-loop/commit/d829c16f14b6841b8d9b4761b08b1f2a6a330ef7))

## [2.1.0](https://github.com/hatayama/unity-cli-loop/compare/v2.0.4...v2.1.0) (2026-04-29)


### Features

* UI automation can see targets clearly and bypass blocked raycasts ([#996](https://github.com/hatayama/unity-cli-loop/issues/996)) ([fe43abe](https://github.com/hatayama/unity-cli-loop/commit/fe43abea6b8e2ce02cc540b3d553b8d07da1ddc0))
* Unity Menu Commands Consolidated to Dynamic Code Execution ([#994](https://github.com/hatayama/unity-cli-loop/issues/994)) ([ea6c95b](https://github.com/hatayama/unity-cli-loop/commit/ea6c95bb0240872f41a8ab63761657fb7f3d4fc4))
* Unity startup recovery does less blocking work ([#990](https://github.com/hatayama/unity-cli-loop/issues/990)) ([63ce4db](https://github.com/hatayama/unity-cli-loop/commit/63ce4db9ff4c6fa15d0a2a11f99c1b0387fc8a1b))


### Bug Fixes

* AI selects the right tool for selected GameObject inspection ([#1003](https://github.com/hatayama/unity-cli-loop/issues/1003)) ([1621d1a](https://github.com/hatayama/unity-cli-loop/commit/1621d1a2d508bf46878340e6c5df76731be872b9))
* Dynamic code starts reliably on Windows PCs ([#1006](https://github.com/hatayama/unity-cli-loop/issues/1006)) ([68893da](https://github.com/hatayama/unity-cli-loop/commit/68893da351df60be7af3300f668d5ab111cd5be2))
* Improve uloop skill guidance and hide internal CLI metadata ([#993](https://github.com/hatayama/unity-cli-loop/issues/993)) ([ec2ba7a](https://github.com/hatayama/unity-cli-loop/commit/ec2ba7a09a9484d4869b1d7aff5338dca9a272d6))
* Make dynamic code compilation easier to maintain ([#1007](https://github.com/hatayama/unity-cli-loop/issues/1007)) ([b966dba](https://github.com/hatayama/unity-cli-loop/commit/b966dbac38bbdaaea87c0d1a62493d9c2b08e195))
* Settings opens faster and tool toggles stay scoped ([#992](https://github.com/hatayama/unity-cli-loop/issues/992)) ([26745ad](https://github.com/hatayama/unity-cli-loop/commit/26745ade840f6cdd6322719fa83cc4de473b1b65))
* Skill improvements for selected Hierarchy inspection ([#1002](https://github.com/hatayama/unity-cli-loop/issues/1002)) ([626f67c](https://github.com/hatayama/unity-cli-loop/commit/626f67c1841c74650185b37c3d94efe6c2ce4367))
* Test runs avoid unsaved editor-change prompts ([#998](https://github.com/hatayama/unity-cli-loop/issues/998)) ([1db7c37](https://github.com/hatayama/unity-cli-loop/commit/1db7c376d8bf78a50e41abb4a4ead4b5bee40077))
* uloop launch docs now explain Unity startup waiting ([#997](https://github.com/hatayama/unity-cli-loop/issues/997)) ([bef8f2f](https://github.com/hatayama/unity-cli-loop/commit/bef8f2fa8cfd69cea58b66c0060baa8801dd1d45))

## [2.0.4](https://github.com/hatayama/unity-cli-loop/compare/v2.0.3...v2.0.4) (2026-04-24)


### Bug Fixes

* Dynamic code execution stays fast with system .NET 6 installed ([#988](https://github.com/hatayama/unity-cli-loop/issues/988)) ([81d4d02](https://github.com/hatayama/unity-cli-loop/commit/81d4d02ebf36f6bbbcea6a01dd9aba845426f13f))

## [2.0.3](https://github.com/hatayama/unity-cli-loop/compare/v2.0.2...v2.0.3) (2026-04-22)


### Bug Fixes

* Setup now keeps third-party skills when switching folder layouts ([#980](https://github.com/hatayama/unity-cli-loop/issues/980)) ([adfc628](https://github.com/hatayama/unity-cli-loop/commit/adfc62852b8f79b46ca02c6ed6f0fc64c1d000db))

## [2.0.2](https://github.com/hatayama/unity-cli-loop/compare/v2.0.1...v2.0.2) (2026-04-22)


### Bug Fixes

* Setup and Settings now clean up grouped skill folders and avoid rerunning skill checks after CLI updates ([#978](https://github.com/hatayama/unity-cli-loop/issues/978)) ([51fe1f5](https://github.com/hatayama/unity-cli-loop/commit/51fe1f5384a7596823fceaaa1c583e788a352196))

## [2.0.1](https://github.com/hatayama/unity-cli-loop/compare/v2.0.0...v2.0.1) (2026-04-22)


### Bug Fixes

* improve `uloop launch` startup behavior by keeping launch progress visible and waiting for the correct Unity project without false warnings ([#965](https://github.com/hatayama/unity-cli-loop/issues/965), [#973](https://github.com/hatayama/unity-cli-loop/issues/973), [#974](https://github.com/hatayama/unity-cli-loop/issues/974), [#975](https://github.com/hatayama/unity-cli-loop/issues/975))
* remove the skill grouping option in Setup and Settings so skills install directly under `skills/`, and stop showing first-run setup screens during updates ([#964](https://github.com/hatayama/unity-cli-loop/issues/964), [#968](https://github.com/hatayama/unity-cli-loop/issues/968), [#977](https://github.com/hatayama/unity-cli-loop/issues/977))

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
