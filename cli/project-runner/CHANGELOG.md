# Changelog

## [3.1.1](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.1.0...uloop-project-runner-v3.1.1) (2026-09-02)


### Bug Fixes

* Skill instructions now match what the tools actually guarantee ([#2498](https://github.com/hatayama/unity-cli-loop/issues/2498)) ([e839eef](https://github.com/hatayama/unity-cli-loop/commit/e839eefe29900eb370d8522fb2979c9e0db36127))

## [3.1.0](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0...uloop-project-runner-v3.1.0) (2026-08-31)


### Features

* run-tests can now keep, fail on, or discard unsaved editor changes ([#2478](https://github.com/hatayama/unity-cli-loop/issues/2478)) ([758fb12](https://github.com/hatayama/unity-cli-loop/commit/758fb12bc0c9ea7f199f59bcbfa6002485d6b41b))

## [3.0.0](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.70...uloop-project-runner-v3.0.0) (2026-08-31)


### Miscellaneous Chores

* force release 3.0.0 ([6233aa5](https://github.com/hatayama/unity-cli-loop/commit/6233aa514a2fb63103ae5780a42602de30becd28))

## [3.0.0-beta.70](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.69...uloop-project-runner-v3.0.0-beta.70) (2026-08-28)


### Bug Fixes

* package install now finds the Unity project from the repository root ([#2448](https://github.com/hatayama/unity-cli-loop/issues/2448)) ([84e38db](https://github.com/hatayama/unity-cli-loop/commit/84e38dbc726110fbc37167b5dabd41608b59df7e))

## [3.0.0-beta.69](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.68...uloop-project-runner-v3.0.0-beta.69) (2026-08-28)


### Features

* add set-code-optimization command and recommend permanent Debug for pause-point ([#2442](https://github.com/hatayama/unity-cli-loop/issues/2442)) ([3c54031](https://github.com/hatayama/unity-cli-loop/commit/3c540310a4e5a6a51ecccc5e04a47c6951a3352a))

## [3.0.0-beta.68](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.67...uloop-project-runner-v3.0.0-beta.68) (2026-08-27)


### Features

* retire the record-input CLI tool in favor of the Recordings window ([#2430](https://github.com/hatayama/unity-cli-loop/issues/2430)) ([54afc07](https://github.com/hatayama/unity-cli-loop/commit/54afc07bff66933dfcd23607e60317ce4c1419a5))

## [3.0.0-beta.67](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.66...uloop-project-runner-v3.0.0-beta.67) (2026-08-25)


### ⚠ BREAKING CHANGES

* align CLI option names across first-party tools ([#2422](https://github.com/hatayama/unity-cli-loop/issues/2422))

### Features

* accept --file and --line on pause-point-status and await-pause-point ([#2411](https://github.com/hatayama/unity-cli-loop/issues/2411)) ([f650e36](https://github.com/hatayama/unity-cli-loop/commit/f650e36a6bcbd2880dba6ee385c59181895e756f))
* add a compact command-name listing to uloop list ([13f5d06](https://github.com/hatayama/unity-cli-loop/commit/13f5d06bba57bfb7f1afdb430dab4e2e7fd384d4))
* add a max-caller-frames option to enable-pause-point ([779c32b](https://github.com/hatayama/unity-cli-loop/commit/779c32b5af581bdca603be069f245711e4d4760d))
* add uloop hot-reload for live C# patching without domain reload ([#2254](https://github.com/hatayama/unity-cli-loop/issues/2254)) ([20e97f6](https://github.com/hatayama/unity-cli-loop/commit/20e97f69c6fd6f64095934a4eebdc18115329e6a))
* align CLI option names across first-party tools ([#2422](https://github.com/hatayama/unity-cli-loop/issues/2422)) ([2e4daff](https://github.com/hatayama/unity-cli-loop/commit/2e4daff6536a7febe88841d9c6104b2fbf8923ff))
* annotate pause-point collection previews with order and truncation ([#2293](https://github.com/hatayama/unity-cli-loop/issues/2293)) ([4b9b156](https://github.com/hatayama/unity-cli-loop/commit/4b9b156a74302006372eb0de720e95c4e3094f27))
* announce pause-point wait start on stderr before the blocking wait ([#2333](https://github.com/hatayama/unity-cli-loop/issues/2333)) ([e8eb46f](https://github.com/hatayama/unity-cli-loop/commit/e8eb46f361427df4621bff67da6cc234b6a519dd))
* compile pending script changes before running tests ([5ab6770](https://github.com/hatayama/unity-cli-loop/commit/5ab6770185af6444b9074424fd88bb667cf8f67b))
* default hot-reload --files to sources changed since the last compile ([1c75e85](https://github.com/hatayama/unity-cli-loop/commit/1c75e85ade883553f691c91d116ed9501bb91835))
* default screenshot capture mode to rendering during Play Mode ([#2379](https://github.com/hatayama/unity-cli-loop/issues/2379)) ([393c11f](https://github.com/hatayama/unity-cli-loop/commit/393c11fa00e8cce3bb6e8058b0a06da56ec34233))
* echo enable-time resolved marker fields on expired pause-point responses ([#2311](https://github.com/hatayama/unity-cli-loop/issues/2311)) ([7332b03](https://github.com/hatayama/unity-cli-loop/commit/7332b03009b0c4cb6ecbc8fd8330c49bc401ab85))
* enable-pause-point switches Release editors to Debug automatically ([b2ba35e](https://github.com/hatayama/unity-cli-loop/commit/b2ba35ee7018ca55e1d36d9bd00d77c768639f60))
* explain frame-boundary pausing on hit responses and surface the expired next action ([#2285](https://github.com/hatayama/unity-cli-loop/issues/2285)) ([812d4fe](https://github.com/hatayama/unity-cli-loop/commit/812d4fed5daaf84c67ea892eddb34f38fa887121))
* explain hit-when skips in pause-point CLI notes and wait diagnostics ([#2406](https://github.com/hatayama/unity-cli-loop/issues/2406)) ([47a231f](https://github.com/hatayama/unity-cli-loop/commit/47a231f29252ececf543bfd7a11d933916a34150))
* explain unreported trigger results on pause-point wait settlement ([#2334](https://github.com/hatayama/unity-cli-loop/issues/2334)) ([ba9590f](https://github.com/hatayama/unity-cli-loop/commit/ba9590fce0e2fe6c571ddab56ef2ef3fd2f27763))
* focus Unity when a compile stalls before compilation starts ([#2341](https://github.com/hatayama/unity-cli-loop/issues/2341)) ([08c8d2a](https://github.com/hatayama/unity-cli-loop/commit/08c8d2a060a80d8d98dc5271c75c306a5edfd172))
* gate pause-point hits with the hit-when condition ([#2405](https://github.com/hatayama/unity-cli-loop/issues/2405)) ([f5f4e33](https://github.com/hatayama/unity-cli-loop/commit/f5f4e3391e3a131b92d12288fbfe633b4a3c4914))
* include caller stack frames in pause-point hit captures ([6c31a83](https://github.com/hatayama/unity-cli-loop/commit/6c31a8300ca7845e7d5a90bde42cc675df3154a5))
* include the running tool's elapsed time in single-flight busy rejections ([c5709bd](https://github.com/hatayama/unity-cli-loop/commit/c5709bd769e384249e2d0d4ea1235ad110e30b22))
* label drift candidates with their compiled methods and rebalance the expired hint ([#2415](https://github.com/hatayama/unity-cli-loop/issues/2415)) ([d17ed2d](https://github.com/hatayama/unity-cli-loop/commit/d17ed2d16d673ce187985ce9c976adad36bcf9ab))
* list all markers when pause-point-status omits --id ([#2413](https://github.com/hatayama/unity-cli-loop/issues/2413)) ([aee4288](https://github.com/hatayama/unity-cli-loop/commit/aee4288a2415efa148e335572442307402cb19e6))
* name the Script Updating Consent dialog in the compile stall warning ([#2328](https://github.com/hatayama/unity-cli-loop/issues/2328)) ([67d0b9a](https://github.com/hatayama/unity-cli-loop/commit/67d0b9ac157a6fb9f2b2730480e48579c8954b58))
* note symbol-less caller frames in pause-point responses ([#2298](https://github.com/hatayama/unity-cli-loop/issues/2298)) ([e5b4b8e](https://github.com/hatayama/unity-cli-loop/commit/e5b4b8e4719798fec9f3b057173d3dc733028f17))
* Remove the leftover uloop completion command ([50aa929](https://github.com/hatayama/unity-cli-loop/commit/50aa92925f963450e0becf6c113366b12ab2a42c))
* report interim compile-wait diagnostics so a stalled compile is visible before the timeout ([6d9c416](https://github.com/hatayama/unity-cli-loop/commit/6d9c4164f0797123084011054715fbc031a54dbb))
* report method entry counts for expired pause points ([#2402](https://github.com/hatayama/unity-cli-loop/issues/2402)) ([d1e3c21](https://github.com/hatayama/unity-cli-loop/commit/d1e3c21fce43d989fdb99f7fd58c5e713abeefcd))
* report the line resolution basis in enable-pause-point responses ([#2409](https://github.com/hatayama/unity-cli-loop/issues/2409)) ([ea30b1e](https://github.com/hatayama/unity-cli-loop/commit/ea30b1e2bbfcaf03163e6f5a83716c9786d7429a))
* state the preview cap and history cost when a captured value is clipped ([#2414](https://github.com/hatayama/unity-cli-loop/issues/2414)) ([984a87f](https://github.com/hatayama/unity-cli-loop/commit/984a87fa46cfb637116a03cc08e800a5f44397c4))
* suggest option values and near-miss option names on argument parsing errors ([#2309](https://github.com/hatayama/unity-cli-loop/issues/2309)) ([62b8aa6](https://github.com/hatayama/unity-cli-loop/commit/62b8aa667a458514a177fbca50c45c4b8311765f))


### Bug Fixes

* --trigger now rejects a leading uloop and shows the command to retry ([#2388](https://github.com/hatayama/unity-cli-loop/issues/2388)) ([9cefd73](https://github.com/hatayama/unity-cli-loop/commit/9cefd733403d6d5d74cde0cdbac511c1e80561bc))
* align clear-pause-point messages with ClearedCount for auto-disarmed and expired markers ([319159b](https://github.com/hatayama/unity-cli-loop/commit/319159bb6cffef137eef8c91eb97435a84b1fff2))
* align pause-point truncation aggregates with per-variable clipping ([#2376](https://github.com/hatayama/unity-cli-loop/issues/2376)) ([ea066fb](https://github.com/hatayama/unity-cli-loop/commit/ea066fb71e6a88fef1b50c81f71bae12d4d0461d))
* describe CLI-only options in await-pause-point and pause-point-status help ([#2299](https://github.com/hatayama/unity-cli-loop/issues/2299)) ([7b237bd](https://github.com/hatayama/unity-cli-loop/commit/7b237bd2130c734733abbf3f920f6250a3885663))
* explain pause-point truncation flags filtered by captured-variable names ([#2290](https://github.com/hatayama/unity-cli-loop/issues/2290)) ([f15ac70](https://github.com/hatayama/unity-cli-loop/commit/f15ac70251f9337973be5c2d25991e400e0865e7))
* harden pause-point caller-frame path and array normalization ([3e86a46](https://github.com/hatayama/unity-cli-loop/commit/3e86a46437353a66c0324d966f2e5140fed1f36f))
* omit null simulate-keyboard fields and surface pause-point reading notes before captured variables ([#2347](https://github.com/hatayama/unity-cli-loop/issues/2347)) ([d3cfee4](https://github.com/hatayama/unity-cli-loop/commit/d3cfee45961b8128cf3ac2281f66f3005a7b7aba))
* pause-point timeout now explains a rejected trigger instead of blaming a missed line ([#2395](https://github.com/hatayama/unity-cli-loop/issues/2395)) ([8a5f1c9](https://github.com/hatayama/unity-cli-loop/commit/8a5f1c91be6b2b7f3acd43bbfbb2009d9370078e))
* suggest unknown-option corrections from any kebab token and CLI-only flags ([f1e2ec2](https://github.com/hatayama/unity-cli-loop/commit/f1e2ec205f14dc8e2621390963916a506180aa65))
* teach missing-id and unexpected-argument NextActions about file:line ids and comma lists ([#2346](https://github.com/hatayama/unity-cli-loop/issues/2346)) ([1822d64](https://github.com/hatayama/unity-cli-loop/commit/1822d64ba12575222e21725b3250998b6e738d29))

## [3.0.0-beta.66] (2026-08-11)


## [3.0.0-beta.65](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.64...uloop-project-runner-v3.0.0-beta.65) (2026-07-29)


### Features

* install the Unity package from the terminal with uloop package install ([#2078](https://github.com/hatayama/unity-cli-loop/issues/2078)) ([5e62a86](https://github.com/hatayama/unity-cli-loop/commit/5e62a86d880d05f15eb246ed5adbfa7ad5db74f7))

## [3.0.0-beta.64](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.63...uloop-project-runner-v3.0.0-beta.64) (2026-07-29)


### Features

* check 3D physics hits with simulate-mouse-input --dry-run ([#2072](https://github.com/hatayama/unity-cli-loop/issues/2072)) ([24225f6](https://github.com/hatayama/unity-cli-loop/commit/24225f65af305fdb486ac80b36d3c0d01bb30d01))

## [3.0.0-beta.63](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.62...uloop-project-runner-v3.0.0-beta.63) (2026-07-29)


### Features

* address pause point round 13-14 leftover improvements ([#2065](https://github.com/hatayama/unity-cli-loop/issues/2065)) ([ce21b4f](https://github.com/hatayama/unity-cli-loop/commit/ce21b4f5d1d3693cb9837288da15f4b43102fabe))

## [3.0.0-beta.62](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.61...uloop-project-runner-v3.0.0-beta.62) (2026-07-29)


### Features

* address pause point round 13-14 verification feedback ([#2059](https://github.com/hatayama/unity-cli-loop/issues/2059)) ([62db173](https://github.com/hatayama/unity-cli-loop/commit/62db17327b467ab44250d1ca59f8b136129251ae))

## [3.0.0-beta.61](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.60...uloop-project-runner-v3.0.0-beta.61) (2026-07-28)


### Features

* configurable compile wait timeout and working timeout recovery ([#2036](https://github.com/hatayama/unity-cli-loop/issues/2036)) ([fc867c3](https://github.com/hatayama/unity-cli-loop/commit/fc867c3914f979dbc8f31f26be4c5753f66e1c36))

## [3.0.0-beta.60](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.59...uloop-project-runner-v3.0.0-beta.60) (2026-07-27)


### Bug Fixes

* Background Unity editors no longer pop over other windows and keep serving commands ([#2029](https://github.com/hatayama/unity-cli-loop/issues/2029)) ([55d7c37](https://github.com/hatayama/unity-cli-loop/commit/55d7c3732b66f6d55a15163cea1916719a1a1459))

## [3.0.0-beta.59](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.58...uloop-project-runner-v3.0.0-beta.59) (2026-07-27)


### Features

* Improve uloop CLI discoverability after E2E verification rounds ([#2023](https://github.com/hatayama/unity-cli-loop/issues/2023)) ([fcad6e7](https://github.com/hatayama/unity-cli-loop/commit/fcad6e7e7ff279ea160828caa938f9105d4a6c30))


### Bug Fixes

* Apply pause-point round 11 verification feedback ([#1985](https://github.com/hatayama/unity-cli-loop/issues/1985)) ([43c4c67](https://github.com/hatayama/unity-cli-loop/commit/43c4c67e06bc3da93726ed728c9d3a0c4d21e167))
* Rename the global uloop launcher to dispatcher in user-facing text ([#1994](https://github.com/hatayama/unity-cli-loop/issues/1994)) ([3fdd062](https://github.com/hatayama/unity-cli-loop/commit/3fdd062d5821b746f2db142d7430a29a5a8ba9a0))

## [3.0.0-beta.58](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.57...uloop-project-runner-v3.0.0-beta.58) (2026-07-24)


### Features

* pause-point round 10 feedback improvements ([8b66c6a](https://github.com/hatayama/unity-cli-loop/commit/8b66c6a7e7066e82820b144b85a9b2d89ff071ed))
* pause-point round 9 feedback improvements ([#1967](https://github.com/hatayama/unity-cli-loop/issues/1967)) ([1ea3696](https://github.com/hatayama/unity-cli-loop/commit/1ea369605c05836485ba95c690029af74914dec7))

## [3.0.0-beta.57](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.56...uloop-project-runner-v3.0.0-beta.57) (2026-07-22)


### Features

* Round-8 pause-point usability improvements ([#1944](https://github.com/hatayama/unity-cli-loop/issues/1944)) ([1940cca](https://github.com/hatayama/unity-cli-loop/commit/1940cca1dea33377300cb06a13cb65ba9be85db9))

## [3.0.0-beta.56](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.55...uloop-project-runner-v3.0.0-beta.56) (2026-07-21)


### Features

* Round-7 pause-point usability improvements ([#1926](https://github.com/hatayama/unity-cli-loop/issues/1926)) ([178f0b5](https://github.com/hatayama/unity-cli-loop/commit/178f0b5e7129ef9f7a7155752c97f564b74b9b6a))

## [3.0.0-beta.55](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.54...uloop-project-runner-v3.0.0-beta.55) (2026-07-21)


### Features

* Round-5 pause-point diagnostics and observability improvements ([#1908](https://github.com/hatayama/unity-cli-loop/issues/1908)) ([958d85d](https://github.com/hatayama/unity-cli-loop/commit/958d85d9af0aba3ddbb57cf191574eeffe07449c))
* Round-6 pause-point usability improvements ([#1914](https://github.com/hatayama/unity-cli-loop/issues/1914)) ([f8f5018](https://github.com/hatayama/unity-cli-loop/commit/f8f501872a1b47699717eb150168c254cae6fec4))

## [3.0.0-beta.54](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.53...uloop-project-runner-v3.0.0-beta.54) (2026-07-20)


### Bug Fixes

* Round-3 pause-point/dynamic-code usability and reliability fixes ([#1884](https://github.com/hatayama/unity-cli-loop/issues/1884)) ([f1de07e](https://github.com/hatayama/unity-cli-loop/commit/f1de07e8030eca5e70de418cf22e4a597d5a0e06))

## [3.0.0-beta.53](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.52...uloop-project-runner-v3.0.0-beta.53) (2026-07-20)


### Bug Fixes

* Pause points no longer expire before await-pause-point can observe them ([#1873](https://github.com/hatayama/unity-cli-loop/issues/1873)) ([00a166b](https://github.com/hatayama/unity-cli-loop/commit/00a166b2f267317813875b710dd1d75f2e6d5602))

## [3.0.0-beta.52](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.51...uloop-project-runner-v3.0.0-beta.52) (2026-07-20)


### Bug Fixes

* Runner-owned command flags and help no longer require a dispatcher release ([#1862](https://github.com/hatayama/unity-cli-loop/issues/1862)) ([96e75f8](https://github.com/hatayama/unity-cli-loop/commit/96e75f8a19c23e4c3d587e91efa3e52574716060))

## [3.0.0-beta.51](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.50...uloop-project-runner-v3.0.0-beta.51) (2026-07-19)


### Bug Fixes

* Pause points and watch expressions no longer leave silent gaps in debugging feedback ([#1854](https://github.com/hatayama/unity-cli-loop/issues/1854)) ([77658f8](https://github.com/hatayama/unity-cli-loop/commit/77658f89efb4a302058d4f546f39839a092e223e))
* pause-pointの応答から重複情報を削減し、変数値だけを後から選んで取得できるように改善 ([#1857](https://github.com/hatayama/unity-cli-loop/issues/1857)) ([d507274](https://github.com/hatayama/unity-cli-loop/commit/d507274f14f61236aee30c3886139aa48c1a46d1))

## [3.0.0-beta.50](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.49...uloop-project-runner-v3.0.0-beta.50) (2026-07-18)


### Bug Fixes

* align tool exit codes with response success ([#1824](https://github.com/hatayama/unity-cli-loop/issues/1824)) ([1cc123b](https://github.com/hatayama/unity-cli-loop/commit/1cc123b8d450b137ef489d55af0d13f9f587c16f))

## [3.0.0-beta.49](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.48...uloop-project-runner-v3.0.0-beta.49) (2026-07-17)


### Features

* Delegate V2 projects through the V3 dispatcher ([#1807](https://github.com/hatayama/unity-cli-loop/issues/1807)) ([3882b19](https://github.com/hatayama/unity-cli-loop/commit/3882b1913184dcbce0f94f6e5b6cf806b7405eb1))


### Bug Fixes

* make Windows v3 workflows reliable ([#1818](https://github.com/hatayama/unity-cli-loop/issues/1818)) ([21eae0a](https://github.com/hatayama/unity-cli-loop/commit/21eae0a96af05355cbf57eb3ab98dd7388fc7b2a))

## [3.0.0-beta.48](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.47...uloop-project-runner-v3.0.0-beta.48) (2026-07-15)


### Features

* add pause point capture modes and history ([#1729](https://github.com/hatayama/unity-cli-loop/issues/1729)) ([0b5e72e](https://github.com/hatayama/unity-cli-loop/commit/0b5e72e788a6f032001ee3670c117d8034089234))
* add pause-point watch expressions ([#1733](https://github.com/hatayama/unity-cli-loop/issues/1733)) ([875835f](https://github.com/hatayama/unity-cli-loop/commit/875835fc71deb70ccdc0bea0d67582a0d42a2f80))
* Improve CLI guidance for tool enums, busy errors, and dynamic-code diagnostics ([7318b9d](https://github.com/hatayama/unity-cli-loop/commit/7318b9d1dcead7b52241e88adab7538b47a5f95a))
* Improve pause point observability with cleared reasons, collection previews, and raw capture ([03c656b](https://github.com/hatayama/unity-cli-loop/commit/03c656b8734de3d87a32dc7e7f90fea61031eeaf))
* Pause point wait command is now await-pause-point ([#1698](https://github.com/hatayama/unity-cli-loop/issues/1698)) ([f1d0a9d](https://github.com/hatayama/unity-cli-loop/commit/f1d0a9d1c6c72a8699a2468e68a05262a50642dc))


### Bug Fixes

* bound Go external OS commands and propagate Ctrl+C cancellation ([#1738](https://github.com/hatayama/unity-cli-loop/issues/1738)) ([ddb0581](https://github.com/hatayama/unity-cli-loop/commit/ddb058124507d0207d371bcf988ef6449b0c0b66))
* compile-consistency — external scene hold, compile wait/TTL align, API Update guidance ([#1760](https://github.com/hatayama/unity-cli-loop/issues/1760)) ([247cb0c](https://github.com/hatayama/unity-cli-loop/commit/247cb0c62a6a87fd56dba0126334fb5061d4d081))
* Harden CLI distribution and Unity IPC security ([#1794](https://github.com/hatayama/unity-cli-loop/issues/1794)) ([b5ca16b](https://github.com/hatayama/unity-cli-loop/commit/b5ca16b34fc8359466183c0cac30f2d77e862212))
* Harden IPC contracts, empty RPC errors, and Settings async UI ([#1778](https://github.com/hatayama/unity-cli-loop/issues/1778)) ([0dffc75](https://github.com/hatayama/unity-cli-loop/commit/0dffc753bec575a29252430a59540ad3e0812848))
* Tool Settings pause-point toggle now gates every pause point command ([#1700](https://github.com/hatayama/unity-cli-loop/issues/1700)) ([8785326](https://github.com/hatayama/unity-cli-loop/commit/8785326661d899751b8ba4ba4d20aebac2b78cc8))

## [3.0.0-beta.47](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.46...uloop-project-runner-v3.0.0-beta.47) (2026-07-11)


### Features

* Enable pause points by source file and line ([#1684](https://github.com/hatayama/unity-cli-loop/issues/1684)) ([272edb3](https://github.com/hatayama/unity-cli-loop/commit/272edb3d665bc54bfe15e84a1d71b36d9cd20c7c))


### Bug Fixes

* Avoid misclassifying copied project error text ([0e821cc](https://github.com/hatayama/unity-cli-loop/commit/0e821cc94b12a03a0131ca64f76da2385432b23e))
* Busy responses now use the standard error envelope ([0aa2e7e](https://github.com/hatayama/unity-cli-loop/commit/0aa2e7e3355cb5e808c460cd3707a5f2ec0e7525))
* Local skill packages no longer include stale cached skills ([#1615](https://github.com/hatayama/unity-cli-loop/issues/1615)) ([9388b91](https://github.com/hatayama/unity-cli-loop/commit/9388b91801ea4ed76aa5c1251861a0efb9210d6c))
* Prefer synced tool definitions for help and completion ([603d178](https://github.com/hatayama/unity-cli-loop/commit/603d1783b0f67116d2f14091a02016d5e343d9f6))
* Preserve get-logs response metadata in pause point evidence ([cec6fae](https://github.com/hatayama/unity-cli-loop/commit/cec6fae309215b4783ee668386beaa19cdaa3db4))
* Remove the dispatcher contract integer generation ([#1504](https://github.com/hatayama/unity-cli-loop/issues/1504)) ([d2ddbce](https://github.com/hatayama/unity-cli-loop/commit/d2ddbce87d9bd68b53a1e202e9ce43996b61acf6))
* Retry incomplete project runner downloads ([3c8cea0](https://github.com/hatayama/unity-cli-loop/commit/3c8cea0e80c514e7f72684d11b13dbb38ffc600b))
* Shared IPC clients assign request IDs safely ([b7d6acc](https://github.com/hatayama/unity-cli-loop/commit/b7d6acc161083a3bd62ce9e66ba0d4f00937f05f))

## [3.0.0-beta.46](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.45...uloop-project-runner-v3.0.0-beta.46) (2026-07-02)


### Bug Fixes

* forward project-scoped --version to the pinned project runner ([#1460](https://github.com/hatayama/unity-cli-loop/issues/1460)) ([7394c6b](https://github.com/hatayama/unity-cli-loop/commit/7394c6bb34eb429b31817b505f49b8e3de509547))
* Setup no longer reports optional npm cleanup failures ([#1452](https://github.com/hatayama/unity-cli-loop/issues/1452)) ([4aa729d](https://github.com/hatayama/unity-cli-loop/commit/4aa729dd29e98b608b906f542a46ed5e333533f6))

## [3.0.0-beta.45](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.44...uloop-project-runner-v3.0.0-beta.45) (2026-07-01)


### Features

* Unity launch now opens projects with compiler errors by default ([#1449](https://github.com/hatayama/unity-cli-loop/issues/1449)) ([990b046](https://github.com/hatayama/unity-cli-loop/commit/990b04636b3e836a9ed73b8c4746c2e395cb5f47))

## [3.0.0-beta.44](https://github.com/hatayama/unity-cli-loop/compare/uloop-project-runner-v3.0.0-beta.43...uloop-project-runner-v3.0.0-beta.44) (2026-06-30)


### Bug Fixes

* Windows update checks no longer interrupt routine commands ([#1434](https://github.com/hatayama/unity-cli-loop/issues/1434)) ([02ce6cc](https://github.com/hatayama/unity-cli-loop/commit/02ce6cc3dbeb92360ebcd5c5318eae6b9ca87c7c))

## [3.0.0-beta.43](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.42...cli-v3.0.0-beta.43) (2026-06-28)


### Bug Fixes

* Dispatcher updates now show the installed version ([#1422](https://github.com/hatayama/unity-cli-loop/issues/1422)) ([954f4ad](https://github.com/hatayama/unity-cli-loop/commit/954f4adf7c861b80db0a5d9bfdc512bfe37b3393))

## [3.0.0-beta.42](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.41...cli-v3.0.0-beta.42) (2026-06-27)


### Bug Fixes

* Dispatcher releases no longer look stable during v3 beta ([#1418](https://github.com/hatayama/unity-cli-loop/issues/1418)) ([bebb5cd](https://github.com/hatayama/unity-cli-loop/commit/bebb5cda7583a66969dcf4183316ec1d658014bd))
* First dispatcher commands now show CLI download status ([#1419](https://github.com/hatayama/unity-cli-loop/issues/1419)) ([722d799](https://github.com/hatayama/unity-cli-loop/commit/722d799ff12db5cdffea370939328c00a34aa461))

## [3.0.0-beta.41](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.40...cli-v3.0.0-beta.41) (2026-06-27)


### Features

* Let uloop launchers use project-pinned CLI versions ([#1413](https://github.com/hatayama/unity-cli-loop/issues/1413)) ([3e39bed](https://github.com/hatayama/unity-cli-loop/commit/3e39bed15f6b65ea54e68ca36ea2c4be898f4c7a))

## [3.0.0-beta.40](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.39...cli-v3.0.0-beta.40) (2026-06-26)


### Features

* Projects can run pinned CLI versions ([#1411](https://github.com/hatayama/unity-cli-loop/issues/1411)) ([1637a34](https://github.com/hatayama/unity-cli-loop/commit/1637a34ac47a31025d37db511ef5736baa745f57))


### Bug Fixes

* Keep release PRs compatible with the CLI launcher ([cce7e15](https://github.com/hatayama/unity-cli-loop/commit/cce7e15e0c3d35b320bd81df366247102d8f8b91))

## [3.0.0-beta.39](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.38...cli-v3.0.0-beta.39) (2026-06-25)


### Bug Fixes

* Keep Go and C# complexity checks under fifteen ([#1403](https://github.com/hatayama/unity-cli-loop/issues/1403)) ([e77d893](https://github.com/hatayama/unity-cli-loop/commit/e77d8938a1ebea3439236c91753677cb0074aa27))

## [3.0.0-beta.38](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.37...cli-v3.0.0-beta.38) (2026-06-22)


### Bug Fixes

* Keep V3 CLI JSON output compatible with V2 ([#1391](https://github.com/hatayama/unity-cli-loop/issues/1391)) ([d07ffa2](https://github.com/hatayama/unity-cli-loop/commit/d07ffa204fbc864eb5b0c0db891baf8e277889ae))
* Make V3 migration safer and easier to run ([#1386](https://github.com/hatayama/unity-cli-loop/issues/1386)) ([03087ce](https://github.com/hatayama/unity-cli-loop/commit/03087ce3b9df4255e5a867195a939ac490d687ae))

## [3.0.0-beta.37](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.36...cli-v3.0.0-beta.37) (2026-06-21)


### Features

* V3 CLI invocation migration skills are available from the wizard ([#1382](https://github.com/hatayama/unity-cli-loop/issues/1382)) ([a327f4a](https://github.com/hatayama/unity-cli-loop/commit/a327f4ad2179b5cd44b1aa5d2747494712edf657))

## [3.0.0-beta.36](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.35...cli-v3.0.0-beta.36) (2026-06-17)


### Bug Fixes

* CLI JSON output now uses consistent field names ([#1360](https://github.com/hatayama/unity-cli-loop/issues/1360)) ([e20c8b3](https://github.com/hatayama/unity-cli-loop/commit/e20c8b330e9f3651554ed2a0184f7d1a49d585eb))
* PlayMode start reports compiler errors instead of timing out ([#1354](https://github.com/hatayama/unity-cli-loop/issues/1354)) ([69804cd](https://github.com/hatayama/unity-cli-loop/commit/69804cdb5f4b811c0c3afafd2b0317bf4f725070))
* Slow Unity responses now bring the Editor forward ([#1353](https://github.com/hatayama/unity-cli-loop/issues/1353)) ([0e5ee2c](https://github.com/hatayama/unity-cli-loop/commit/0e5ee2c0d0e66b4b484de7a317a770d5b08ab57e))
* Unity stall diagnostics now point to modal dialogs ([#1361](https://github.com/hatayama/unity-cli-loop/issues/1361)) ([dc8ff2c](https://github.com/hatayama/unity-cli-loop/commit/dc8ff2c1432e2e1a377ee2aa910fa0f92c112c1a))

## [3.0.0-beta.35](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.34...cli-v3.0.0-beta.35) (2026-06-15)


### Bug Fixes

* Windows dynamic code snippets are easier to pass safely ([#1346](https://github.com/hatayama/unity-cli-loop/issues/1346)) ([eb91151](https://github.com/hatayama/unity-cli-loop/commit/eb9115183f184bff5ea2fed0471882a42b709c31))

## [3.0.0-beta.34](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.33...cli-v3.0.0-beta.34) (2026-06-15)


### Bug Fixes

* Setup now installs CLI releases that match the required protocol ([#1340](https://github.com/hatayama/unity-cli-loop/issues/1340)) ([91cca52](https://github.com/hatayama/unity-cli-loop/commit/91cca52cf51c0675da413294be7d39ab4ec143fe))

## [3.0.0-beta.33](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.32...cli-v3.0.0-beta.33) (2026-06-14)


### Features

* Gate CLI/Unity compatibility on an IPC protocol version instead of release numbers ([#1329](https://github.com/hatayama/unity-cli-loop/issues/1329)) ([85c21d3](https://github.com/hatayama/unity-cli-loop/commit/85c21d328b8aa412c7e5b60b93e7a89720ee6680))
* Pause point waits now explain their evidence ([#1338](https://github.com/hatayama/unity-cli-loop/issues/1338)) ([0b5b468](https://github.com/hatayama/unity-cli-loop/commit/0b5b468ca1681b8ca750dbbf97267d7bbb6f7cb6))


### Bug Fixes

* Expired pause points now explain how to recover ([#1335](https://github.com/hatayama/unity-cli-loop/issues/1335)) ([2b7ae47](https://github.com/hatayama/unity-cli-loop/commit/2b7ae47e884cd7231a37c83314251d268ad9058b))
* Unity launch waits reliably during slow startup and restart ([#1339](https://github.com/hatayama/unity-cli-loop/issues/1339)) ([6f92adb](https://github.com/hatayama/unity-cli-loop/commit/6f92adbf5761ac33548ee213231980370c9acb0d))
* Windows CLI accepts WSL and Git Bash project paths ([#1334](https://github.com/hatayama/unity-cli-loop/issues/1334)) ([0d6fdeb](https://github.com/hatayama/unity-cli-loop/commit/0d6fdeb479fada253b5475f8d88be1cc6b393de0))

## [3.0.0-beta.32](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.31...cli-v3.0.0-beta.32) (2026-06-13)


### Bug Fixes

* Restore the Unity 2022 execute-dynamic-code fast path and make reload waiting opt-in

## [3.0.0-beta.31](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.24...cli-v3.0.0-beta.31) (2026-06-12)


### Features

* Pause points are easier to drive: unified naming, embedded logs, diagnosis hints, and frame stepping ([#1309](https://github.com/hatayama/unity-cli-loop/issues/1309)) ([875fd91](https://github.com/hatayama/unity-cli-loop/commit/875fd915a0a977cad11bf021fcfe9c0f4e93212d))
* Pause Unity at named debug breaks for inspection ([#1283](https://github.com/hatayama/unity-cli-loop/issues/1283)) ([5628335](https://github.com/hatayama/unity-cli-loop/commit/5628335100e6a6e6abe1a3baa3c2134e4a16b127))


### Bug Fixes

* Cancelled test runs no longer hang, and log retrieval responds faster ([#1321](https://github.com/hatayama/unity-cli-loop/issues/1321)) ([ee81068](https://github.com/hatayama/unity-cli-loop/commit/ee81068727ac31261658c34fe8d8048e6f3d7f4e))
* Launch now confirms when Unity is ready or restarted ([#1301](https://github.com/hatayama/unity-cli-loop/issues/1301)) ([3b72fff](https://github.com/hatayama/unity-cli-loop/commit/3b72fffa4de96d0daae266f925b5de5d2ffdc392))
* Make Unity readiness and PlayMode stop results clearer ([#1300](https://github.com/hatayama/unity-cli-loop/issues/1300)) ([a36e661](https://github.com/hatayama/unity-cli-loop/commit/a36e66185e717ebf03a819a6c6d0906b1797ae98))
* Preserve compile diagnostics across Unity reloads ([#1282](https://github.com/hatayama/unity-cli-loop/issues/1282)) ([447a697](https://github.com/hatayama/unity-cli-loop/commit/447a697883e8f886df9d198d643c8b4751416abd))
* Unity connection stays alive: the server restarts itself after failures and the CLI detects frozen Editors instead of hanging ([#1312](https://github.com/hatayama/unity-cli-loop/issues/1312)) ([0392ca9](https://github.com/hatayama/unity-cli-loop/commit/0392ca93572147022ef8ee8f66ba463319132857))

## [3.0.0-beta.24](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.23...cli-v3.0.0-beta.24) (2026-06-02)


### Bug Fixes

* Compile handles externally changed open Scenes without blocking ([#1261](https://github.com/hatayama/unity-cli-loop/issues/1261)) ([8d6ed1b](https://github.com/hatayama/unity-cli-loop/commit/8d6ed1b7107d9cada658e2f7bb7bbd71122840b7))

## [3.0.0-beta.23](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.22...cli-v3.0.0-beta.23) (2026-05-31)


### Bug Fixes

* Unity packages no longer include development CLI binaries.

## [3.0.0-beta.22](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.21...cli-v3.0.0-beta.22) (2026-05-31)


### Bug Fixes

* Compile commands now finish reliably after Unity reloads scripts ([#1248](https://github.com/hatayama/unity-cli-loop/issues/1248)) ([f593f46](https://github.com/hatayama/unity-cli-loop/commit/f593f463a7519eba992e525290ec3de5cc4fd276))

## [3.0.0-beta.21](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.20...cli-v3.0.0-beta.21) (2026-05-30)


### Bug Fixes

* Native CLI releases no longer fail on stale binaries ([#1242](https://github.com/hatayama/unity-cli-loop/issues/1242)) ([80c1ed0](https://github.com/hatayama/unity-cli-loop/commit/80c1ed07e0a3e5b06d0eb2f21906b2451e6c584a))
* Setup now requires the CLI needed for reliable compile waits ([#1246](https://github.com/hatayama/unity-cli-loop/issues/1246)) ([07c8247](https://github.com/hatayama/unity-cli-loop/commit/07c8247b0801e453eac23c8611d3f572bd675f80))

## [3.0.0-beta.20](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.19...cli-v3.0.0-beta.20) (2026-05-30)


### Bug Fixes

* Compile commands no longer hang across Unity reloads ([#1240](https://github.com/hatayama/unity-cli-loop/issues/1240)) ([12238ce](https://github.com/hatayama/unity-cli-loop/commit/12238ce492e364e3a1999364027cded03ab96262))

## [3.0.0-beta.19](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.18...cli-v3.0.0-beta.19) (2026-05-28)


### Bug Fixes

* Busy responses now show as temporary status messages ([#1227](https://github.com/hatayama/unity-cli-loop/issues/1227)) ([fdd6b98](https://github.com/hatayama/unity-cli-loop/commit/fdd6b98e7f738018754b4a4a32416b2afa451d3b))

## [3.0.0-beta.18](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.17...cli-v3.0.0-beta.18) (2026-05-27)


### Bug Fixes

* Play mode control waits reliably and input overlays avoid missing script warnings ([#1219](https://github.com/hatayama/unity-cli-loop/issues/1219)) ([2ad9f59](https://github.com/hatayama/unity-cli-loop/commit/2ad9f596f7d18b19c0dd012a9442e1d88c100a56))
* Setup now requires the Play Mode wait CLI release ([#1220](https://github.com/hatayama/unity-cli-loop/issues/1220)) ([73db4b4](https://github.com/hatayama/unity-cli-loop/commit/73db4b444f4df8f39757fa0532d303afb7d60da6))
* Unity busy states are easier to diagnose ([#1215](https://github.com/hatayama/unity-cli-loop/issues/1215)) ([fb4713d](https://github.com/hatayama/unity-cli-loop/commit/fb4713d5567110536bb37b157b4abfb25a95994c))

## [3.0.0-beta.17](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.16...cli-v3.0.0-beta.17) (2026-05-26)


### Features

* Tests now save editor changes before running ([#1212](https://github.com/hatayama/unity-cli-loop/issues/1212)) ([ded7d74](https://github.com/hatayama/unity-cli-loop/commit/ded7d7411905f3edeb0c286567c8d7d03ae57aa5))

## [3.0.0-beta.16](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.15...cli-v3.0.0-beta.16) (2026-05-25)


### Bug Fixes

* Make skill instructions use CLI flag syntax ([#1210](https://github.com/hatayama/unity-cli-loop/issues/1210)) ([a11923d](https://github.com/hatayama/unity-cli-loop/commit/a11923dc61cdced2bbe57656058084c726920d8a))

## [3.0.0-beta.15](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.14...cli-v3.0.0-beta.15) (2026-05-25)


### Bug Fixes

* Unity commands recover without stale readiness cleanup ([#1199](https://github.com/hatayama/unity-cli-loop/issues/1199)) ([a4b3f06](https://github.com/hatayama/unity-cli-loop/commit/a4b3f06ad14dd88d465f5ab2fe3b2705a0b4ac4e))

## [3.0.0-beta.14](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.13...cli-v3.0.0-beta.14) (2026-05-23)


### Bug Fixes

* CLI updates use installer scripts from the selected release ([#1190](https://github.com/hatayama/unity-cli-loop/issues/1190)) ([761a8c6](https://github.com/hatayama/unity-cli-loop/commit/761a8c6354f8c8fa497ac046a02a76b2b5b0cb47))

## [3.0.0-beta.13](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.12...cli-v3.0.0-beta.13) (2026-05-23)


### Features

* Improve Windows CLI installs and release provenance ([#1186](https://github.com/hatayama/unity-cli-loop/issues/1186)) ([3dead33](https://github.com/hatayama/unity-cli-loop/commit/3dead3341dc2286031e3287d1ef47da7bfd6ce9c))
* Install uloop natively on macOS ([#1187](https://github.com/hatayama/unity-cli-loop/issues/1187)) ([1c12b49](https://github.com/hatayama/unity-cli-loop/commit/1c12b4991d1da53701ae97f1c1ed6a2fcb032c96))


### Bug Fixes

* Unity commands recover reliably after editor reloads ([#1182](https://github.com/hatayama/unity-cli-loop/issues/1182)) ([7c035ec](https://github.com/hatayama/unity-cli-loop/commit/7c035eccb7beb4a173dc75378c588c3a4e5dcb02))

## [3.0.0-beta.12](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.11...cli-v3.0.0-beta.12) (2026-05-19)


### Bug Fixes

* Unity tool requests no longer overlap during long-running work ([#1164](https://github.com/hatayama/unity-cli-loop/issues/1164)) ([c5a583b](https://github.com/hatayama/unity-cli-loop/commit/c5a583ba457e2c330b8c5150cc101e81b790fb45))
* Windows CLI uninstall no longer leaves stale commands behind ([#1169](https://github.com/hatayama/unity-cli-loop/issues/1169)) ([62a7c88](https://github.com/hatayama/unity-cli-loop/commit/62a7c887af62e31c02d7d0fefdd204f37f8f070b))

## [3.0.0-beta.11](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.10...cli-v3.0.0-beta.11) (2026-05-17)


### Bug Fixes

* Settings no longer shows the CLI as installed after uninstall ([#1154](https://github.com/hatayama/unity-cli-loop/issues/1154)) ([090f0a3](https://github.com/hatayama/unity-cli-loop/commit/090f0a3a180a5d3e7c74dd7b6dbc1b7aab884835))

## [3.0.0-beta.10](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.9...cli-v3.0.0-beta.10) (2026-05-17)


### Bug Fixes

* Improve Unity reload recovery and skill setup reliability ([#1150](https://github.com/hatayama/unity-cli-loop/issues/1150)) ([4556535](https://github.com/hatayama/unity-cli-loop/commit/4556535e69a15a0e8dc117131d860e5a597a84bd))
* Make Unity tool skill descriptions more concise ([#1148](https://github.com/hatayama/unity-cli-loop/issues/1148)) ([c09a5af](https://github.com/hatayama/unity-cli-loop/commit/c09a5afa01deccea694413bd640787c42f40e54d))

## [3.0.0-beta.9](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.8...cli-v3.0.0-beta.9) (2026-05-17)


### Features

* Code execution waits for reload recovery by default ([#1142](https://github.com/hatayama/unity-cli-loop/issues/1142)) ([15d3ad0](https://github.com/hatayama/unity-cli-loop/commit/15d3ad0b2048e95d2fee876a21ba4fac54444d4e))


### Bug Fixes

* CLI commands recover reliably after Unity reloads ([#1136](https://github.com/hatayama/unity-cli-loop/issues/1136)) ([7e45f1e](https://github.com/hatayama/unity-cli-loop/commit/7e45f1e7ba7f9c96d6503faaf3153ddbfd33b9fd))
* CLI recovery stays reliable during Unity readiness updates ([#1139](https://github.com/hatayama/unity-cli-loop/issues/1139)) ([6dbe57b](https://github.com/hatayama/unity-cli-loop/commit/6dbe57ba3397c5e63f7aab90520ebcac8210b74a))
* Make CLI help consistent for native and Unity commands ([#1146](https://github.com/hatayama/unity-cli-loop/issues/1146)) ([802afa3](https://github.com/hatayama/unity-cli-loop/commit/802afa3e23ea405c3cf4ff944e14afaae82bb55e))
* Unity busy detection no longer relies on obsolete lock files ([#1144](https://github.com/hatayama/unity-cli-loop/issues/1144)) ([ba5746f](https://github.com/hatayama/unity-cli-loop/commit/ba5746f1fbfb602ed10dc99f108e4bc761491ceb))

## [3.0.0-beta.8](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.7...cli-v3.0.0-beta.8) (2026-05-16)


### Features

* uloop can uninstall its global command from terminal and Settings ([#1135](https://github.com/hatayama/unity-cli-loop/issues/1135)) ([4122d57](https://github.com/hatayama/unity-cli-loop/commit/4122d57eb79cbe491c633063b99e22484816d355))

## [3.0.0-beta.7](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.6...cli-v3.0.0-beta.7) (2026-05-11)


### Features

* Native CLI is distributed as a single uloop binary ([#1100](https://github.com/hatayama/unity-cli-loop/issues/1100)) ([1180fae](https://github.com/hatayama/unity-cli-loop/commit/1180fae9be33c3f1cc6e35044b2ee42130052e93))

## [3.0.0-beta.6](https://github.com/hatayama/unity-cli-loop/compare/cli-v3.0.0-beta.5...cli-v3.0.0-beta.6) (2026-05-11)

### Features

* unify the native CLI into one global uloop binary
