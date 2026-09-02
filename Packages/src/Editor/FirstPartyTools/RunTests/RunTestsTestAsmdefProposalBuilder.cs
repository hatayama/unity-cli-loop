using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Synthesizes a concrete test .asmdef (name, path, test-assembly wiring, references to the
    /// assemblies under test) for a project that has no test assembly for the requested TestMode.
    /// </summary>
    internal static class RunTestsTestAsmdefProposalBuilder
    {
        private const string FallbackBaseName = "Project";
        private const string EditorSuffix = ".Editor";
        private const string UnityEngineTestRunnerReference = "UnityEngine.TestRunner";
        private const string UnityEditorTestRunnerReference = "UnityEditor.TestRunner";

        /// <summary>
        /// Loads the project's asmdefs and product name, then builds the proposal. Null when a
        /// test assembly for the mode already exists.
        /// </summary>
        internal static RunTestsTestAsmdefProposal Propose(UnityCliLoopTestMode testMode)
        {
            Debug.Assert(
                MainThreadSwitcher.IsMainThread,
                "Test asmdef proposals must run on the main thread because AssetDatabase and PlayerSettings are Unity APIs.");

            RunTestsAsmdefInfo[] asmdefs = RunTestsNoTestsDiagnosticService.LoadProjectAsmdefs();
            return Build(asmdefs, testMode, PlayerSettings.productName);
        }

        internal static RunTestsTestAsmdefProposal Build(
            IReadOnlyList<RunTestsAsmdefInfo> asmdefs,
            UnityCliLoopTestMode testMode,
            string productName)
        {
            Debug.Assert(asmdefs != null, "asmdefs must not be null");
            Debug.Assert(productName != null, "productName must not be null");

            bool editMode = testMode == UnityCliLoopTestMode.EditMode;
            // Why the Editor platform decides relevance: EditMode tests are discovered only from
            // Editor-only assemblies and PlayMode tests only from the others, so a project with a
            // PlayMode test assembly still has nowhere to put an EditMode test.
            bool hasTestAssemblyForMode = asmdefs.Any(asmdef =>
                asmdef.IsTestAssembly() && asmdef.IsEditorOnly() == editMode);
            if (hasTestAssemblyForMode)
            {
                return null;
            }

            // Why editor-only assemblies are dropped for PlayMode: a runtime test assembly cannot
            // compile against them, so the proposal would fail on its first compile.
            string[] assembliesUnderTest = asmdefs
                .Where(asmdef => !asmdef.IsTestAssembly())
                .Where(asmdef => editMode || !asmdef.IsEditorOnly())
                .Select(asmdef => asmdef.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string preferredName = ChooseBaseName(assembliesUnderTest, productName) + (editMode ? ".Tests.Editor" : ".Tests.PlayMode");
            string folder = editMode ? "Assets/Tests/Editor/" : "Assets/Tests/PlayMode/";
            string name = ChooseUnusedName(preferredName, folder, asmdefs);

            AsmdefTemplate template = new AsmdefTemplate
            {
                name = name,
                references = new[] { UnityEngineTestRunnerReference, UnityEditorTestRunnerReference }
                    .Concat(assembliesUnderTest)
                    .ToArray(),
                includePlatforms = editMode ? new[] { "Editor" } : Array.Empty<string>()
            };
            string content = JsonConvert.SerializeObject(template, Formatting.Indented);
            return new RunTestsTestAsmdefProposal(folder + name + ".asmdef", content);
        }

        // Why a suffix instead of reusing the name: an unmarked asmdef may already own the
        // preferred name or path, and Unity rejects duplicate assembly names, so the proposal
        // must never tell the caller to overwrite an existing file.
        private static string ChooseUnusedName(string preferredName, string folder, IReadOnlyList<RunTestsAsmdefInfo> asmdefs)
        {
            string candidate = preferredName;
            for (int suffix = 2; IsNameOrPathTaken(candidate, folder, asmdefs); suffix++)
            {
                candidate = preferredName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return candidate;
        }

        private static bool IsNameOrPathTaken(string name, string folder, IReadOnlyList<RunTestsAsmdefInfo> asmdefs)
        {
            string assetPath = folder + name + ".asmdef";
            return asmdefs.Any(asmdef =>
                string.Equals(asmdef.Name, name, StringComparison.Ordinal)
                || string.Equals(asmdef.AssetPath, assetPath, StringComparison.Ordinal));
        }

        // Why a single assembly wins: "Game" -> "Game.Tests.Editor" reads as the natural sibling.
        // With several assemblies no single one is the subject, so the product name stands in.
        private static string ChooseBaseName(IReadOnlyList<string> assembliesUnderTest, string productName)
        {
            if (assembliesUnderTest.Count == 1)
            {
                return TrimEditorSuffix(assembliesUnderTest[0]);
            }

            string sanitized = new string(productName.Where(IsAssemblyNameCharacter).ToArray());
            return sanitized.Length == 0 ? FallbackBaseName : sanitized;
        }

        private static string TrimEditorSuffix(string assemblyName)
        {
            if (assemblyName.EndsWith(EditorSuffix, StringComparison.Ordinal) && assemblyName.Length > EditorSuffix.Length)
            {
                return assemblyName.Substring(0, assemblyName.Length - EditorSuffix.Length);
            }

            return assemblyName;
        }

        private static bool IsAssemblyNameCharacter(char character)
        {
            return char.IsLetterOrDigit(character) || character == '.' || character == '_';
        }

        // Why this shape: it is what the Inspector's "Test Assemblies" toggle writes on current
        // Unity versions. Newtonsoft keeps the declaration order, so the output reads like a
        // hand-written asmdef.
        private sealed class AsmdefTemplate
        {
            public string name = string.Empty;
            public string rootNamespace = string.Empty;
            public string[] references = Array.Empty<string>();
            public string[] includePlatforms = Array.Empty<string>();
            public string[] excludePlatforms = Array.Empty<string>();
            public bool allowUnsafeCode = false;
            public bool overrideReferences = true;
            public string[] precompiledReferences = { "nunit.framework.dll" };
            public bool autoReferenced = false;
            public string[] defineConstraints = { "UNITY_INCLUDE_TESTS" };
            public string[] versionDefines = Array.Empty<string>();
            public bool noEngineReferences = false;
        }
    }
}
