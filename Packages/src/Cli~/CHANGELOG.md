# Changelog

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
