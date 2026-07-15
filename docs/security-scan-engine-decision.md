# C# Security Scan Engine Decision

Date: 2026-07-15

## Decision

The repository uses GitHub CodeQL for C# security analysis with `build-mode: none` and the `security-extended` query suite. The workflow pins `github/codeql-action` to the full commit SHA for v4.37.0 and selects its linked CodeQL 2.26.0 toolchain. A repository-owned Go guard validates the scanner identity, tool version, SARIF structure, successful no-build completion, extracted-source evidence, and database-quality floor before upload.

The approved local proof analyzed all 975 C# files under `Packages/src` and `Assets/Tests`, generated SARIF 2.1.0, and reported call-target resolution of 68% and known-type resolution of 82%. A second proof added a temporary command-injection source outside the repository checkout. CodeQL 2.26.0 with `codeql/csharp-queries@1.7.5` and `security-extended` reported `cs/command-line-injection`. Only the resulting minimal SARIF evidence is retained under release-automation test data; the vulnerable source is not part of the repository.

The probe is recorded rather than rerun as a separate CodeQL job on every pull request because a second database extraction and 70-query analysis would approximately double C# scan time. The production workflow fixes both the action commit and reported CodeQL semantic version, while tests reject scanner-version drift and require the recorded probe finding to remain non-empty.

## Alternatives

### SecurityCodeScan.VS2019

Rejected. Its latest repository release is 5.6.7 from September 5, 2022, which does not satisfy the 2026 maintenance requirement. The previous workflow also converted build failure into a zero-result placeholder SARIF, so it could not distinguish scanner failure from a clean scan.

Primary source: <https://github.com/security-code-scan/security-code-scan>

### Semgrep OSS

Not selected. Semgrep produces SARIF and its current language documentation lists C# support, but the OSS CLI identifies advanced C# analysis as a Pro-language capability. The local OSS proof scanned the repository and produced SARIF, but its C# security coverage was materially narrower than the selected CodeQL security suite.

Primary sources: <https://docs.semgrep.dev/supported-languages/> and <https://docs.semgrep.dev/cli-reference/>

### Microsoft/Roslyn analyzers

Rejected for this workflow. .NET analyzers run as part of a C# project build. The repository proof against the Unity-generated project failed with `NETSDK1004` because the required Unity-generated restore assets were unavailable, which correctly demonstrates that an independent analyzer job cannot treat build failure as zero findings.

Primary sources: <https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview> and <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings>

## Future accuracy improvement

CodeQL no-build analysis cannot resolve every Unity API reference, which lowers interprocedural accuracy and can cause false negatives. A future, separately reviewed change may generate Unity project files in CI and evaluate CodeQL `autobuild` or `manual` mode with resolved Unity assemblies. That work is intentionally outside PR-3 because it adds Unity startup, dependency, and CI-time complexity; it must preserve the fail-closed SARIF and database-quality guards established here.

Expanding the scan corpus to repository-local C# outside the shipped Unity package and its tests, including `Assets/Editor`, `Assets/Scenes`, `tools`, and `tests`, is also deferred to a separately reviewed change. Those sources were not part of the approved 975-file proof, so expansion requires remeasuring and reapproving the 68%/82% quality baseline and the 75,000-line extraction floor instead of silently applying package-derived thresholds to a different corpus.

GitHub documents the supported C# build modes and their trade-offs at <https://docs.github.com/en/code-security/concepts/code-scanning/codeql/codeql-for-compiled-languages>.
