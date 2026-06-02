using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Adds asmdef configuration hints when Unity Test Runner reports that no tests were discovered.
    /// </summary>
    internal sealed class RunTestsNoTestsDiagnosticService
    {
        private const string HintPrefix = " Possible asmdef issues:";
        private const string TestAssembliesReference = "TestAssemblies";
        private const string UnityEngineTestRunnerReference = "UnityEngine.TestRunner";
        private const string UnityEditorTestRunnerReference = "UnityEditor.TestRunner";
        private const string UnityEngineTestRunnerGuidReference = "GUID:27619889b8ba8c24980f49ee34dbb44a";
        private const string UnityEditorTestRunnerGuidReference = "GUID:0acc523941302664db1f4e527237feb3";
        private const int MaxFindings = 4;

        public string AppendDiagnosticsIfNeeded(
            string message,
            bool success,
            int testCount,
            UnityCliLoopTestMode testMode,
            TestFilterType filterType)
        {
            if (!ShouldAppendDiagnostics(message, success, testCount, filterType))
            {
                return message;
            }

            RunTestsAsmdefInfo[] asmdefs = LoadProjectAsmdefs();
            RunTestsAsmdefDiagnosticFinding[] findings = Analyze(asmdefs, testMode);
            return AppendFindingsIfEligible(message, success, testCount, findings);
        }

        internal static string AppendDiagnosticsOrOriginalMessage(string message, Func<string> appendDiagnostics)
        {
            Debug.Assert(appendDiagnostics != null, "appendDiagnostics must not be null");

            try
            {
                return appendDiagnostics();
            }
            catch (Exception exception) when (IsRecoverableAsmdefInspectionException(exception))
            {
                Debug.LogWarning($"Failed to build no-tests diagnostics: {exception}");
                return message;
            }
        }

        private static bool IsRecoverableAsmdefInspectionException(Exception exception)
        {
            Debug.Assert(exception != null, "exception must not be null");

            return exception is IOException ||
                   exception is UnauthorizedAccessException;
        }

        internal static string AppendFindingsIfEligible(
            string message,
            bool success,
            int testCount,
            IReadOnlyList<RunTestsAsmdefDiagnosticFinding> findings)
        {
            Debug.Assert(findings != null, "findings must not be null");

            if (!ShouldInspect(message, success, testCount) || findings.Count == 0)
            {
                return message;
            }

            string[] findingMessages = findings
                .Take(MaxFindings)
                .Select(finding => $"{finding.AssetPath}: {finding.Message}")
                .ToArray();
            string suffix = string.Join(" ", findingMessages);
            if (findings.Count > MaxFindings)
            {
                suffix += $" Additional asmdef issues omitted: {findings.Count - MaxFindings}.";
            }

            return message + HintPrefix + " " + suffix;
        }

        internal static RunTestsAsmdefDiagnosticFinding[] Analyze(
            IReadOnlyList<RunTestsAsmdefInfo> asmdefs,
            UnityCliLoopTestMode testMode)
        {
            Debug.Assert(asmdefs != null, "asmdefs must not be null");

            RunTestsAsmdefInfo[] allTestAsmdefs = asmdefs
                .Where(IsTestAsmdefCandidate)
                .ToArray();
            RunTestsAsmdefInfo[] testAsmdefs = allTestAsmdefs
                .Where(asmdef => IsRelevantToTestMode(asmdef, testMode))
                .ToArray();
            RunTestsAsmdefInfo[] runtimeAsmdefs = asmdefs
                .Where(asmdef => IsRuntimeAsmdef(asmdef, allTestAsmdefs))
                .ToArray();
            List<RunTestsAsmdefDiagnosticFinding> findings = new();

            foreach (RunTestsAsmdefInfo testAsmdef in testAsmdefs)
            {
                bool hasTestAssemblyMarker = HasTestAssemblyMarker(testAsmdef);
                bool hasAnyDirectTestRunnerReference = HasAnyDirectTestRunnerReference(testAsmdef);

                if (!hasTestAssemblyMarker && !hasAnyDirectTestRunnerReference)
                {
                    findings.Add(new RunTestsAsmdefDiagnosticFinding(
                        testAsmdef.AssetPath,
                        "looks like a test asmdef but is not marked as a test assembly. Add optionalUnityReferences: [\"TestAssemblies\"] or enable testAssemblies."));
                }

                if (hasTestAssemblyMarker && hasAnyDirectTestRunnerReference)
                {
                    findings.Add(new RunTestsAsmdefDiagnosticFinding(
                        testAsmdef.AssetPath,
                        "test assembly marker is combined with direct UnityEngine.TestRunner or UnityEditor.TestRunner references. Remove direct TestRunner references to avoid duplicate references."));
                }

                if (testMode == UnityCliLoopTestMode.EditMode && LooksLikeEditModeAsmdef(testAsmdef) && !IncludesEditorPlatform(testAsmdef))
                {
                    findings.Add(new RunTestsAsmdefDiagnosticFinding(
                        testAsmdef.AssetPath,
                        "looks like an EditMode test asmdef but does not target Editor. Add includePlatforms: [\"Editor\"]."));
                }

                if (testMode == UnityCliLoopTestMode.PlayMode && LooksLikePlayModeAsmdef(testAsmdef) && IncludesEditorPlatform(testAsmdef))
                {
                    findings.Add(new RunTestsAsmdefDiagnosticFinding(
                        testAsmdef.AssetPath,
                        "looks like a PlayMode test asmdef but targets Editor only. Remove Editor-only includePlatforms for PlayMode tests."));
                }

                if (runtimeAsmdefs.Length > 0 && !ReferencesAnyRuntimeAsmdef(testAsmdef, runtimeAsmdefs))
                {
                    findings.Add(new RunTestsAsmdefDiagnosticFinding(
                        testAsmdef.AssetPath,
                        "does not reference a runtime asmdef. If these tests target runtime code, reference the runtime asmdef under test."));
                }
            }

            return findings.ToArray();
        }

        internal static bool ShouldAppendDiagnostics(
            string message,
            bool success,
            int testCount,
            TestFilterType filterType)
        {
            return filterType == TestFilterType.all && ShouldInspect(message, success, testCount);
        }

        internal static bool ShouldInspect(string message, bool success, int testCount)
        {
            return !success
                   && testCount == 0
                   && string.Equals(message, RunTestsResponse.NoTestsFoundMessage, StringComparison.Ordinal);
        }

        private static RunTestsAsmdefInfo[] LoadProjectAsmdefs()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            List<RunTestsAsmdefInfo> asmdefs = new();

            foreach (string guid in asmdefGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsInspectableProjectAsmdef(assetPath))
                {
                    continue;
                }

                string absolutePath = Path.Combine(projectRoot, assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                AsmdefJson json = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(absolutePath));
                if (json == null)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(json.name)
                    ? Path.GetFileNameWithoutExtension(assetPath)
                    : json.name;
                asmdefs.Add(new RunTestsAsmdefInfo(
                    assetPath,
                    guid,
                    name,
                    json.references ?? Array.Empty<string>(),
                    json.optionalUnityReferences ?? Array.Empty<string>(),
                    json.includePlatforms ?? Array.Empty<string>(),
                    json.testAssemblies));
            }

            return asmdefs
                .OrderBy(asmdef => asmdef.AssetPath, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsInspectableProjectAsmdef(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath)
                   && assetPath.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static bool IsTestAsmdefCandidate(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return HasTestAssemblyMarker(asmdef)
                   || HasAnyDirectTestRunnerReference(asmdef)
                   || LooksLikeNamedTestAsmdef(asmdef);
        }

        private static bool IsRelevantToTestMode(RunTestsAsmdefInfo asmdef, UnityCliLoopTestMode testMode)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            bool looksLikeEditModeAsmdef = LooksLikeEditModeNameOrPath(asmdef);
            bool looksLikePlayModeAsmdef = LooksLikePlayModeAsmdef(asmdef);
            if (testMode == UnityCliLoopTestMode.EditMode)
            {
                return looksLikeEditModeAsmdef
                       || (!looksLikePlayModeAsmdef && IncludesEditorPlatform(asmdef));
            }

            return looksLikePlayModeAsmdef
                   || (!looksLikeEditModeAsmdef && !IncludesEditorPlatform(asmdef));
        }

        private static bool LooksLikeNamedTestAsmdef(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            string fileName = Path.GetFileNameWithoutExtension(asmdef.AssetPath);
            return ContainsModeTestName(asmdef.Name)
                   || ContainsModeTestName(fileName)
                   || IsUnderCommonTestFolder(asmdef.AssetPath);
        }

        private static bool ContainsModeTestName(string value)
        {
            Debug.Assert(value != null, "value must not be null");

            return value.Contains("EditMode.Tests", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("PlayMode.Tests", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith(".Tests.Editor", StringComparison.OrdinalIgnoreCase)
                   || ContainsNestedTestsModeName(value)
                   || value.EndsWith(".Tests.PlayMode", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsNestedTestsModeName(string value)
        {
            Debug.Assert(value != null, "value must not be null");

            return value.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
                   && (value.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase)
                       || value.EndsWith(".EditMode", StringComparison.OrdinalIgnoreCase)
                       || value.EndsWith(".PlayMode", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUnderCommonTestFolder(string assetPath)
        {
            Debug.Assert(assetPath != null, "assetPath must not be null");

            return assetPath.StartsWith("Assets/Tests/Editor/", StringComparison.Ordinal)
                   || assetPath.StartsWith("Assets/Tests/EditMode/", StringComparison.Ordinal)
                   || assetPath.StartsWith("Assets/Tests/PlayMode/", StringComparison.Ordinal);
        }

        private static bool IsRuntimeAsmdef(RunTestsAsmdefInfo asmdef, IReadOnlyList<RunTestsAsmdefInfo> testAsmdefs)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");
            Debug.Assert(testAsmdefs != null, "testAsmdefs must not be null");

            return !testAsmdefs.Contains(asmdef)
                   && !LooksLikeEditModeAsmdef(asmdef)
                   && !asmdef.Name.Contains("Test", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTestAssemblyMarker(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return asmdef.TestAssemblies
                   || asmdef.OptionalUnityReferences.Contains(TestAssembliesReference);
        }

        private static bool HasNamedDirectTestRunnerReference(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return asmdef.References.Contains(UnityEngineTestRunnerReference)
                   || asmdef.References.Contains(UnityEditorTestRunnerReference);
        }

        private static bool HasAnyDirectTestRunnerReference(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return HasNamedDirectTestRunnerReference(asmdef)
                   || asmdef.References.Contains(UnityEngineTestRunnerGuidReference)
                   || asmdef.References.Contains(UnityEditorTestRunnerGuidReference);
        }

        private static bool IncludesEditorPlatform(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return asmdef.IncludePlatforms.Contains("Editor");
        }

        private static bool LooksLikeEditModeAsmdef(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return LooksLikeEditModeNameOrPath(asmdef) || IncludesEditorPlatform(asmdef);
        }

        private static bool LooksLikeEditModeNameOrPath(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return asmdef.AssetPath.Contains("/Editor/", StringComparison.Ordinal)
                   || asmdef.AssetPath.Contains("/EditMode/", StringComparison.Ordinal)
                   || asmdef.Name.EndsWith(".Editor", StringComparison.Ordinal)
                   || asmdef.Name.Contains("EditMode", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePlayModeAsmdef(RunTestsAsmdefInfo asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            return asmdef.AssetPath.Contains("/PlayMode/", StringComparison.Ordinal)
                   || asmdef.Name.EndsWith(".PlayMode", StringComparison.Ordinal)
                   || asmdef.Name.Contains("PlayMode", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReferencesAnyRuntimeAsmdef(
            RunTestsAsmdefInfo testAsmdef,
            IReadOnlyList<RunTestsAsmdefInfo> runtimeAsmdefs)
        {
            Debug.Assert(testAsmdef != null, "testAsmdef must not be null");
            Debug.Assert(runtimeAsmdefs != null, "runtimeAsmdefs must not be null");

            foreach (RunTestsAsmdefInfo runtimeAsmdef in runtimeAsmdefs)
            {
                if (testAsmdef.References.Contains(runtimeAsmdef.Name) ||
                    testAsmdef.References.Contains("GUID:" + runtimeAsmdef.Guid))
                {
                    return true;
                }
            }

            return false;
        }

        [Serializable]
        private sealed class AsmdefJson
        {
            public string name = string.Empty;
            public string[] references = Array.Empty<string>();
            public string[] optionalUnityReferences = Array.Empty<string>();
            public string[] includePlatforms = Array.Empty<string>();
            public bool testAssemblies = false;
        }
    }

    /// <summary>
    /// Carries the asmdef fields needed for run-tests no-discovery diagnostics.
    /// </summary>
    internal sealed class RunTestsAsmdefInfo
    {
        public RunTestsAsmdefInfo(
            string assetPath,
            string guid,
            string name,
            string[] references,
            string[] optionalUnityReferences,
            string[] includePlatforms,
            bool testAssemblies)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be null or empty");
            Debug.Assert(guid != null, "guid must not be null");
            Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(optionalUnityReferences != null, "optionalUnityReferences must not be null");
            Debug.Assert(includePlatforms != null, "includePlatforms must not be null");

            AssetPath = assetPath;
            Guid = guid;
            Name = name;
            References = references;
            OptionalUnityReferences = optionalUnityReferences;
            IncludePlatforms = includePlatforms;
            TestAssemblies = testAssemblies;
        }

        public string AssetPath { get; }
        public string Guid { get; }
        public string Name { get; }
        public string[] References { get; }
        public string[] OptionalUnityReferences { get; }
        public string[] IncludePlatforms { get; }
        public bool TestAssemblies { get; }
    }

    /// <summary>
    /// Carries one asmdef hint that can be appended to a no-tests run-tests response.
    /// </summary>
    internal sealed class RunTestsAsmdefDiagnosticFinding
    {
        public RunTestsAsmdefDiagnosticFinding(string assetPath, string message)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(message), "message must not be null or empty");

            AssetPath = assetPath;
            Message = message;
        }

        public string AssetPath { get; }
        public string Message { get; }
    }
}
