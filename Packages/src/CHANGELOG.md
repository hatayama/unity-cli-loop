# Changelog

## [3.0.0-beta.53](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.52...v3.0.0-beta.53) (2026-07-19)


### Bug Fixes

* Improve compile and pause-point skill guidance for AI agents ([#1836](https://github.com/hatayama/unity-cli-loop/issues/1836)) ([c7c650e](https://github.com/hatayama/unity-cli-loop/commit/c7c650eceaacb0d0e32d6c1208224c205d1e1210))
* Keep the Setup Wizard skill target dropdown available ([#1842](https://github.com/hatayama/unity-cli-loop/issues/1842)) ([2d73535](https://github.com/hatayama/unity-cli-loop/commit/2d735357b3e378947b98aa3d4eea02f2e3b0f60a))
* resolve macOS E2E failures ([#1834](https://github.com/hatayama/unity-cli-loop/issues/1834)) ([6d498b4](https://github.com/hatayama/unity-cli-loop/commit/6d498b490265f986c7250e96ef0a19bc50a16f78))
* Setup falls back to skill target selection when folders are missing ([#1839](https://github.com/hatayama/unity-cli-loop/issues/1839)) ([7deb9d2](https://github.com/hatayama/unity-cli-loop/commit/7deb9d2412b982e02fe0fc8ea57f8f261e4ef1dd))
* suppress expected launch-time worker errors ([#1837](https://github.com/hatayama/unity-cli-loop/issues/1837)) ([0be8528](https://github.com/hatayama/unity-cli-loop/commit/0be8528a6c786f699b1fed073437f880bda53c75))

## [3.0.0-beta.52](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.51...v3.0.0-beta.52) (2026-07-18)


### Bug Fixes

* align tool exit codes with response success ([#1824](https://github.com/hatayama/unity-cli-loop/issues/1824)) ([1cc123b](https://github.com/hatayama/unity-cli-loop/commit/1cc123b8d450b137ef489d55af0d13f9f587c16f))

## [3.0.0-beta.51](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.50...v3.0.0-beta.51) (2026-07-17)


### Bug Fixes

* make Windows v3 workflows reliable ([#1818](https://github.com/hatayama/unity-cli-loop/issues/1818)) ([21eae0a](https://github.com/hatayama/unity-cli-loop/commit/21eae0a96af05355cbf57eb3ab98dd7388fc7b2a))

## [3.0.0-beta.50](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.49...v3.0.0-beta.50) (2026-07-15)


### Features

* add pause point capture modes and history ([#1729](https://github.com/hatayama/unity-cli-loop/issues/1729)) ([0b5e72e](https://github.com/hatayama/unity-cli-loop/commit/0b5e72e788a6f032001ee3670c117d8034089234))
* add pause-point watch expressions ([#1733](https://github.com/hatayama/unity-cli-loop/issues/1733)) ([875835f](https://github.com/hatayama/unity-cli-loop/commit/875835fc71deb70ccdc0bea0d67582a0d42a2f80))
* Cap .uloop/outputs folders at 20 files automatically ([#1772](https://github.com/hatayama/unity-cli-loop/issues/1772)) ([8d67dae](https://github.com/hatayama/unity-cli-loop/commit/8d67daec5b8ec48ef43ff31bf40e094226ffe438))
* Improve CLI guidance for tool enums, busy errors, and dynamic-code diagnostics ([7318b9d](https://github.com/hatayama/unity-cli-loop/commit/7318b9d1dcead7b52241e88adab7538b47a5f95a))
* Improve pause point observability with cleared reasons, collection previews, and raw capture ([03c656b](https://github.com/hatayama/unity-cli-loop/commit/03c656b8734de3d87a32dc7e7f90fea61031eeaf))
* Pause point wait command is now await-pause-point ([#1698](https://github.com/hatayama/unity-cli-loop/issues/1698)) ([f1d0a9d](https://github.com/hatayama/unity-cli-loop/commit/f1d0a9d1c6c72a8699a2468e68a05262a50642dc))
* Pause-point snapshots now identify which instance was hit ([#1737](https://github.com/hatayama/unity-cli-loop/issues/1737)) ([a086ac9](https://github.com/hatayama/unity-cli-loop/commit/a086ac9f192dcc2e7c087c6f475f42b0962052e5))
* Prompt to remove the temporary V3 migration skill after docs are migrated ([#1711](https://github.com/hatayama/unity-cli-loop/issues/1711)) ([a6eff6e](https://github.com/hatayama/unity-cli-loop/commit/a6eff6e889a9d980040a1a1901b5e2390bfe0214))


### Bug Fixes

* captured variables stay readable after re-enabling a pause point while paused ([#1734](https://github.com/hatayama/unity-cli-loop/issues/1734)) ([4e63cc5](https://github.com/hatayama/unity-cli-loop/commit/4e63cc5c56ba2b9257ac334ef2fa0a41c0384205))
* compile-consistency — external scene hold, compile wait/TTL align, API Update guidance ([#1760](https://github.com/hatayama/unity-cli-loop/issues/1760)) ([247cb0c](https://github.com/hatayama/unity-cli-loop/commit/247cb0c62a6a87fd56dba0126334fb5061d4d081))
* Device Simulator support for screenshot and mouse flow ([#1769](https://github.com/hatayama/unity-cli-loop/issues/1769)) ([0af2840](https://github.com/hatayama/unity-cli-loop/commit/0af28403ad56394779d4719c7b1b241d328cd00c))
* dynamic-code cancel and Editor shutdown no longer hang on stuck work ([#1753](https://github.com/hatayama/unity-cli-loop/issues/1753)) ([bc1233a](https://github.com/hatayama/unity-cli-loop/commit/bc1233acd79449a1020821b021f85b5cb68b0763))
* Harden CLI distribution and Unity IPC security ([#1794](https://github.com/hatayama/unity-cli-loop/issues/1794)) ([b5ca16b](https://github.com/hatayama/unity-cli-loop/commit/b5ca16b34fc8359466183c0cac30f2d77e862212))
* harden execute-dynamic-code against reload busy sticks and worker lifecycle races ([#1765](https://github.com/hatayama/unity-cli-loop/issues/1765)) ([625282d](https://github.com/hatayama/unity-cli-loop/commit/625282d8bc7dc31aef2751dc7a7fb1e651cfada3))
* Harden IPC contracts, empty RPC errors, and Settings async UI ([#1778](https://github.com/hatayama/unity-cli-loop/issues/1778)) ([0dffc75](https://github.com/hatayama/unity-cli-loop/commit/0dffc753bec575a29252430a59540ad3e0812848))
* Harden V2-to-V3 third-party migration for safe apply and reliable scans ([#1710](https://github.com/hatayama/unity-cli-loop/issues/1710)) ([b4fbb0d](https://github.com/hatayama/unity-cli-loop/commit/b4fbb0db6b8837e4036637e11684bc08da427935))
* Improve CLI PlayMode reliability for background input simulation ([#1714](https://github.com/hatayama/unity-cli-loop/issues/1714)) ([8845709](https://github.com/hatayama/unity-cli-loop/commit/8845709d3f70af6fc50ae98501b184f659ef5a33))
* pause-point disconnect no longer leaves Play Mode stuck; quiet-save before CLI Play ([#1756](https://github.com/hatayama/unity-cli-loop/issues/1756)) ([3b17b68](https://github.com/hatayama/unity-cli-loop/commit/3b17b68ebc485d7b700ef7e10837fc5a258021cc))
* run-tests hangs no longer hold the tool slot for up to 30 minutes ([#1742](https://github.com/hatayama/unity-cli-loop/issues/1742)) ([efa4898](https://github.com/hatayama/unity-cli-loop/commit/efa48982e37e39dd4d6b6459d2616cefb04eb81a))
* run-tests timeout can cancel in-flight Test Runner jobs ([#1743](https://github.com/hatayama/unity-cli-loop/issues/1743)) ([b348ad5](https://github.com/hatayama/unity-cli-loop/commit/b348ad562dc1d9be0cdd44cd5c18773e5ccee2ab))
* Settings no longer shows watch tools as separate toggles ([#1789](https://github.com/hatayama/unity-cli-loop/issues/1789)) ([dad66b7](https://github.com/hatayama/unity-cli-loop/commit/dad66b7c08c3f0b1e9eaa87e78e2d32eca7f2072))
* Tool Settings pause-point toggle now gates every pause point command ([#1700](https://github.com/hatayama/unity-cli-loop/issues/1700)) ([8785326](https://github.com/hatayama/unity-cli-loop/commit/8785326661d899751b8ba4ba4d20aebac2b78cc8))

## [3.0.0-beta.49](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.48...v3.0.0-beta.49) (2026-07-11)


### Features

* Add a raycast tool for checking what a Game View coordinate hits in 3D physics ([#1659](https://github.com/hatayama/unity-cli-loop/issues/1659)) ([5352e00](https://github.com/hatayama/unity-cli-loop/commit/5352e005c0dcfec0081a4610f481d41c668b5057))
* Capture and format pause-point variables (locals, parameters, instance fields, UnityEngine.Object) ([#1682](https://github.com/hatayama/unity-cli-loop/issues/1682)) ([3818f79](https://github.com/hatayama/unity-cli-loop/commit/3818f799b8b59b20f02aae064639cb424c2a3dbd))
* Enable pause points by source file and line ([#1684](https://github.com/hatayama/unity-cli-loop/issues/1684)) ([272edb3](https://github.com/hatayama/unity-cli-loop/commit/272edb3d665bc54bfe15e84a1d71b36d9cd20c7c))
* **package:** Inject capture calls at source lines with Harmony ([#1683](https://github.com/hatayama/unity-cli-loop/issues/1683)) ([0159711](https://github.com/hatayama/unity-cli-loop/commit/01597115e943ad019a46702c83f0e0c34fd99874))
* **package:** Resolve source file and line to patch locations via portable PDBs ([#1681](https://github.com/hatayama/unity-cli-loop/issues/1681)) ([2cf5fe2](https://github.com/hatayama/unity-cli-loop/commit/2cf5fe285006a336dc0aeb7977ae3ce7d668553a))
* Raycast annotation now labels every UI-split closed region separately ([#1667](https://github.com/hatayama/unity-cli-loop/issues/1667)) ([1444de6](https://github.com/hatayama/unity-cli-loop/commit/1444de61018b361454b43f39445e75caf232a617))
* Screenshot tool can now annotate 3D physics raycast candidates ([#1661](https://github.com/hatayama/unity-cli-loop/issues/1661)) ([c5d2fd0](https://github.com/hatayama/unity-cli-loop/commit/c5d2fd0dd1d30988299730f9074d3a797660717c))


### Bug Fixes

* Async setup and input cleanup carry cancellation tokens ([1a0ebe6](https://github.com/hatayama/unity-cli-loop/commit/1a0ebe63d484b04a483afb33dfc2317c9e899f01))
* CLI-only skills remain discoverable when project path casing differs ([#1616](https://github.com/hatayama/unity-cli-loop/issues/1616)) ([fcd6a20](https://github.com/hatayama/unity-cli-loop/commit/fcd6a20d3dcfcd614113f0f7060655c88fca686c))
* Dispatcher requires the project runner pin instead of parsing CliConstants ([#1501](https://github.com/hatayama/unity-cli-loop/issues/1501)) ([2012eb8](https://github.com/hatayama/unity-cli-loop/commit/2012eb882cf5bef0de72a49d2cda08f12586daf3))
* Domain reload waits complete during recovery ([e80b630](https://github.com/hatayama/unity-cli-loop/commit/e80b630d592279f3e2837ef1edeaf1065efe54f4))
* Duplicate skills now follow stable source priority ([#1614](https://github.com/hatayama/unity-cli-loop/issues/1614)) ([95270bb](https://github.com/hatayama/unity-cli-loop/commit/95270bb51990a6ad10e84e2b9941b884514b009c))
* Dynamic code snippets now recognize bare Object and Random calls consistently ([#1657](https://github.com/hatayama/unity-cli-loop/issues/1657)) ([ce5c364](https://github.com/hatayama/unity-cli-loop/commit/ce5c364996b19f02f393e110cb8e429247b3cd86))
* execute-dynamic-code no longer hides internal errors behind a generic failure message ([#1521](https://github.com/hatayama/unity-cli-loop/issues/1521)) ([138ad9e](https://github.com/hatayama/unity-cli-loop/commit/138ad9e55ad662d47e28fd291c9ac656299e1fac))
* Ignore malformed RPC capability metadata ([386f906](https://github.com/hatayama/unity-cli-loop/commit/386f906036bd17b9f2e3c324fc0ee2f75b4f86d0))
* Invalid tool parameters now return a readable error result instead of an RPC failure ([#1522](https://github.com/hatayama/unity-cli-loop/issues/1522)) ([2aff8bd](https://github.com/hatayama/unity-cli-loop/commit/2aff8bdac893bc234d812cfdc05cb40f7b99045d))
* Local skill packages no longer include stale cached skills ([#1615](https://github.com/hatayama/unity-cli-loop/issues/1615)) ([9388b91](https://github.com/hatayama/unity-cli-loop/commit/9388b91801ea4ed76aa5c1251861a0efb9210d6c))
* Mouse click and long-press coordinates now match the Game View's actual resolution ([#1662](https://github.com/hatayama/unity-cli-loop/issues/1662)) ([f6574dd](https://github.com/hatayama/unity-cli-loop/commit/f6574dddc3095acd588b4180956ea42de604a934))
* Native CLI setup avoids duplicate PATH entries ([#1621](https://github.com/hatayama/unity-cli-loop/issues/1621)) ([345c027](https://github.com/hatayama/unity-cli-loop/commit/345c027b4e1d851adb98f181a450fe9b2fc0b16d))
* **package:** Clear active pause points before running tests ([#1690](https://github.com/hatayama/unity-cli-loop/issues/1690)) ([018872f](https://github.com/hatayama/unity-cli-loop/commit/018872f690244df9cfcdf3e48c5c5d39a9ab9ae9))
* **package:** Reject PlayMode test runs while the Editor is paused ([#1687](https://github.com/hatayama/unity-cli-loop/issues/1687)) ([998ce19](https://github.com/hatayama/unity-cli-loop/commit/998ce198bb104b2ee3644b0ba61757677f26a07b))
* **package:** Stop tools from hanging when a pause point fires mid-command ([#1685](https://github.com/hatayama/unity-cli-loop/issues/1685)) ([442f9bf](https://github.com/hatayama/unity-cli-loop/commit/442f9bfddd9ae5a435baf57ab7d23d4e8cdd9963))
* **package:** Trim redundant generic content from tool skills ([#1691](https://github.com/hatayama/unity-cli-loop/issues/1691)) ([d2a178b](https://github.com/hatayama/unity-cli-loop/commit/d2a178b9e3681480c5ede81bb4e51c0a318aa1d4))
* **package:** Update stale pause point guidance in tool skills ([#1688](https://github.com/hatayama/unity-cli-loop/issues/1688)) ([ec3f545](https://github.com/hatayama/unity-cli-loop/commit/ec3f545314057455260773adb5ac9272db4895fc))
* Quoted skill metadata is recognized consistently ([#1613](https://github.com/hatayama/unity-cli-loop/issues/1613)) ([49f0302](https://github.com/hatayama/unity-cli-loop/commit/49f0302a0582587a791edaf075cd70487bcfa33d))
* Raycast grid annotation no longer misses rotated or split colliders ([#1666](https://github.com/hatayama/unity-cli-loop/issues/1666)) ([a92a8fa](https://github.com/hatayama/unity-cli-loop/commit/a92a8fae760cc70e42891d23df4567f96a2cbf5e))
* Read minimum version requirements from the project runner pin ([#1506](https://github.com/hatayama/unity-cli-loop/issues/1506)) ([18bf780](https://github.com/hatayama/unity-cli-loop/commit/18bf780dc31f6105902c383186e97c4aec7c8772))
* Recovery completion no longer surfaces as UI errors ([b095e5f](https://github.com/hatayama/unity-cli-loop/commit/b095e5f998579b4ea05161a5c918fe2ec1b4355d))
* Reject malformed dispatcher versions consistently ([0d0b3dc](https://github.com/hatayama/unity-cli-loop/commit/0d0b3dca7d69a99159c75d454e8994f04d063466))
* Remove the dispatcher contract integer generation ([#1504](https://github.com/hatayama/unity-cli-loop/issues/1504)) ([d2ddbce](https://github.com/hatayama/unity-cli-loop/commit/d2ddbce87d9bd68b53a1e202e9ce43996b61acf6))
* run-tests errors now show the original failure instead of a generic message ([#1520](https://github.com/hatayama/unity-cli-loop/issues/1520)) ([1257fcd](https://github.com/hatayama/unity-cli-loop/commit/1257fcd6ed0e075a13032c1c4328dd41cb0f2574))
* Server recovery now surfaces cleanup failures ([c41526c](https://github.com/hatayama/unity-cli-loop/commit/c41526c44b01d1e0b3f2bf4e10b50f83a0afd329))
* Shared compiler restarts no longer leak process handles ([#1630](https://github.com/hatayama/unity-cli-loop/issues/1630)) ([8f11be5](https://github.com/hatayama/unity-cli-loop/commit/8f11be5bf9ed1e792536293fc1b15b73024aff4d))
* Shared compiler shutdown no longer leaves worker processes running ([#1631](https://github.com/hatayama/unity-cli-loop/issues/1631)) ([9b2bf86](https://github.com/hatayama/unity-cli-loop/commit/9b2bf86a80a5aaa6da8269805075d37d035ac696))
* Shrink the project runner pin schema to two fields ([#1503](https://github.com/hatayama/unity-cli-loop/issues/1503)) ([adceaed](https://github.com/hatayama/unity-cli-loop/commit/adceaedd78a8b7f608f5c2179e1ee799fdecb136))
* Simulating UI input now fails clearly when PlayMode is paused ([#1519](https://github.com/hatayama/unity-cli-loop/issues/1519)) ([5b903ed](https://github.com/hatayama/unity-cli-loop/commit/5b903ed2560af9e12159c0ded1245b68e2978c95))
* Surface startup recovery failures when no server is restored ([d5e5d55](https://github.com/hatayama/unity-cli-loop/commit/d5e5d550595f2403ea830eded2cc4533abaadc84))
* UI drag simulation now keeps mouse position in sync during drags ([#1658](https://github.com/hatayama/unity-cli-loop/issues/1658)) ([effbd78](https://github.com/hatayama/unity-cli-loop/commit/effbd787c7114e784ceed3c27b0e0afb25728d5e))
* UTF-16BE skill files keep their byte order during setup ([#1618](https://github.com/hatayama/unity-cli-loop/issues/1618)) ([7b09e42](https://github.com/hatayama/unity-cli-loop/commit/7b09e42460e1b982aef489b8c0aa941fc51c044d))
* Verify installer script checksums before Unity Editor installs it ([#1668](https://github.com/hatayama/unity-cli-loop/issues/1668)) ([bf33af3](https://github.com/hatayama/unity-cli-loop/commit/bf33af3e6c533f20f2ecdc94fcaa95d6b056ff99))
* Worker compiler timeouts no longer block indefinitely ([#1628](https://github.com/hatayama/unity-cli-loop/issues/1628)) ([e1eb989](https://github.com/hatayama/unity-cli-loop/commit/e1eb9894a53edb5c3f47601f7bcf4ceadeb1a3b6))

## [3.0.0-beta.48](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.47...v3.0.0-beta.48) (2026-07-01)


### Features

* Unity launch now opens projects with compiler errors by default ([#1449](https://github.com/hatayama/unity-cli-loop/issues/1449)) ([990b046](https://github.com/hatayama/unity-cli-loop/commit/990b04636b3e836a9ed73b8c4746c2e395cb5f47))

## [3.0.0-beta.47](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.46...v3.0.0-beta.47) (2026-07-01)


### Bug Fixes

* Unity no longer pauses on external change dialogs after focus return ([#1447](https://github.com/hatayama/unity-cli-loop/issues/1447)) ([5972b81](https://github.com/hatayama/unity-cli-loop/commit/5972b81ac8437a3cb867202177598371df854c4d))

## [3.0.0-beta.46](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.45...v3.0.0-beta.46) (2026-06-30)


### Bug Fixes

* Release PRs explain release component roles ([#1437](https://github.com/hatayama/unity-cli-loop/issues/1437)) ([a2824fa](https://github.com/hatayama/unity-cli-loop/commit/a2824fafd88dc5b2e263f6cded15856fe61a31bd))
* Skill updates no longer show a completion dialog ([#1432](https://github.com/hatayama/unity-cli-loop/issues/1432)) ([b99fb41](https://github.com/hatayama/unity-cli-loop/commit/b99fb41c1d78512804a4b71fe98b6eedef8575a1))
* Windows update checks no longer interrupt routine commands ([#1434](https://github.com/hatayama/unity-cli-loop/issues/1434)) ([02ce6cc](https://github.com/hatayama/unity-cli-loop/commit/02ce6cc3dbeb92360ebcd5c5318eae6b9ca87c7c))

## [3.0.0-beta.45](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.44...v3.0.0-beta.45) (2026-06-29)


### Features

* Launch Unity with compiler errors or a chosen Editor version ([#1423](https://github.com/hatayama/unity-cli-loop/issues/1423)) ([5ce43fa](https://github.com/hatayama/unity-cli-loop/commit/5ce43fa5e5ad5d02a338fb315ecd575cefa77f3c))
* Project runner releases now use clearer names ([#1427](https://github.com/hatayama/unity-cli-loop/issues/1427)) ([c3e41ce](https://github.com/hatayama/unity-cli-loop/commit/c3e41ce55896ae63358c2cbbabff9e1a25921e44))

## [3.0.0-beta.44](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.43...v3.0.0-beta.44) (2026-06-28)


### Bug Fixes

* Setup now prompts when dispatcher requirements change ([#1420](https://github.com/hatayama/unity-cli-loop/issues/1420)) ([1870ac4](https://github.com/hatayama/unity-cli-loop/commit/1870ac445db47a431966d4fcdeb017629af0662b))

## [3.0.0-beta.43](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.42...v3.0.0-beta.43) (2026-06-27)


### Bug Fixes

* Dispatcher releases no longer look stable during v3 beta ([#1418](https://github.com/hatayama/unity-cli-loop/issues/1418)) ([bebb5cd](https://github.com/hatayama/unity-cli-loop/commit/bebb5cda7583a66969dcf4183316ec1d658014bd))
* First dispatcher commands now show CLI download status ([#1419](https://github.com/hatayama/unity-cli-loop/issues/1419)) ([722d799](https://github.com/hatayama/unity-cli-loop/commit/722d799ff12db5cdffea370939328c00a34aa461))
* Keep beta dispatcher releases out of Latest ([#1415](https://github.com/hatayama/unity-cli-loop/issues/1415)) ([a2df843](https://github.com/hatayama/unity-cli-loop/commit/a2df843f0106d14cc68b64419cf84861adca8f8c))
* Setup now shows CLI update prompts for outdated CLI installs ([#1417](https://github.com/hatayama/unity-cli-loop/issues/1417)) ([24a170f](https://github.com/hatayama/unity-cli-loop/commit/24a170f92f4228d9b41b53116d7ee251fec851ad))

## [3.0.0-beta.42](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.41...v3.0.0-beta.42) (2026-06-27)


### Features

* Let uloop launchers use project-pinned CLI versions ([#1413](https://github.com/hatayama/unity-cli-loop/issues/1413)) ([3e39bed](https://github.com/hatayama/unity-cli-loop/commit/3e39bed15f6b65ea54e68ca36ea2c4be898f4c7a))

## [3.0.0-beta.41](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.40...v3.0.0-beta.41) (2026-06-26)


### Features

* Projects can run pinned CLI versions ([#1411](https://github.com/hatayama/unity-cli-loop/issues/1411)) ([1637a34](https://github.com/hatayama/unity-cli-loop/commit/1637a34ac47a31025d37db511ef5736baa745f57))
* Settings now shows selectable tool details ([#1408](https://github.com/hatayama/unity-cli-loop/issues/1408)) ([07b8401](https://github.com/hatayama/unity-cli-loop/commit/07b84013a87d4ef5ca9a6c14af95339e22248699))


### Bug Fixes

* Keep release PRs compatible with the CLI launcher ([cce7e15](https://github.com/hatayama/unity-cli-loop/commit/cce7e15e0c3d35b320bd81df366247102d8f8b91))
* Tool Settings now hides pause point helper tools ([#1410](https://github.com/hatayama/unity-cli-loop/issues/1410)) ([7a7057d](https://github.com/hatayama/unity-cli-loop/commit/7a7057d3f3861accc20f31b1b31f00606f07feb7))

## [3.0.0-beta.40](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.39...v3.0.0-beta.40) (2026-06-25)


### Bug Fixes

* Keep Go and C# complexity checks under fifteen ([#1403](https://github.com/hatayama/unity-cli-loop/issues/1403)) ([e77d893](https://github.com/hatayama/unity-cli-loop/commit/e77d8938a1ebea3439236c91753677cb0074aa27))
* Migration handles deeply nested Windows project files ([#1396](https://github.com/hatayama/unity-cli-loop/issues/1396)) ([bd3d7ed](https://github.com/hatayama/unity-cli-loop/commit/bd3d7ed7f55e81664ab911523acd32bc2da5a75c))

## [3.0.0-beta.39](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.38...v3.0.0-beta.39) (2026-06-22)


### Bug Fixes

* Keep V3 CLI JSON output compatible with V2 ([#1391](https://github.com/hatayama/unity-cli-loop/issues/1391)) ([d07ffa2](https://github.com/hatayama/unity-cli-loop/commit/d07ffa204fbc864eb5b0c0db891baf8e277889ae))
* Make V3 migration safer and easier to run ([#1386](https://github.com/hatayama/unity-cli-loop/issues/1386)) ([03087ce](https://github.com/hatayama/unity-cli-loop/commit/03087ce3b9df4255e5a867195a939ac490d687ae))

## [3.0.0-beta.38](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.37...v3.0.0-beta.38) (2026-06-21)


### Features

* V3 CLI invocation migration skills are available from the wizard ([#1382](https://github.com/hatayama/unity-cli-loop/issues/1382)) ([a327f4a](https://github.com/hatayama/unity-cli-loop/commit/a327f4ad2179b5cd44b1aa5d2747494712edf657))


### Bug Fixes

* get-logs works with active Console filters ([#1381](https://github.com/hatayama/unity-cli-loop/issues/1381)) ([ffd3c91](https://github.com/hatayama/unity-cli-loop/commit/ffd3c9138484cb95c275fa9720bc8e24cfafd500))
* Migration checks are faster and more reliable ([#1376](https://github.com/hatayama/unity-cli-loop/issues/1376)) ([07f3020](https://github.com/hatayama/unity-cli-loop/commit/07f302047b62c3b3ebb1d9e498c3961aa0a1e8ca))
* Migration now upgrades v3 editor tools without leaving compile errors ([#1374](https://github.com/hatayama/unity-cli-loop/issues/1374)) ([7dec89f](https://github.com/hatayama/unity-cli-loop/commit/7dec89f032e0b572b423227fc69dbb3190a65c52))

## [3.0.0-beta.37](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.36...v3.0.0-beta.37) (2026-06-18)


### Bug Fixes

* Dynamic code cancellation no longer leaves Unity busy ([#1369](https://github.com/hatayama/unity-cli-loop/issues/1369)) ([e7e58e9](https://github.com/hatayama/unity-cli-loop/commit/e7e58e9b0fda20d9cd77962acc3cbd05e4b278a9))
* Prevent bundled dependency conflicts in consuming Unity projects ([#1364](https://github.com/hatayama/unity-cli-loop/issues/1364)) ([f195892](https://github.com/hatayama/unity-cli-loop/commit/f195892356db23dc9ffca9d5b59064c1130e3c78))
* Simplify and deduplicate agent skill definitions and code references ([#1367](https://github.com/hatayama/unity-cli-loop/issues/1367)) ([83cfc25](https://github.com/hatayama/unity-cli-loop/commit/83cfc2525930e13f70828bc890cf750569e6b5ad))
* Simulated UI clicks now reach clipped overlay controls ([#1366](https://github.com/hatayama/unity-cli-loop/issues/1366)) ([925543d](https://github.com/hatayama/unity-cli-loop/commit/925543d423970804d506c63f26ed7146af33ceaa))

## [3.0.0-beta.36](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.35...v3.0.0-beta.36) (2026-06-17)


### Features

* Dynamic code execution uses full Unity access by default ([#1362](https://github.com/hatayama/unity-cli-loop/issues/1362)) ([5b64443](https://github.com/hatayama/unity-cli-loop/commit/5b64443b2593a44caba2f8d67127cb89b92997f4))


### Bug Fixes

* Clarify skill guidance for pause points and Windows code execution ([#1363](https://github.com/hatayama/unity-cli-loop/issues/1363)) ([7250f9d](https://github.com/hatayama/unity-cli-loop/commit/7250f9dd5b3d919da10525271573934fcd4e9e0a))
* CLI JSON output now uses consistent field names ([#1360](https://github.com/hatayama/unity-cli-loop/issues/1360)) ([e20c8b3](https://github.com/hatayama/unity-cli-loop/commit/e20c8b330e9f3651554ed2a0184f7d1a49d585eb))
* Make PowerShell multiline code guidance clearer ([#1355](https://github.com/hatayama/unity-cli-loop/issues/1355)) ([3571823](https://github.com/hatayama/unity-cli-loop/commit/3571823293db8d13f02d75c1acf2125405ebdece))
* PlayMode start reports compiler errors instead of timing out ([#1354](https://github.com/hatayama/unity-cli-loop/issues/1354)) ([69804cd](https://github.com/hatayama/unity-cli-loop/commit/69804cdb5f4b811c0c3afafd2b0317bf4f725070))
* Update pause point skill description ([#1351](https://github.com/hatayama/unity-cli-loop/issues/1351)) ([2ee34eb](https://github.com/hatayama/unity-cli-loop/commit/2ee34eb277ecd94fd06d1ee9096a98bbfd3bf3db))

## [3.0.0-beta.35](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.34...v3.0.0-beta.35) (2026-06-15)


### Bug Fixes

* Test runs no longer race Play Mode cleanup ([#1347](https://github.com/hatayama/unity-cli-loop/issues/1347)) ([6b53da5](https://github.com/hatayama/unity-cli-loop/commit/6b53da5ee581cd8b8045fa65eddbc213c6968af2))
* Windows dynamic code snippets are easier to pass safely ([#1346](https://github.com/hatayama/unity-cli-loop/issues/1346)) ([eb91151](https://github.com/hatayama/unity-cli-loop/commit/eb9115183f184bff5ea2fed0471882a42b709c31))

## [3.0.0-beta.34](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.33...v3.0.0-beta.34) (2026-06-15)


### Bug Fixes

* Clarify pause point skill guidance ([#1345](https://github.com/hatayama/unity-cli-loop/issues/1345)) ([d93c28e](https://github.com/hatayama/unity-cli-loop/commit/d93c28e68596b5743586bf87841c9084f48c8d92))
* Forced compilation now explains unknown result fields ([#1342](https://github.com/hatayama/unity-cli-loop/issues/1342)) ([7dd609d](https://github.com/hatayama/unity-cli-loop/commit/7dd609d49ff2f5911766c0cf9901120719ce6f24))
* Setup now installs CLI releases that match the required protocol ([#1340](https://github.com/hatayama/unity-cli-loop/issues/1340)) ([91cca52](https://github.com/hatayama/unity-cli-loop/commit/91cca52cf51c0675da413294be7d39ab4ec143fe))

## [3.0.0-beta.33](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.32...v3.0.0-beta.33) (2026-06-14)


### Features

* Gate CLI/Unity compatibility on an IPC protocol version instead of release numbers ([#1329](https://github.com/hatayama/unity-cli-loop/issues/1329)) ([85c21d3](https://github.com/hatayama/unity-cli-loop/commit/85c21d328b8aa412c7e5b60b93e7a89720ee6680))
* Pause point waits now explain their evidence ([#1338](https://github.com/hatayama/unity-cli-loop/issues/1338)) ([0b5b468](https://github.com/hatayama/unity-cli-loop/commit/0b5b468ca1681b8ca750dbbf97267d7bbb6f7cb6))


### Bug Fixes

* Expired pause points now explain how to recover ([#1335](https://github.com/hatayama/unity-cli-loop/issues/1335)) ([2b7ae47](https://github.com/hatayama/unity-cli-loop/commit/2b7ae47e884cd7231a37c83314251d268ad9058b))
* Keep dynamic code execution responsive by default ([#1331](https://github.com/hatayama/unity-cli-loop/issues/1331)) ([148c109](https://github.com/hatayama/unity-cli-loop/commit/148c1098bb052b8d638cd7345d86a7843d78118c))
* run-tests now identifies zero-test runs ([#1336](https://github.com/hatayama/unity-cli-loop/issues/1336)) ([1ff3b6b](https://github.com/hatayama/unity-cli-loop/commit/1ff3b6bf64adf0161fcfaed3ea454205ce752d5f))
* Unity tools no longer hang while waiting for editor frames ([#1333](https://github.com/hatayama/unity-cli-loop/issues/1333)) ([9b415a2](https://github.com/hatayama/unity-cli-loop/commit/9b415a2b54bce8fcfef2cb7d4a24b1ceb24db2fc))

## [3.0.0-beta.32](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.31...v3.0.0-beta.32) (2026-06-12)


### Features

* Pause points are easier to drive: unified naming, embedded logs, diagnosis hints, and frame stepping ([#1309](https://github.com/hatayama/unity-cli-loop/issues/1309)) ([875fd91](https://github.com/hatayama/unity-cli-loop/commit/875fd915a0a977cad11bf021fcfe9c0f4e93212d))
* Pause Unity at named debug breaks for inspection ([#1283](https://github.com/hatayama/unity-cli-loop/issues/1283)) ([5628335](https://github.com/hatayama/unity-cli-loop/commit/5628335100e6a6e6abe1a3baa3c2134e4a16b127))


### Bug Fixes

* Cancelled test runs no longer hang, and log retrieval responds faster ([#1321](https://github.com/hatayama/unity-cli-loop/issues/1321)) ([ee81068](https://github.com/hatayama/unity-cli-loop/commit/ee81068727ac31261658c34fe8d8048e6f3d7f4e))
* Clarify transient debug-break guidance ([#1289](https://github.com/hatayama/unity-cli-loop/issues/1289)) ([476b2da](https://github.com/hatayama/unity-cli-loop/commit/476b2dae0edaf43c5069140473403bc64ddde841))
* Input simulation no longer hangs when Editor updates stall ([#1306](https://github.com/hatayama/unity-cli-loop/issues/1306)) ([7498397](https://github.com/hatayama/unity-cli-loop/commit/7498397c455123b7e4f8d5765f54357e816324b9))
* Launch now confirms when Unity is ready or restarted ([#1301](https://github.com/hatayama/unity-cli-loop/issues/1301)) ([3b72fff](https://github.com/hatayama/unity-cli-loop/commit/3b72fffa4de96d0daae266f925b5de5d2ffdc392))
* Make Unity readiness and PlayMode stop results clearer ([#1300](https://github.com/hatayama/unity-cli-loop/issues/1300)) ([a36e661](https://github.com/hatayama/unity-cli-loop/commit/a36e66185e717ebf03a819a6c6d0906b1797ae98))
* Other local users can no longer connect to the Unity Editor's uloop channel on Windows ([#1322](https://github.com/hatayama/unity-cli-loop/issues/1322)) ([8362dd7](https://github.com/hatayama/unity-cli-loop/commit/8362dd7127c024916133ed587b9f4372ed259356))
* Preserve compile diagnostics across Unity reloads ([#1282](https://github.com/hatayama/unity-cli-loop/issues/1282)) ([447a697](https://github.com/hatayama/unity-cli-loop/commit/447a697883e8f886df9d198d643c8b4751416abd))
* Setup no longer opens after upgrades with no CLI or skill updates ([#1277](https://github.com/hatayama/unity-cli-loop/issues/1277)) ([b0608f3](https://github.com/hatayama/unity-cli-loop/commit/b0608f3901e2b67c5df8d387dd13f143e8336424))
* Unity connection stays alive: the server restarts itself after failures and the CLI detects frozen Editors instead of hanging ([#1312](https://github.com/hatayama/unity-cli-loop/issues/1312)) ([0392ca9](https://github.com/hatayama/unity-cli-loop/commit/0392ca93572147022ef8ee8f66ba463319132857))
* Unity startup recovery avoids premature readiness timeouts ([#1296](https://github.com/hatayama/unity-cli-loop/issues/1296)) ([1d3d2b6](https://github.com/hatayama/unity-cli-loop/commit/1d3d2b6780f239acdede657e0dd153911ffaf669))

## [3.0.0-beta.31](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.30...v3.0.0-beta.31) (2026-06-02)


### Bug Fixes

* Compile waits longer before reporting indeterminate results ([#1276](https://github.com/hatayama/unity-cli-loop/issues/1276)) ([100e9aa](https://github.com/hatayama/unity-cli-loop/commit/100e9aa5aa06b049536545bcd5dd0a6839b27d95))
* No-test runs now explain likely test assembly setup issues ([#1274](https://github.com/hatayama/unity-cli-loop/issues/1274)) ([d64d9bb](https://github.com/hatayama/unity-cli-loop/commit/d64d9bb3aa135be5f6785099d4cefb46bc1f4a44))

## [3.0.0-beta.30](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.29...v3.0.0-beta.30) (2026-06-02)


### Bug Fixes

* Compile handles externally changed open Scenes without blocking ([#1261](https://github.com/hatayama/unity-cli-loop/issues/1261)) ([8d6ed1b](https://github.com/hatayama/unity-cli-loop/commit/8d6ed1b7107d9cada658e2f7bb7bbd71122840b7))

## [3.0.0-beta.29](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.28...v3.0.0-beta.29) (2026-06-01)


### Bug Fixes

* Compile reports assembly definition errors instead of unknown status ([#1260](https://github.com/hatayama/unity-cli-loop/issues/1260)) ([e093b0b](https://github.com/hatayama/unity-cli-loop/commit/e093b0bb070fe66c413e880b278ba9b00c31daa2))
* Setup updates outdated CLI before offering PATH repair ([#1258](https://github.com/hatayama/unity-cli-loop/issues/1258)) ([2254286](https://github.com/hatayama/unity-cli-loop/commit/2254286ead5bb90a77893ff1b071d9b536534dac))

## [3.0.0-beta.28](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.27...v3.0.0-beta.28) (2026-06-01)


### Bug Fixes

* Interrupted PlayMode tests no longer leave domain reload disabled ([#1254](https://github.com/hatayama/unity-cli-loop/issues/1254)) ([600f96d](https://github.com/hatayama/unity-cli-loop/commit/600f96d709998be4c8124502a23b04d23e921e01))

## [3.0.0-beta.27](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.26...v3.0.0-beta.27) (2026-05-31)


### Bug Fixes

* Unity package no longer includes development CLI binaries ([#1250](https://github.com/hatayama/unity-cli-loop/issues/1250)) ([93b0176](https://github.com/hatayama/unity-cli-loop/commit/93b0176ac2dfd16d697ca426a9f56f7e97031e6d))

## [3.0.0-beta.26](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.25...v3.0.0-beta.26) (2026-05-31)


### Bug Fixes

* Compile commands now finish reliably after Unity reloads scripts ([#1248](https://github.com/hatayama/unity-cli-loop/issues/1248)) ([f593f46](https://github.com/hatayama/unity-cli-loop/commit/f593f463a7519eba992e525290ec3de5cc4fd276))

## [3.0.0-beta.25](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.24...v3.0.0-beta.25) (2026-05-30)


### Bug Fixes

* Setup now requires the CLI needed for reliable compile waits ([#1246](https://github.com/hatayama/unity-cli-loop/issues/1246)) ([07c8247](https://github.com/hatayama/unity-cli-loop/commit/07c8247b0801e453eac23c8611d3f572bd675f80))

## [3.0.0-beta.24](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.23...v3.0.0-beta.24) (2026-05-30)


### Bug Fixes

* Compile commands no longer hang across Unity reloads ([#1240](https://github.com/hatayama/unity-cli-loop/issues/1240)) ([12238ce](https://github.com/hatayama/unity-cli-loop/commit/12238ce492e364e3a1999364027cded03ab96262))

## [3.0.0-beta.23](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.22...v3.0.0-beta.23) (2026-05-28)


### Bug Fixes

* Compile no longer leaves Unity busy after finishing ([#1237](https://github.com/hatayama/unity-cli-loop/issues/1237)) ([f705b4b](https://github.com/hatayama/unity-cli-loop/commit/f705b4b7a9cc48ded3625bae91da6a39d9095462))
* Dynamic code snippets can use Unity Object without ambiguity ([#1234](https://github.com/hatayama/unity-cli-loop/issues/1234)) ([0428197](https://github.com/hatayama/unity-cli-loop/commit/0428197e7c957ab4915646471b3142b1394e2373))
* Simulated key presses now reach gameplay polling ([#1236](https://github.com/hatayama/unity-cli-loop/issues/1236)) ([ba735f1](https://github.com/hatayama/unity-cli-loop/commit/ba735f158a2be60829ad9e349a536a954637439a))

## [3.0.0-beta.22](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.21...v3.0.0-beta.22) (2026-05-28)


### Bug Fixes

* Compile fails fast on asmdef and asmref import errors ([#1225](https://github.com/hatayama/unity-cli-loop/issues/1225)) ([a90ca87](https://github.com/hatayama/unity-cli-loop/commit/a90ca875382228914468919d717454eeea25b9e7))
* Player builds stay free of editor-only uLoop tooling ([#1229](https://github.com/hatayama/unity-cli-loop/issues/1229)) ([ff6fb8d](https://github.com/hatayama/unity-cli-loop/commit/ff6fb8ddc979a77f5a9afbd759dbe13203a42ccd))
* Settings makes installed CLI uninstall action appear inactive ([#1228](https://github.com/hatayama/unity-cli-loop/issues/1228)) ([99af542](https://github.com/hatayama/unity-cli-loop/commit/99af5428048e83d7722e28acd7c226352742be1c))
* Unity CLI Loop assemblies no longer expose legacy uLoopMCP names ([#1230](https://github.com/hatayama/unity-cli-loop/issues/1230)) ([cb75c0c](https://github.com/hatayama/unity-cli-loop/commit/cb75c0c48c71adfe8372ad064486bd2d5a2ea7a0))

## [3.0.0-beta.21](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.20...v3.0.0-beta.21) (2026-05-27)


### Bug Fixes

* Play mode control waits reliably and input overlays avoid missing script warnings ([#1219](https://github.com/hatayama/unity-cli-loop/issues/1219)) ([2ad9f59](https://github.com/hatayama/unity-cli-loop/commit/2ad9f596f7d18b19c0dd012a9442e1d88c100a56))
* Setup now requires the Play Mode wait CLI release ([#1220](https://github.com/hatayama/unity-cli-loop/issues/1220)) ([73db4b4](https://github.com/hatayama/unity-cli-loop/commit/73db4b444f4df8f39757fa0532d303afb7d60da6))
* Unity busy states are easier to diagnose ([#1215](https://github.com/hatayama/unity-cli-loop/issues/1215)) ([fb4713d](https://github.com/hatayama/unity-cli-loop/commit/fb4713d5567110536bb37b157b4abfb25a95994c))

## [3.0.0-beta.20](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.19...v3.0.0-beta.20) (2026-05-26)


### Features

* Tests now save editor changes before running ([#1212](https://github.com/hatayama/unity-cli-loop/issues/1212)) ([ded7d74](https://github.com/hatayama/unity-cli-loop/commit/ded7d7411905f3edeb0c286567c8d7d03ae57aa5))


### Bug Fixes

* Input simulation no longer stalls when Run In Background is disabled ([#1214](https://github.com/hatayama/unity-cli-loop/issues/1214)) ([7023ac4](https://github.com/hatayama/unity-cli-loop/commit/7023ac4aa9309ac342d50c84e449b24e6e59b0d1))

## [3.0.0-beta.19](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.18...v3.0.0-beta.19) (2026-05-25)


### Bug Fixes

* Input simulation commands no longer hang while showing overlays ([#1208](https://github.com/hatayama/unity-cli-loop/issues/1208)) ([b21a47c](https://github.com/hatayama/unity-cli-loop/commit/b21a47c021074e63f7db472e5666c23bc743ffe1))
* Make skill instructions use CLI flag syntax ([#1210](https://github.com/hatayama/unity-cli-loop/issues/1210)) ([a11923d](https://github.com/hatayama/unity-cli-loop/commit/a11923dc61cdced2bbe57656058084c726920d8a))

## [3.0.0-beta.18](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.17...v3.0.0-beta.18) (2026-05-25)


### Bug Fixes

* Simplify compile skill guidance ([#1206](https://github.com/hatayama/unity-cli-loop/issues/1206)) ([71b080e](https://github.com/hatayama/unity-cli-loop/commit/71b080ef58558ba190fd0ac249529f3f7e0f6dc1))

## [3.0.0-beta.17](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.16...v3.0.0-beta.17) (2026-05-25)


### Bug Fixes

* Input overlays load without auto-referencing runtime assemblies ([#1195](https://github.com/hatayama/unity-cli-loop/issues/1195)) ([45e5daf](https://github.com/hatayama/unity-cli-loop/commit/45e5daf547719ccec322b4b0efeb3f692b78602f))
* Internal assemblies no longer leak into project scripts ([#1197](https://github.com/hatayama/unity-cli-loop/issues/1197)) ([546bf6d](https://github.com/hatayama/unity-cli-loop/commit/546bf6d54dd32ac513d4857a163971b8adc1ac39))
* Keep Run Tests available without extra Editor startup hooks ([#1201](https://github.com/hatayama/unity-cli-loop/issues/1201)) ([b8d89c3](https://github.com/hatayama/unity-cli-loop/commit/b8d89c32605e9f125bc92d598eac9315707bd95e))
* run-tests is available in package consumer projects ([#1192](https://github.com/hatayama/unity-cli-loop/issues/1192)) ([311e453](https://github.com/hatayama/unity-cli-loop/commit/311e4531cdd45babf7b374497717a54f30176f75))
* Unity commands recover without stale readiness cleanup ([#1199](https://github.com/hatayama/unity-cli-loop/issues/1199)) ([a4b3f06](https://github.com/hatayama/unity-cli-loop/commit/a4b3f06ad14dd88d465f5ab2fe3b2705a0b4ac4e))

## [3.0.0-beta.16](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.15...v3.0.0-beta.16) (2026-05-23)


### Bug Fixes

* CLI updates use installer scripts from the selected release ([#1190](https://github.com/hatayama/unity-cli-loop/issues/1190)) ([761a8c6](https://github.com/hatayama/unity-cli-loop/commit/761a8c6354f8c8fa497ac046a02a76b2b5b0cb47))
* Windows CLI updates no longer fail when upgrading from older versions ([#1189](https://github.com/hatayama/unity-cli-loop/issues/1189)) ([79f60fd](https://github.com/hatayama/unity-cli-loop/commit/79f60fdf4d4a163bb26fb3d7647de15b9fa13fed))

## [3.0.0-beta.15](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.14...v3.0.0-beta.15) (2026-05-23)


### Features

* Improve Windows CLI installs and release provenance ([#1186](https://github.com/hatayama/unity-cli-loop/issues/1186)) ([3dead33](https://github.com/hatayama/unity-cli-loop/commit/3dead3341dc2286031e3287d1ef47da7bfd6ce9c))
* Install uloop natively on macOS ([#1187](https://github.com/hatayama/unity-cli-loop/issues/1187)) ([1c12b49](https://github.com/hatayama/unity-cli-loop/commit/1c12b4991d1da53701ae97f1c1ed6a2fcb032c96))


### Bug Fixes

* Startup no longer freezes during migration checks ([#1181](https://github.com/hatayama/unity-cli-loop/issues/1181)) ([78b5b22](https://github.com/hatayama/unity-cli-loop/commit/78b5b22a2fe7ee00944e2d3b37a0bcd9e31284ae))
* Unity CLI install repairs missing terminal command setup ([#1176](https://github.com/hatayama/unity-cli-loop/issues/1176)) ([c633a8e](https://github.com/hatayama/unity-cli-loop/commit/c633a8e5934bc68aa31f3989aacd9edd735b5c16))
* Unity commands recover reliably after editor reloads ([#1182](https://github.com/hatayama/unity-cli-loop/issues/1182)) ([7c035ec](https://github.com/hatayama/unity-cli-loop/commit/7c035eccb7beb4a173dc75378c588c3a4e5dcb02))

## [3.0.0-beta.14](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.13...v3.0.0-beta.14) (2026-05-19)


### Bug Fixes

* Busy responses no longer create Unity Console errors ([#1168](https://github.com/hatayama/unity-cli-loop/issues/1168)) ([be7aa80](https://github.com/hatayama/unity-cli-loop/commit/be7aa80d0048374b53170c5df34c54600425cb12))
* Prevent stale server recovery after Editor restarts ([#1166](https://github.com/hatayama/unity-cli-loop/issues/1166)) ([5b7835d](https://github.com/hatayama/unity-cli-loop/commit/5b7835d43e72d53cfd7997c97a4f9cf15be3f2e4))
* Unity tool requests no longer overlap during long-running work ([#1164](https://github.com/hatayama/unity-cli-loop/issues/1164)) ([c5a583b](https://github.com/hatayama/unity-cli-loop/commit/c5a583ba457e2c330b8c5150cc101e81b790fb45))
* Windows CLI uninstall no longer leaves stale commands behind ([#1169](https://github.com/hatayama/unity-cli-loop/issues/1169)) ([62a7c88](https://github.com/hatayama/unity-cli-loop/commit/62a7c887af62e31c02d7d0fefdd204f37f8f070b))

## [3.0.0-beta.13](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.12...v3.0.0-beta.13) (2026-05-17)


### Bug Fixes

* Settings no longer shows the CLI as installed after uninstall ([#1154](https://github.com/hatayama/unity-cli-loop/issues/1154)) ([090f0a3](https://github.com/hatayama/unity-cli-loop/commit/090f0a3a180a5d3e7c74dd7b6dbc1b7aab884835))

## [3.0.0-beta.12](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.11...v3.0.0-beta.12) (2026-05-17)


### Bug Fixes

* Windows CLI install no longer fails during binary verification ([#1152](https://github.com/hatayama/unity-cli-loop/issues/1152)) ([04abc42](https://github.com/hatayama/unity-cli-loop/commit/04abc42b69360111907454ac43d38f1836c8bc3d))

## [3.0.0-beta.11](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.10...v3.0.0-beta.11) (2026-05-17)


### Bug Fixes

* Improve Unity reload recovery and skill setup reliability ([#1150](https://github.com/hatayama/unity-cli-loop/issues/1150)) ([4556535](https://github.com/hatayama/unity-cli-loop/commit/4556535e69a15a0e8dc117131d860e5a597a84bd))
* Make Unity tool skill descriptions more concise ([#1148](https://github.com/hatayama/unity-cli-loop/issues/1148)) ([c09a5af](https://github.com/hatayama/unity-cli-loop/commit/c09a5afa01deccea694413bd640787c42f40e54d))

## [3.0.0-beta.10](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.9...v3.0.0-beta.10) (2026-05-17)


### Features

* Code execution waits for reload recovery by default ([#1142](https://github.com/hatayama/unity-cli-loop/issues/1142)) ([15d3ad0](https://github.com/hatayama/unity-cli-loop/commit/15d3ad0b2048e95d2fee876a21ba4fac54444d4e))


### Bug Fixes

* CLI commands recover reliably after Unity reloads ([#1136](https://github.com/hatayama/unity-cli-loop/issues/1136)) ([7e45f1e](https://github.com/hatayama/unity-cli-loop/commit/7e45f1e7ba7f9c96d6503faaf3153ddbfd33b9fd))
* CLI recovery stays reliable during Unity readiness updates ([#1139](https://github.com/hatayama/unity-cli-loop/issues/1139)) ([6dbe57b](https://github.com/hatayama/unity-cli-loop/commit/6dbe57ba3397c5e63f7aab90520ebcac8210b74a))
* Make CLI help consistent for native and Unity commands ([#1146](https://github.com/hatayama/unity-cli-loop/issues/1146)) ([802afa3](https://github.com/hatayama/unity-cli-loop/commit/802afa3e23ea405c3cf4ff944e14afaae82bb55e))
* Unity busy detection no longer relies on obsolete lock files ([#1144](https://github.com/hatayama/unity-cli-loop/issues/1144)) ([ba5746f](https://github.com/hatayama/unity-cli-loop/commit/ba5746f1fbfb602ed10dc99f108e4bc761491ceb))

## [3.0.0-beta.9](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.8...v3.0.0-beta.9) (2026-05-16)


### Features

* uloop can uninstall its global command from terminal and Settings ([#1135](https://github.com/hatayama/unity-cli-loop/issues/1135)) ([4122d57](https://github.com/hatayama/unity-cli-loop/commit/4122d57eb79cbe491c633063b99e22484816d355))


### Bug Fixes

* Setup can update the CLI on older Windows PowerShell ([#1133](https://github.com/hatayama/unity-cli-loop/issues/1133)) ([6601ed3](https://github.com/hatayama/unity-cli-loop/commit/6601ed3af6cb89d18ff7dfee25148b4ad351ea21))

## [3.0.0-beta.8](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.7...v3.0.0-beta.8) (2026-05-15)


### Features

* Help older custom tools migrate to V3 automatically ([#1125](https://github.com/hatayama/unity-cli-loop/issues/1125)) ([8412aa4](https://github.com/hatayama/unity-cli-loop/commit/8412aa449040d0ee8d05fb50b55b944e0cf31570))


### Bug Fixes

* Release PRs resume after branch-scoped releases are published ([#1127](https://github.com/hatayama/unity-cli-loop/issues/1127)) ([141e888](https://github.com/hatayama/unity-cli-loop/commit/141e888c9f89dc6cbb22cab48274527bf1f53897))
* Setup recognizes existing npm CLI installs ([#1131](https://github.com/hatayama/unity-cli-loop/issues/1131)) ([7fbb0d8](https://github.com/hatayama/unity-cli-loop/commit/7fbb0d8ea34b7fe1c85de73240cbc0ca1129fe18))
* Uninstall CLI no longer reinstalls the global command ([#1126](https://github.com/hatayama/unity-cli-loop/issues/1126)) ([2adffbc](https://github.com/hatayama/unity-cli-loop/commit/2adffbc1f97d62b02323b7926e1e1f3e82cd3ec0))

## [3.0.0-beta.7](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.6...v3.0.0-beta.7) (2026-05-12)


### Bug Fixes

* Release PRs keep changelog baselines in sync ([#1104](https://github.com/hatayama/unity-cli-loop/issues/1104)) ([3d0426e](https://github.com/hatayama/unity-cli-loop/commit/3d0426eb560087a702ed756f092897587976e5f1))
* Setup now requires the single-binary CLI before running tools ([#1101](https://github.com/hatayama/unity-cli-loop/issues/1101)) ([98c3cda](https://github.com/hatayama/unity-cli-loop/commit/98c3cda7caeb76635bb14dfd11cf177aa41fbd06))

## [3.0.0-beta.6](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.5...v3.0.0-beta.6) (2026-05-11)


### Features

* Native CLI is distributed as a single uloop binary ([#1100](https://github.com/hatayama/unity-cli-loop/issues/1100)) ([1180fae](https://github.com/hatayama/unity-cli-loop/commit/1180fae9be33c3f1cc6e35044b2ee42130052e93))
* Simplify native CLI packaging and updates ([#1099](https://github.com/hatayama/unity-cli-loop/issues/1099)) ([35ef3c0](https://github.com/hatayama/unity-cli-loop/commit/35ef3c0c61b4bb8d00d2dab8ab8468fa3b5bdab6))


### Bug Fixes

* CLI and Skills reload buttons refresh independently ([#1091](https://github.com/hatayama/unity-cli-loop/issues/1091)) ([95f7c2d](https://github.com/hatayama/unity-cli-loop/commit/95f7c2d941a044f2177f2bde057d650da45547cb))

## [3.0.0-beta.5](https://github.com/hatayama/unity-cli-loop/compare/v3.0.0-beta.4...v3.0.0-beta.5) (2026-05-10)


### Bug Fixes

* Release PRs no longer include old changelog entries after publishing ([#1088](https://github.com/hatayama/unity-cli-loop/issues/1088)) ([1a0922b](https://github.com/hatayama/unity-cli-loop/commit/1a0922bea7b28a35833f2e3330a9522a2c4fc50d))
* Repository-level fixes now create beta release PRs ([#1089](https://github.com/hatayama/unity-cli-loop/issues/1089)) ([ca4888a](https://github.com/hatayama/unity-cli-loop/commit/ca4888ae8409fbea4aee22c00d91e05166fb5d26))

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
