# Changelog

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
