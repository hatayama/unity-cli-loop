# Changelog

## [3.0.1](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0...dispatcher-v3.0.1) (2026-08-31)


### Bug Fixes

* show the dispatcher version for interactive uloop -v inside V2 and V3 projects ([#2467](https://github.com/hatayama/unity-cli-loop/issues/2467)) ([9c79487](https://github.com/hatayama/unity-cli-loop/commit/9c79487224624bf52011867c5f57fbb9130fb283))

## [3.0.0](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.37...dispatcher-v3.0.0) (2026-08-31)


### Miscellaneous Chores

* force release 3.0.0 ([6233aa5](https://github.com/hatayama/unity-cli-loop/commit/6233aa514a2fb63103ae5780a42602de30becd28))

## [3.0.0-beta.37](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.36...dispatcher-v3.0.0-beta.37) (2026-08-28)


### Bug Fixes

* package install now finds the Unity project from the repository root ([#2448](https://github.com/hatayama/unity-cli-loop/issues/2448)) ([84e38db](https://github.com/hatayama/unity-cli-loop/commit/84e38dbc726110fbc37167b5dabd41608b59df7e))

## [3.0.0-beta.36](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.35...dispatcher-v3.0.0-beta.36) (2026-08-28)


### Features

* add set-code-optimization command and recommend permanent Debug for pause-point ([#2442](https://github.com/hatayama/unity-cli-loop/issues/2442)) ([3c54031](https://github.com/hatayama/unity-cli-loop/commit/3c540310a4e5a6a51ecccc5e04a47c6951a3352a))

## [3.0.0-beta.35](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.34...dispatcher-v3.0.0-beta.35) (2026-08-27)


### Bug Fixes

* skip dispatcher update download when already at the target version ([#2438](https://github.com/hatayama/unity-cli-loop/issues/2438)) ([cec5d09](https://github.com/hatayama/unity-cli-loop/commit/cec5d09584a51bcddcc4211d877508cc40b8ba29))

## [3.0.0-beta.34](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.33...dispatcher-v3.0.0-beta.34) (2026-08-27)


### Features

* retire the record-input CLI tool in favor of the Recordings window ([#2430](https://github.com/hatayama/unity-cli-loop/issues/2430)) ([54afc07](https://github.com/hatayama/unity-cli-loop/commit/54afc07bff66933dfcd23607e60317ce4c1419a5))

## [3.0.0-beta.33](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.32...dispatcher-v3.0.0-beta.33) (2026-08-25)


### ⚠ BREAKING CHANGES

* align CLI option names across first-party tools ([#2422](https://github.com/hatayama/unity-cli-loop/issues/2422))

### Features

* accept --file and --line on pause-point-status and await-pause-point ([#2411](https://github.com/hatayama/unity-cli-loop/issues/2411)) ([f650e36](https://github.com/hatayama/unity-cli-loop/commit/f650e36a6bcbd2880dba6ee385c59181895e756f))
* add a compact command-name listing to uloop list ([13f5d06](https://github.com/hatayama/unity-cli-loop/commit/13f5d06bba57bfb7f1afdb430dab4e2e7fd384d4))
* add a max-caller-frames option to enable-pause-point ([779c32b](https://github.com/hatayama/unity-cli-loop/commit/779c32b5af581bdca603be069f245711e4d4760d))
* add uloop hot-reload for live C# patching without domain reload ([#2254](https://github.com/hatayama/unity-cli-loop/issues/2254)) ([20e97f6](https://github.com/hatayama/unity-cli-loop/commit/20e97f69c6fd6f64095934a4eebdc18115329e6a))
* align CLI option names across first-party tools ([#2422](https://github.com/hatayama/unity-cli-loop/issues/2422)) ([2e4daff](https://github.com/hatayama/unity-cli-loop/commit/2e4daff6536a7febe88841d9c6104b2fbf8923ff))
* compile pending script changes before running tests ([5ab6770](https://github.com/hatayama/unity-cli-loop/commit/5ab6770185af6444b9074424fd88bb667cf8f67b))
* default hot-reload --files to sources changed since the last compile ([1c75e85](https://github.com/hatayama/unity-cli-loop/commit/1c75e85ade883553f691c91d116ed9501bb91835))
* default screenshot capture mode to rendering during Play Mode ([#2379](https://github.com/hatayama/unity-cli-loop/issues/2379)) ([393c11f](https://github.com/hatayama/unity-cli-loop/commit/393c11fa00e8cce3bb6e8058b0a06da56ec34233))
* focus a freshly launched Unity while waiting for readiness ([#2344](https://github.com/hatayama/unity-cli-loop/issues/2344)) ([a931e0d](https://github.com/hatayama/unity-cli-loop/commit/a931e0d3f8f98dcee1a40525a8fec3633c2760fb))
* gate pause-point hits with the hit-when condition ([#2405](https://github.com/hatayama/unity-cli-loop/issues/2405)) ([f5f4e33](https://github.com/hatayama/unity-cli-loop/commit/f5f4e3391e3a131b92d12288fbfe633b4a3c4914))
* include the running tool's elapsed time in single-flight busy rejections ([c5709bd](https://github.com/hatayama/unity-cli-loop/commit/c5709bd769e384249e2d0d4ea1235ad110e30b22))
* install skills into any directory with the new --output-dir option ([#2332](https://github.com/hatayama/unity-cli-loop/issues/2332)) ([f3c8051](https://github.com/hatayama/unity-cli-loop/commit/f3c8051de70e90c57ab03305649c44ca28c56d37))
* list all markers when pause-point-status omits --id ([#2413](https://github.com/hatayama/unity-cli-loop/issues/2413)) ([aee4288](https://github.com/hatayama/unity-cli-loop/commit/aee4288a2415efa148e335572442307402cb19e6))
* Remove the leftover uloop completion command ([50aa929](https://github.com/hatayama/unity-cli-loop/commit/50aa92925f963450e0becf6c113366b12ab2a42c))
* report interim compile-wait diagnostics so a stalled compile is visible before the timeout ([6d9c416](https://github.com/hatayama/unity-cli-loop/commit/6d9c4164f0797123084011054715fbc031a54dbb))


### Bug Fixes

* --trigger now rejects a leading uloop and shows the command to retry ([#2388](https://github.com/hatayama/unity-cli-loop/issues/2388)) ([9cefd73](https://github.com/hatayama/unity-cli-loop/commit/9cefd733403d6d5d74cde0cdbac511c1e80561bc))
* align clear-pause-point messages with ClearedCount for auto-disarmed and expired markers ([319159b](https://github.com/hatayama/unity-cli-loop/commit/319159bb6cffef137eef8c91eb97435a84b1fff2))
* describe CLI-only options in await-pause-point and pause-point-status help ([#2299](https://github.com/hatayama/unity-cli-loop/issues/2299)) ([7b237bd](https://github.com/hatayama/unity-cli-loop/commit/7b237bd2130c734733abbf3f920f6250a3885663))
* force TLS 1.2 and basic parsing for PowerShell installer downloads ([ea9e052](https://github.com/hatayama/unity-cli-loop/commit/ea9e0529a8b8565eac344a72582a1770c2c59d4e))
* Git Bash can install uloop from a zip without unzip ([cbcdc62](https://github.com/hatayama/unity-cli-loop/commit/cbcdc62b7fc1c1134522fb7f31cbb8c0c8863b9d))
* Windows uloop update can replace a running uloop.exe ([6857430](https://github.com/hatayama/unity-cli-loop/commit/6857430d1c33ab7e46c778eae318afdf2de53576))

## [3.0.0-beta.32](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.31...dispatcher-v3.0.0-beta.32) (2026-08-12)


### Features

* distribute uloop via a Homebrew tap ([#2157](https://github.com/hatayama/unity-cli-loop/issues/2157)) ([f882ea0](https://github.com/hatayama/unity-cli-loop/commit/f882ea0a9bc79cad97ae48dd4ea0ddc6e1a98dce))

## [3.0.0-beta.31] (2026-08-11)


## [3.0.0-beta.30](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.29...dispatcher-v3.0.0-beta.30) (2026-07-30)


### Bug Fixes

* terminal install works with curl alone by trusting the repository pin ([#2081](https://github.com/hatayama/unity-cli-loop/issues/2081)) ([58ec443](https://github.com/hatayama/unity-cli-loop/commit/58ec44303d69e1aa3e620f3c27b293218f4b5cc3))

## [3.0.0-beta.29](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.28...dispatcher-v3.0.0-beta.29) (2026-07-29)


### Features

* install the Unity package from the terminal with uloop package install ([#2078](https://github.com/hatayama/unity-cli-loop/issues/2078)) ([5e62a86](https://github.com/hatayama/unity-cli-loop/commit/5e62a86d880d05f15eb246ed5adbfa7ad5db74f7))

## [3.0.0-beta.28](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.27...dispatcher-v3.0.0-beta.28) (2026-07-29)


### Features

* check 3D physics hits with simulate-mouse-input --dry-run ([#2072](https://github.com/hatayama/unity-cli-loop/issues/2072)) ([24225f6](https://github.com/hatayama/unity-cli-loop/commit/24225f65af305fdb486ac80b36d3c0d01bb30d01))

## [3.0.0-beta.27](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.26...dispatcher-v3.0.0-beta.27) (2026-07-29)


### Features

* address pause point round 13-14 leftover improvements ([#2065](https://github.com/hatayama/unity-cli-loop/issues/2065)) ([ce21b4f](https://github.com/hatayama/unity-cli-loop/commit/ce21b4f5d1d3693cb9837288da15f4b43102fabe))

## [3.0.0-beta.26](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.25...dispatcher-v3.0.0-beta.26) (2026-07-29)


### Features

* address pause point round 13-14 verification feedback ([#2059](https://github.com/hatayama/unity-cli-loop/issues/2059)) ([62db173](https://github.com/hatayama/unity-cli-loop/commit/62db17327b467ab44250d1ca59f8b136129251ae))

## [3.0.0-beta.25](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.24...dispatcher-v3.0.0-beta.25) (2026-07-28)


### Features

* configurable compile wait timeout and working timeout recovery ([#2036](https://github.com/hatayama/unity-cli-loop/issues/2036)) ([fc867c3](https://github.com/hatayama/unity-cli-loop/commit/fc867c3914f979dbc8f31f26be4c5753f66e1c36))

## [3.0.0-beta.24](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.23...dispatcher-v3.0.0-beta.24) (2026-07-27)


### Bug Fixes

* V2 delegation notice now names the V2 CLI version that ran ([#2027](https://github.com/hatayama/unity-cli-loop/issues/2027)) ([5ed982d](https://github.com/hatayama/unity-cli-loop/commit/5ed982d6d523714355f33b7fe8856f773079afeb))

## [3.0.0-beta.23](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.22...dispatcher-v3.0.0-beta.23) (2026-07-27)


### Features

* Improve uloop CLI discoverability after E2E verification rounds ([#2023](https://github.com/hatayama/unity-cli-loop/issues/2023)) ([fcad6e7](https://github.com/hatayama/unity-cli-loop/commit/fcad6e7e7ff279ea160828caa938f9105d4a6c30))


### Bug Fixes

* Rename the global uloop launcher to dispatcher in user-facing text ([#1994](https://github.com/hatayama/unity-cli-loop/issues/1994)) ([3fdd062](https://github.com/hatayama/unity-cli-loop/commit/3fdd062d5821b746f2db142d7430a29a5a8ba9a0))
* Wait for V2 server readiness in uloop launch ([#2024](https://github.com/hatayama/unity-cli-loop/issues/2024)) ([0f076fc](https://github.com/hatayama/unity-cli-loop/commit/0f076fc20c9ede308cd6a9c3fb22302c58f85ef2))

## [3.0.0-beta.22](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.21...dispatcher-v3.0.0-beta.22) (2026-07-24)


### Bug Fixes

* Improve skills setup panel empty state and refresh scoping ([#1983](https://github.com/hatayama/unity-cli-loop/issues/1983)) ([caeb079](https://github.com/hatayama/unity-cli-loop/commit/caeb0799d78331162e1cae5c0572c8fc2ed6aa81))
* Tighten v3 migration skill guidance and align wizard prompt ([#1981](https://github.com/hatayama/unity-cli-loop/issues/1981)) ([7c8a4a2](https://github.com/hatayama/unity-cli-loop/commit/7c8a4a20a7cda853a9ddfdf966fa24c4ccfc5609))

## [3.0.0-beta.21](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.20...dispatcher-v3.0.0-beta.21) (2026-07-24)


### Features

* pause-point round 10 feedback improvements ([8b66c6a](https://github.com/hatayama/unity-cli-loop/commit/8b66c6a7e7066e82820b144b85a9b2d89ff071ed))


### Bug Fixes

* Windows CLI install robustness and uninstall message wording ([#1970](https://github.com/hatayama/unity-cli-loop/issues/1970)) ([40a12ad](https://github.com/hatayama/unity-cli-loop/commit/40a12add4b445293ee7a4820ae26c860068a2dac))

## [3.0.0-beta.20](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.0-beta.19...dispatcher-v3.0.0-beta.20) (2026-07-22)


### Features

* Round-8 pause-point usability improvements ([#1944](https://github.com/hatayama/unity-cli-loop/issues/1944)) ([1940cca](https://github.com/hatayama/unity-cli-loop/commit/1940cca1dea33377300cb06a13cb65ba9be85db9))

## [3.0.0-beta.19](https://github.com/hatayama/unity-cli-loop/releases/tag/dispatcher-v3.0.0-beta.19) (2026-07-21)

Version realignment: the dispatcher rejoins the 3.0.0-beta line. The 3.1.0-beta series existed only because an unintended dispatcher-v3.0.0 bootstrap release made release-please treat 3.0.0 as shipped. No functional changes.

## [3.1.0-beta.19](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.18...dispatcher-v3.1.0-beta.19) (2026-07-20)


### Features

* dispatcher env override to use a locally built project runner ([#1863](https://github.com/hatayama/unity-cli-loop/issues/1863)) ([e6ec679](https://github.com/hatayama/unity-cli-loop/commit/e6ec67921fde80b5a8bdc5c3915618489d7f4615))


### Bug Fixes

* Launch no longer overclaims a crash for a stale UnityLockfile ([#1867](https://github.com/hatayama/unity-cli-loop/issues/1867)) ([7622db7](https://github.com/hatayama/unity-cli-loop/commit/7622db77b952243c1ca387ffca06af0fcf25217d))

## [3.1.0-beta.18](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.17...dispatcher-v3.1.0-beta.18) (2026-07-20)


### Bug Fixes

* Runner-owned command flags and help no longer require a dispatcher release ([#1862](https://github.com/hatayama/unity-cli-loop/issues/1862)) ([96e75f8](https://github.com/hatayama/unity-cli-loop/commit/96e75f8a19c23e4c3d587e91efa3e52574716060))

## [3.1.0-beta.17](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.16...dispatcher-v3.1.0-beta.17) (2026-07-19)


### Bug Fixes

* pause-pointの応答から重複情報を削減し、変数値だけを後から選んで取得できるように改善 ([#1857](https://github.com/hatayama/unity-cli-loop/issues/1857)) ([d507274](https://github.com/hatayama/unity-cli-loop/commit/d507274f14f61236aee30c3886139aa48c1a46d1))

## [3.1.0-beta.16](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.15...dispatcher-v3.1.0-beta.16) (2026-07-19)


### Bug Fixes

* Detect V2 Unity projects with file:/embedded package references or ambiguous git cache generations ([#1847](https://github.com/hatayama/unity-cli-loop/issues/1847)) ([85509a8](https://github.com/hatayama/unity-cli-loop/commit/85509a8d5b50cf5354e0506332f5f335d012cfa4))

## [3.1.0-beta.15](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.14...dispatcher-v3.1.0-beta.15) (2026-07-18)


### Bug Fixes

* align tool exit codes with response success ([#1824](https://github.com/hatayama/unity-cli-loop/issues/1824)) ([1cc123b](https://github.com/hatayama/unity-cli-loop/commit/1cc123b8d450b137ef489d55af0d13f9f587c16f))
* Launch V2 Unity projects without hanging ([#1826](https://github.com/hatayama/unity-cli-loop/issues/1826)) ([4ace5b4](https://github.com/hatayama/unity-cli-loop/commit/4ace5b49f3cbc7fa92ff7c3871f5064091c4b5cc))

## [3.1.0-beta.14](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.1.0-beta.13...dispatcher-v3.1.0-beta.14) (2026-07-17)


### Features

* Delegate V2 projects through the V3 dispatcher ([#1807](https://github.com/hatayama/unity-cli-loop/issues/1807)) ([3882b19](https://github.com/hatayama/unity-cli-loop/commit/3882b1913184dcbce0f94f6e5b6cf806b7405eb1))


### Bug Fixes

* make Windows v3 workflows reliable ([#1818](https://github.com/hatayama/unity-cli-loop/issues/1818)) ([21eae0a](https://github.com/hatayama/unity-cli-loop/commit/21eae0a96af05355cbf57eb3ab98dd7388fc7b2a))
* Project commands now work from paths containing glob characters ([#1810](https://github.com/hatayama/unity-cli-loop/issues/1810)) ([c56843a](https://github.com/hatayama/unity-cli-loop/commit/c56843a11e7e794d27c378dbaddeac90bf87be75))
* V2 delegation now recognizes SCP Git dependencies with path queries ([#1811](https://github.com/hatayama/unity-cli-loop/issues/1811)) ([b42f918](https://github.com/hatayama/unity-cli-loop/commit/b42f91857c51556102a35514da89c305d3feb1bf))

## [3.1.0-beta.13](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.1-beta.13...dispatcher-v3.1.0-beta.13) (2026-07-15)


### Features

* add pause-point watch expressions ([#1733](https://github.com/hatayama/unity-cli-loop/issues/1733)) ([875835f](https://github.com/hatayama/unity-cli-loop/commit/875835fc71deb70ccdc0bea0d67582a0d42a2f80))
* Improve CLI guidance for tool enums, busy errors, and dynamic-code diagnostics ([7318b9d](https://github.com/hatayama/unity-cli-loop/commit/7318b9d1dcead7b52241e88adab7538b47a5f95a))
* Pause point wait command is now await-pause-point ([#1698](https://github.com/hatayama/unity-cli-loop/issues/1698)) ([f1d0a9d](https://github.com/hatayama/unity-cli-loop/commit/f1d0a9d1c6c72a8699a2468e68a05262a50642dc))


### Bug Fixes

* bound Go external OS commands and propagate Ctrl+C cancellation ([#1738](https://github.com/hatayama/unity-cli-loop/issues/1738)) ([ddb0581](https://github.com/hatayama/unity-cli-loop/commit/ddb058124507d0207d371bcf988ef6449b0c0b66))
* compile-consistency — external scene hold, compile wait/TTL align, API Update guidance ([#1760](https://github.com/hatayama/unity-cli-loop/issues/1760)) ([247cb0c](https://github.com/hatayama/unity-cli-loop/commit/247cb0c62a6a87fd56dba0126334fb5061d4d081))
* Harden CLI distribution and Unity IPC security ([#1794](https://github.com/hatayama/unity-cli-loop/issues/1794)) ([b5ca16b](https://github.com/hatayama/unity-cli-loop/commit/b5ca16b34fc8359466183c0cac30f2d77e862212))
* Harden IPC contracts, empty RPC errors, and Settings async UI ([#1778](https://github.com/hatayama/unity-cli-loop/issues/1778)) ([0dffc75](https://github.com/hatayama/unity-cli-loop/commit/0dffc753bec575a29252430a59540ad3e0812848))
* Harden V2-to-V3 third-party migration for safe apply and reliable scans ([#1710](https://github.com/hatayama/unity-cli-loop/issues/1710)) ([b4fbb0d](https://github.com/hatayama/unity-cli-loop/commit/b4fbb0db6b8837e4036637e11684bc08da427935))

## [3.0.1-beta.13](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.1-beta.12...dispatcher-v3.0.1-beta.13) (2026-07-11)


### Bug Fixes

* Avoid misclassifying copied project error text ([0e821cc](https://github.com/hatayama/unity-cli-loop/commit/0e821cc94b12a03a0131ca64f76da2385432b23e))
* Busy responses now use the standard error envelope ([0aa2e7e](https://github.com/hatayama/unity-cli-loop/commit/0aa2e7e3355cb5e808c460cd3707a5f2ec0e7525))
* Dispatcher requires the project runner pin instead of parsing CliConstants ([#1501](https://github.com/hatayama/unity-cli-loop/issues/1501)) ([2012eb8](https://github.com/hatayama/unity-cli-loop/commit/2012eb882cf5bef0de72a49d2cda08f12586daf3))
* **dispatcher:** Verify dispatcher archive attestation before installer runs ([#1673](https://github.com/hatayama/unity-cli-loop/issues/1673)) ([1a49534](https://github.com/hatayama/unity-cli-loop/commit/1a4953464d7fa335ed73c2b71ef1e88485769ff6))
* **dispatcher:** Verify installer attestation before running self-update ([#1671](https://github.com/hatayama/unity-cli-loop/issues/1671)) ([109bc9f](https://github.com/hatayama/unity-cli-loop/commit/109bc9f2202b9adea817276624a6f5be77133c34))
* **dispatcher:** Verify project runner archive attestation before extraction ([#1672](https://github.com/hatayama/unity-cli-loop/issues/1672)) ([fa3313d](https://github.com/hatayama/unity-cli-loop/commit/fa3313d64ba98455015afc12acd6b407b4c52681))
* **install:** Validate ULOOP_VERSION shape before use ([#1675](https://github.com/hatayama/unity-cli-loop/issues/1675)) ([c09b683](https://github.com/hatayama/unity-cli-loop/commit/c09b683bd10c25626a72d7cac14db235f744aa1c))
* Local skill packages no longer include stale cached skills ([#1615](https://github.com/hatayama/unity-cli-loop/issues/1615)) ([9388b91](https://github.com/hatayama/unity-cli-loop/commit/9388b91801ea4ed76aa5c1251861a0efb9210d6c))
* Native uninstall now removes stale shell PATH setup ([#1498](https://github.com/hatayama/unity-cli-loop/issues/1498)) ([3450a1e](https://github.com/hatayama/unity-cli-loop/commit/3450a1ebe22d5a65f0c75d5ef1cd2a761ec3e5d5))
* Prefer synced tool definitions for help and completion ([603d178](https://github.com/hatayama/unity-cli-loop/commit/603d1783b0f67116d2f14091a02016d5e343d9f6))
* Read minimum version requirements from the project runner pin ([#1506](https://github.com/hatayama/unity-cli-loop/issues/1506)) ([18bf780](https://github.com/hatayama/unity-cli-loop/commit/18bf780dc31f6105902c383186e97c4aec7c8772))
* Reject malformed dispatcher minimum versions consistently ([aa8a2fc](https://github.com/hatayama/unity-cli-loop/commit/aa8a2fc0dd2bff438dbc1c58ed7216333342aa81))
* Remove the dispatcher contract integer generation ([#1504](https://github.com/hatayama/unity-cli-loop/issues/1504)) ([d2ddbce](https://github.com/hatayama/unity-cli-loop/commit/d2ddbce87d9bd68b53a1e202e9ce43996b61acf6))
* Retry incomplete project runner downloads ([3c8cea0](https://github.com/hatayama/unity-cli-loop/commit/3c8cea0e80c514e7f72684d11b13dbb38ffc600b))
* Shared IPC clients assign request IDs safely ([b7d6acc](https://github.com/hatayama/unity-cli-loop/commit/b7d6acc161083a3bd62ce9e66ba0d4f00937f05f))
* Shrink the project runner pin schema to two fields ([#1503](https://github.com/hatayama/unity-cli-loop/issues/1503)) ([adceaed](https://github.com/hatayama/unity-cli-loop/commit/adceaedd78a8b7f608f5c2179e1ee799fdecb136))
* Verify dispatcher self-update installers ([0d132e2](https://github.com/hatayama/unity-cli-loop/commit/0d132e2b6cb10691cbd7ed9299b11bc4da8d03b1))

## [3.0.1-beta.12](https://github.com/hatayama/unity-cli-loop/compare/dispatcher-v3.0.1-beta.11...dispatcher-v3.0.1-beta.12) (2026-07-03)


### Bug Fixes

* Correct misleading dispatcher error and help messages ([#1464](https://github.com/hatayama/unity-cli-loop/issues/1464)) ([439835f](https://github.com/hatayama/unity-cli-loop/commit/439835f716efe48d7fed2376e6f4eff0f1bdeba7))

## [3.0.1-beta.11](https://github.com/hatayama/unity-cli-loop/releases/tag/dispatcher-v3.0.1-beta.11) (2026-07-03)

Initial changelog entry. Earlier dispatcher releases predate Release Please management; see the GitHub releases page for their notes.
