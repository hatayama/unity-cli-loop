using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for transform-worker bootstrap and skip/manifest smoke checks.
    /// </summary>
    public class TransformWorkerClientTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string ExpectedListEnumeratorFullName =
            "System.Collections.Generic.List`1/Enumerator<System.Int32>";

        /// <summary>
        /// What: bootstrap compiles (or reuses a cached) worker.dll, then running the worker on the
        /// e2e fixture source returns shim entries and the expected skip reasons — including bare
        /// sibling qualify, conditional-access skip, private delegate invoke (bare / this / other),
        /// null-coalescing assignment skip, and property-receiver compound-write skip.
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_OnE2EFixture_ReturnsEntriesAndExpectedSkips()
        {
            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output, Is.Not.Null);
            Assert.That(result.Output.entries, Is.Not.Null);
            Assert.That(result.Output.entries.Length, Is.GreaterThan(0), "Expected at least one shim entry.");
            Assert.That(result.Output.shimSource, Is.Not.Null.And.Not.Empty);

            bool foundCompute = false;
            bool foundQueryPrivateDelegation = false;
            bool foundListEnumeratorFullName = false;
            bool foundCallsBareSiblings = false;
            bool foundAsyncPrivateAndBareSibling = false;
            bool foundAsyncInvokePrivateDelegate = false;
            bool foundAsyncInvokePrivateDelegateOnThis = false;
            bool foundAsyncInvokePrivateDelegateOnOther = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    Assert.That(entry.shimMethodName, Does.Contain("__shim"));
                    Assert.That(entry.shimTypeName, Does.Contain("UloopHotReloadShims"));
                    Assert.That(entry.patchKind, Is.EqualTo("transplant"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.QueryPrivate))
                {
                    foundQueryPrivateDelegation = true;
                    Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindDelegation));
                    Assert.That(result.Output.shimSource, Does.Contain("__BindAccessors"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.CallsBareSiblings))
                {
                    foundCallsBareSiblings = true;
                    Assert.That(entry.patchKind, Is.EqualTo("transplant"));
                    string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                    Assert.That(slice, Does.Contain("__uloopInstance.VisibleSibling()"));
                    Assert.That(slice, Does.Contain(".VisibleStaticSibling()"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.AsyncPrivateAndBareSibling))
                {
                    foundAsyncPrivateAndBareSibling = true;
                    Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindDelegation));
                    string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                    Assert.That(slice, Does.Contain("__uloopInstance.VisibleSibling()"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.AsyncInvokePrivateDelegate))
                {
                    foundAsyncInvokePrivateDelegate = true;
                    Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindDelegation));
                    string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                    Assert.That(slice, Does.Contain("__F__callback(__uloopInstance)()"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.AsyncInvokePrivateDelegateOnThis))
                {
                    foundAsyncInvokePrivateDelegateOnThis = true;
                    Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindDelegation));
                    string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                    Assert.That(slice, Does.Contain("__F__callback(__uloopInstance)()"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.AsyncInvokePrivateDelegateOnOther))
                {
                    foundAsyncInvokePrivateDelegateOnOther = true;
                    Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindDelegation));
                    string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                    Assert.That(slice, Does.Contain("__F__callback(other)()"));
                }

                if (entry.methodName == nameof(HotReloadE2EFixture.CountEnumerator)
                    && entry.parameterTypeFullNames != null
                    && entry.parameterTypeFullNames.Length == 1
                    && entry.parameterTypeFullNames[0] == ExpectedListEnumeratorFullName)
                {
                    foundListEnumeratorFullName = true;
                }
            }

            Assert.That(foundCompute, Is.True, "ComputeWithPrivate entry missing from worker output.");
            Assert.That(
                foundQueryPrivateDelegation,
                Is.True,
                "QueryPrivate must be a delegation entry (accessor rewrite), not a worker skip.");
            Assert.That(
                foundCallsBareSiblings,
                Is.True,
                "CallsBareSiblings must transplant with qualified bare sibling calls.");
            Assert.That(
                foundAsyncPrivateAndBareSibling,
                Is.True,
                "AsyncPrivateAndBareSibling must be a delegation entry.");
            Assert.That(
                foundAsyncInvokePrivateDelegate,
                Is.True,
                "AsyncInvokePrivateDelegate must be a delegation entry with FieldRef invoke.");
            Assert.That(
                foundAsyncInvokePrivateDelegateOnThis,
                Is.True,
                "AsyncInvokePrivateDelegateOnThis must be a delegation entry with FieldRef invoke.");
            Assert.That(
                foundAsyncInvokePrivateDelegateOnOther,
                Is.True,
                "AsyncInvokePrivateDelegateOnOther must be a delegation entry with FieldRef on other.");
            Assert.That(
                foundListEnumeratorFullName,
                Is.True,
                "CountEnumerator parameterTypeFullNames must use Cecil nested-generic FullName: "
                + ExpectedListEnumeratorFullName);

            Assert.That(result.Output.skipped, Is.Not.Null, "Expected a skipped list from the worker.");
            AssertHasSkip(result, nameof(HotReloadE2EFixture.CallsBase), "base");
            AssertHasSkip(result, "ExplicitPing", "Explicit interface");
            AssertHasSkip(result, nameof(HotReloadE2EFixture.AsyncReadPrivateIndexer), "private/internal");
            AssertHasSkip(result, nameof(HotReloadE2EFixture.AsyncConditionalPrivateField), "conditional access");
            AssertHasSkip(result, nameof(HotReloadE2EFixture.AsyncNullCoalesceAssignPrivateProperty), "null-coalescing");
            AssertHasSkip(
                result,
                nameof(HotReloadE2EFixture.AsyncCompoundWriteViaPropertyReceiver),
                "receiver with possible side effects would be evaluated twice");
        }

        /// <summary>
        /// What: every worker entry carries a 1-based shim source line range so the orchestrator
        /// can attribute compile errors per method (end within the emitted source; ranges do not
        /// overlap).
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_OnE2EFixture_EveryEntryHasAValidShimSourceLineRange()
        {
            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Empty);

            int shimSourceLineCount = CountLines(result.Output.shimSource);
            List<(int Start, int End, string MethodName)> ranges =
                new List<(int Start, int End, string MethodName)>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                Assert.That(
                    entry.shimSourceStartLine,
                    Is.GreaterThanOrEqualTo(1),
                    "Entry missing a 1-based shim source start line: " + entry.methodName);
                Assert.That(
                    entry.shimSourceStartLine,
                    Is.LessThanOrEqualTo(entry.shimSourceEndLine),
                    "Entry shim source start line must not be after its end line: " + entry.methodName);
                Assert.That(
                    entry.shimSourceEndLine,
                    Is.LessThanOrEqualTo(shimSourceLineCount),
                    "Entry shim source end line exceeds shimSource line count: " + entry.methodName);
                ranges.Add((entry.shimSourceStartLine, entry.shimSourceEndLine, entry.methodName));
            }

            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (int index = 1; index < ranges.Count; index++)
            {
                Assert.That(
                    ranges[index].Start,
                    Is.GreaterThan(ranges[index - 1].End),
                    "Shim source line ranges overlap between "
                    + ranges[index - 1].MethodName
                    + " and "
                    + ranges[index].MethodName);
            }
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int lineCount = 1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    lineCount++;
                }
            }

            return lineCount;
        }

        /// <summary>
        /// What: extracts one shim method declaration from the aggregated shimSource so Contains
        /// asserts cannot pass via text belonging to a different entry.
        /// </summary>
        private static string SliceShimMethod(string shimSource, string shimMethodName)
        {
            Assert.That(shimMethodName, Is.Not.Null.And.Not.Empty);
            int nameIndex = shimSource.IndexOf(shimMethodName, StringComparison.Ordinal);
            Assert.That(
                nameIndex,
                Is.GreaterThanOrEqualTo(0),
                "shimMethodName not found in shimSource: " + shimMethodName);

            int declarationStart = shimSource.LastIndexOf(
                "public static",
                nameIndex,
                StringComparison.Ordinal);
            Assert.That(
                declarationStart,
                Is.GreaterThanOrEqualTo(0),
                "shim method declaration start not found for: " + shimMethodName);

            int openBrace = shimSource.IndexOf('{', nameIndex);
            Assert.That(
                openBrace,
                Is.GreaterThanOrEqualTo(0),
                "shim method body not found for: " + shimMethodName);

            int depth = 0;
            for (int index = openBrace; index < shimSource.Length; index++)
            {
                char character = shimSource[index];
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return shimSource.Substring(declarationStart, index - declarationStart + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced braces while slicing shim method: " + shimMethodName);
            return null;
        }

        private static void AssertHasSkip(
            TransformWorkerClientResult result,
            string methodNameFragment,
            string reasonFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(methodNameFragment)
                    && skipped.reason != null
                    && skipped.reason.Contains(reasonFragment))
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected skip for '" + methodNameFragment + "' with reason containing '"
                + reasonFragment + "'.");
        }

        /// <summary>
        /// What: with a snapshot whose ComputeWithPrivate body differs, the worker emits only that edited method and reports a positive unchangedMethodCount for the rest.
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyInOneMethod_EmitsOnlyEditedMethod()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string snapshotSource = onDisk.Replace(
                "return _secret + delta;",
                "return _secret + delta + 999;",
                StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync(snapshotSource);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.unchangedMethodCount, Is.GreaterThan(0));

            bool foundCompute = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                Assert.That(
                    entry.methodName,
                    Is.Not.EqualTo(nameof(HotReloadE2EFixture.QueryPrivate)),
                    "Unedited QueryPrivate must not appear in entries.");
                if (entry.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                }
            }

            Assert.That(foundCompute, Is.True, "Edited ComputeWithPrivate must appear in entries.");

            if (result.Output.skipped != null)
            {
                foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
                {
                    Assert.That(
                        skipped.method,
                        Does.Not.Contain(nameof(HotReloadE2EFixture.QueryPrivate)),
                        "Unedited QueryPrivate must not appear in skipped.");
                }
            }
        }

        /// <summary>
        /// What: a snapshot that differs only by EOL (LF↔CRLF) treats every method as unchanged — Windows guardrail for line-ending noise.
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyByEol_TreatsAllMethodsUnchanged()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string normalizedLf = onDisk.Replace("\r\n", "\n", StringComparison.Ordinal);
            string snapshotSource = normalizedLf.Contains('\n')
                ? normalizedLf.Replace("\n", "\r\n", StringComparison.Ordinal)
                : normalizedLf + "\r\n";
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: EOL-swapped snapshot must differ as raw text.");

            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync(snapshotSource);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Empty);
            Assert.That(result.Output.unchangedMethodCount, Is.GreaterThan(0));
        }

        /// <summary>
        /// What: a snapshot that changes only a field initializer emits the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotFieldInitializerChanged_EmitsOutsideMethodBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string snapshotSource = onDisk.Replace(
                "private int _secret = 10;",
                "private int _secret = 11;",
                StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk));

            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync(snapshotSource);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.declarationDriftWarnings, Is.Not.Null);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.Some.Contain("Edits outside method bodies in HotReloadE2EFixtures.cs"));
        }

        /// <summary>
        /// What: a snapshot that duplicates a method key falls back to no-baseline behavior (same entries as a null snapshot, unchangedMethodCount 0).
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDuplicateMethodKey_FallsBackToNoBaseline()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            const string queryPrivateBlock =
                @"        [MethodImpl(MethodImplOptions.NoInlining)]
        public int QueryPrivate()
        {
            int[] values = { 1, 2, 3 };
            return (from value in values where value < _secret select value).Count();
        }
";
            Assert.That(onDisk, Does.Contain("public int QueryPrivate()"));
            // Duplicate the method inside HotReloadE2EFixture so BuildSyntaxMethodKey collides.
            const string classCloseMarker =
                "            return token.N;\n        }\n    }\n\n    /// <summary>\n    /// Internal type used only by";
            int markerIndex = onDisk.IndexOf(classCloseMarker, StringComparison.Ordinal);
            Assert.That(markerIndex, Is.GreaterThan(0), "Could not locate HotReloadE2EFixture class close for duplicate insert.");
            int insertAt = onDisk.IndexOf("\n    }\n\n    /// <summary>", markerIndex, StringComparison.Ordinal);
            Assert.That(insertAt, Is.GreaterThan(0));
            string snapshotSource = onDisk.Insert(insertAt + 1, queryPrivateBlock);

            TransformWorkerClientResult baseline = await RunWorkerOnE2EFixtureAsync();
            TransformWorkerClientResult withCollision = await RunWorkerOnE2EFixtureAsync(snapshotSource);
            Assert.That(baseline.Success, Is.True, baseline.ErrorMessage);
            Assert.That(withCollision.Success, Is.True, withCollision.ErrorMessage);
            Assert.That(withCollision.Output.unchangedMethodCount, Is.EqualTo(0));

            HashSet<string> baselineKeys = CollectEntryKeys(baseline.Output.entries);
            HashSet<string> collisionKeys = CollectEntryKeys(withCollision.Output.entries);
            Assert.That(collisionKeys, Is.EquivalentTo(baselineKeys));
        }

        private static HashSet<string> CollectEntryKeys(TransformWorkerEntryDto[] entries)
        {
            HashSet<string> keys = new HashSet<string>();
            if (entries == null)
            {
                return keys;
            }

            foreach (TransformWorkerEntryDto entry in entries)
            {
                keys.Add(
                    entry.typeMetadataName + "::" + entry.methodName + "("
                    + string.Join(",", entry.parameterTypeFullNames ?? Array.Empty<string>()) + ")");
            }

            return keys;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnE2EFixtureAsync(
            string snapshotSource = null)
        {
            string fixturePath = ResolveE2EFixturePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = fixturePath,
                defines = compilationAssembly.defines ?? System.Array.Empty<string>(),
                referencePaths = compilationAssembly.allReferences,
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string ResolveE2EFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadE2EFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "E2E fixture source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
