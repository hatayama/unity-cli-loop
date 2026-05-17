# Changelog

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
