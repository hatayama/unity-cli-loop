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
        /// What: every worker entry carries a 1-based original-source line range (start &gt;= 1,
        /// start &lt;= end) and those ranges do not overlap within the fixture file.
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_OnE2EFixture_EveryEntryHasAValidOriginalSourceLineRange()
        {
            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Empty);

            List<(int Start, int End, string MethodName)> ranges =
                new List<(int Start, int End, string MethodName)>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                Assert.That(
                    entry.sourceStartLine,
                    Is.GreaterThanOrEqualTo(1),
                    "Entry missing a 1-based original source start line: " + entry.methodName);
                Assert.That(
                    entry.sourceStartLine,
                    Is.LessThanOrEqualTo(entry.sourceEndLine),
                    "Entry source start line must not be after its end line: " + entry.methodName);
                ranges.Add((entry.sourceStartLine, entry.sourceEndLine, entry.methodName));
            }

            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (int index = 1; index < ranges.Count; index++)
            {
                Assert.That(
                    ranges[index].Start,
                    Is.GreaterThan(ranges[index - 1].End),
                    "Original source line ranges overlap between "
                    + ranges[index - 1].MethodName
                    + " and "
                    + ranges[index].MethodName);
            }
        }

        /// <summary>
        /// What: emitted shimSource contains #line directives that name the project-relative path
        /// before methods/statements and #line default after each shim method.
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_OnE2EFixture_ShimSourceContainsLineDirectives()
        {
            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.shimSource, Is.Not.Null.And.Not.Empty);

            string expectedDirectivePrefix = "#line ";
            string expectedPathLiteral = "\"" + ResolveE2EFixtureProjectRelativePath() + "\"";
            Assert.That(result.Output.shimSource, Does.Contain(expectedDirectivePrefix));
            Assert.That(result.Output.shimSource, Does.Contain(expectedPathLiteral));
            Assert.That(result.Output.shimSource, Does.Contain("#line default"));

            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
                Assert.That(
                    slice,
                    Does.Contain(expectedDirectivePrefix),
                    "Shim method must carry at least one #line directive: " + entry.methodName);
                Assert.That(
                    slice,
                    Does.Contain(expectedPathLiteral),
                    "Shim method #line must name the project-relative path: " + entry.methodName);
            }
        }

        /// <summary>
        /// What: an expression-bodied method maps its #line to the arrow expression's original
        /// start line (not the declaration keyword line when they differ).
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_ArrowBodyMethod_LineDirectiveUsesArrowExpressionLine()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string tempDirectory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "ArrowLineDirective");
            Directory.CreateDirectory(tempDirectory);
            string sourcePath = Path.Combine(tempDirectory, "ArrowBodyFixture.cs");
            // Line 1 blank intentionally so the arrow expression starts on line 7 while the
            // method keyword is on line 6 — the #line must report 7.
            // Why a constant body: private-field access would force Delegation against a type that
            // is not in the compiled test assembly; a literal return keeps Transplant and still
            // exercises arrow-expression line mapping.
            const string sourceText =
                "\nnamespace ArrowLineDirectiveFixture\n{\n    public class ArrowHost\n    {\n"
                + "        public int Read()\n"
                + "            => 42;\n"
                + "    }\n}\n";
            File.WriteAllText(sourcePath, sourceText);

            string projectRelativePath = "Library/UloopHotReload/TestSources/ArrowLineDirective/ArrowBodyFixture.cs";
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(sourcePath, projectRelativePath);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Empty);

            TransformWorkerEntryDto arrowEntry = null;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "Read")
                {
                    arrowEntry = entry;
                    break;
                }
            }

            Assert.That(arrowEntry, Is.Not.Null, "Read entry missing.");
            // Why not SliceShimMethod: expression-bodied shims have no `{` block to bound a slice.
            string expectedLineDirective = "#line 7 \"" + projectRelativePath + "\"";
            Assert.That(
                result.Output.shimSource,
                Does.Contain(expectedLineDirective),
                "Arrow-body #line must point at the expression start line.\n" + result.Output.shimSource);
            Assert.That(
                result.Output.shimSource,
                Does.Contain(arrowEntry.shimMethodName),
                "Shim method must appear in emitted source.");
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
        /// What: with a snapshot whose ComputeWithPrivate body differs, the worker emits only that
        /// edited method and lists the remaining methods in unchangedMethods.
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
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);
            Assert.That(result.Output.unchangedMethods.Length, Is.GreaterThan(0));
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Method-body-only edits must not emit the outside-method-body drift warning.");

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
        /// What: a snapshot that differs only by EOL (LF↔CRLF) treats every method as unchanged —
        /// Windows guardrail for line-ending noise — and lists ComputeWithPrivate in
        /// unchangedMethods.
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyByEol_TreatsAllMethodsUnchanged()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string normalizedLf = onDisk.Replace("\r\n", "\n", StringComparison.Ordinal);
            // Opposite EOL of the on-disk bytes so the precondition holds for both LF and CRLF checkouts.
            string snapshotSource = onDisk.Contains("\r\n", StringComparison.Ordinal)
                ? normalizedLf
                : normalizedLf.Replace("\n", "\r\n", StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: EOL-swapped snapshot must differ as raw text.");

            TransformWorkerClientResult result = await RunWorkerOnE2EFixtureAsync(snapshotSource);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Empty);
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);
            Assert.That(result.Output.unchangedMethods.Length, Is.GreaterThan(0));
            bool foundComputeUnchanged = false;
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                if (unchanged.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundComputeUnchanged = true;
                    break;
                }
            }

            Assert.That(
                foundComputeUnchanged,
                Is.True,
                "EOL-only snapshot must list ComputeWithPrivate in unchangedMethods.");
            Assert.That(result.Output.skipped, Is.Empty);
            Assert.That(result.Output.declarationDriftWarnings, Is.Empty);
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
        /// What: a snapshot that duplicates a method key falls back to no-baseline behavior
        /// (same entries as a null snapshot, empty unchangedMethods).
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
            Assert.That(
                withCollision.Output.unchangedMethods == null
                || withCollision.Output.unchangedMethods.Length == 0,
                Is.True,
                "Duplicate-key fallback must not report unchanged methods.");

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
            return await RunWorkerOnSourceAsync(
                ResolveE2EFixturePath(),
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource = null)
        {
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
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? System.Array.Empty<string>(),
                referencePaths = compilationAssembly.allReferences,
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath
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

        private static string ResolveE2EFixtureProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
        }
    }
}
