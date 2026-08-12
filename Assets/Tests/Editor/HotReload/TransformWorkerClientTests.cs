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

            int fixtureLineCount = File.ReadAllLines(ResolveE2EFixturePath()).Length;
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
                Assert.That(
                    entry.sourceEndLine,
                    Is.LessThanOrEqualTo(fixtureLineCount),
                    "Entry source end line exceeds fixture file line count: " + entry.methodName);
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
                // Why outside SliceShimMethod: #line default is trailing trivia after the closing
                // '}', so the brace-bounded slice cannot see it — assert the reset in the gap
                // before the next method (or EOF) instead.
                Assert.That(
                    TextAfterShimMethodContainsLineDefault(
                        result.Output.shimSource,
                        entry.shimMethodName),
                    Is.True,
                    "Each shim method must be followed by #line default: " + entry.methodName);
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
            (bool found, int declarationStart, int closeBraceIndex) =
                FindShimMethodSpan(shimSource, shimMethodName);
            Assert.That(
                found,
                Is.True,
                "Could not locate a balanced shim method span for: " + shimMethodName);
            return shimSource.Substring(declarationStart, closeBraceIndex - declarationStart + 1);
        }

        /// <summary>
        /// What: returns whether <c>#line default</c> appears after a shim method's closing brace
        /// and before the next <c>public static</c> method declaration (or EOF).
        /// </summary>
        private static bool TextAfterShimMethodContainsLineDefault(string shimSource, string shimMethodName)
        {
            (bool found, int _, int closeBraceIndex) = FindShimMethodSpan(shimSource, shimMethodName);
            if (!found)
            {
                return false;
            }

            int gapStart = closeBraceIndex + 1;
            int nextMethod = shimSource.IndexOf("public static", gapStart, StringComparison.Ordinal);
            string gap = nextMethod < 0
                ? shimSource.Substring(gapStart)
                : shimSource.Substring(gapStart, nextMethod - gapStart);
            return gap.Contains("#line default", StringComparison.Ordinal);
        }

        /// <summary>
        /// What: finds a shim method's <c>public static</c> declaration start and the index of its
        /// balanced closing brace so slice and post-method gap checks share one scan.
        /// </summary>
        private static (bool Found, int DeclarationStart, int CloseBraceIndex) FindShimMethodSpan(
            string shimSource,
            string shimMethodName)
        {
            if (string.IsNullOrEmpty(shimSource) || string.IsNullOrEmpty(shimMethodName))
            {
                return (false, -1, -1);
            }

            int nameIndex = shimSource.IndexOf(shimMethodName, StringComparison.Ordinal);
            if (nameIndex < 0)
            {
                return (false, -1, -1);
            }

            int declarationStart = shimSource.LastIndexOf(
                "public static",
                nameIndex,
                StringComparison.Ordinal);
            if (declarationStart < 0)
            {
                return (false, -1, -1);
            }

            int openBrace = shimSource.IndexOf('{', nameIndex);
            if (openBrace < 0)
            {
                return (false, -1, -1);
            }

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
                        return (true, declarationStart, index);
                    }
                }
            }

            return (false, -1, -1);
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
        /// What: a self-snapshot of every .cs file under Assets/Tests/Editor/HotReload/ yields
        /// Success, empty parseErrors/entries/declarationDriftWarnings, and at least one
        /// unchanged or skipped method (proves the worker recognized the file). Permanent guard
        /// that identical source never false-patches after the unannotated-baseline fix.
        /// </summary>
        [Test]
        public async Task Run_WithSelfSnapshotOnHotReloadTestSources_TreatsAllMethodsUnchanged()
        {
            string directory = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload");
            Assert.That(Directory.Exists(directory), Is.True, "HotReload test directory missing.");
            string[] sourcePaths = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
            Assert.That(sourcePaths.Length, Is.GreaterThan(0), "Expected at least one HotReload test .cs file.");

            List<string> failures = new List<string>();
            foreach (string sourcePath in sourcePaths)
            {
                string fullPath = Path.GetFullPath(sourcePath);
                string projectRelativePath =
                    "Assets/Tests/Editor/HotReload/" + Path.GetFileName(fullPath);
                string onDisk = File.ReadAllText(fullPath);
                TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                    fullPath,
                    projectRelativePath,
                    snapshotSource: onDisk);

                List<string> fileFailures = new List<string>();
                if (!result.Success)
                {
                    fileFailures.Add("Success=false: " + result.ErrorMessage);
                }

                if (result.Output == null)
                {
                    fileFailures.Add("Output is null");
                    failures.Add(projectRelativePath + " -> " + string.Join("; ", fileFailures));
                    continue;
                }

                if (result.Output.parseErrors != null && result.Output.parseErrors.Length > 0)
                {
                    fileFailures.Add(
                        "parseErrors=[" + string.Join(" | ", result.Output.parseErrors) + "]");
                }

                if (result.Output.entries != null && result.Output.entries.Length > 0)
                {
                    fileFailures.Add(
                        "entries=[" + FormatEntryMethodNames(result.Output.entries) + "]");
                }

                int unchangedCount =
                    result.Output.unchangedMethods != null ? result.Output.unchangedMethods.Length : 0;
                int skippedCount = result.Output.skipped != null ? result.Output.skipped.Length : 0;
                if (unchangedCount + skippedCount < 1)
                {
                    fileFailures.Add(
                        "unchangedMethods+skipped < 1 (unchanged="
                        + unchangedCount + ", skipped=" + skippedCount + ")");
                }

                if (result.Output.declarationDriftWarnings != null
                    && result.Output.declarationDriftWarnings.Length > 0)
                {
                    fileFailures.Add(
                        "declarationDriftWarnings=["
                        + string.Join(" | ", result.Output.declarationDriftWarnings) + "]");
                }

                if (fileFailures.Count > 0)
                {
                    failures.Add(projectRelativePath + " -> " + string.Join("; ", fileFailures));
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "Self-snapshot must treat every HotReload test source as unchanged:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// What: an identical self-snapshot of HotReloadShapeFixture treats every method as
        /// unchanged (empty entries; all five shape methods listed in unchangedMethods). Guards
        /// the annotated-vs-plain AreEquivalent asymmetry on long-return / unchecked / switch.
        /// </summary>
        [Test]
        public async Task Run_WithIdenticalSnapshotOnShapeFixture_TreatsAllMethodsUnchanged()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output, Is.Not.Null);
            Assert.That(
                result.Output.entries,
                Is.Empty,
                "Identical snapshot must emit no patch entries; got: "
                + FormatEntryMethodNames(result.Output.entries));

            string[] expectedMethods =
            {
                nameof(HotReloadShapeFixture.ShortSingle),
                nameof(HotReloadShapeFixture.ExpressionBodied),
                nameof(HotReloadShapeFixture.LongSingleReturn),
                nameof(HotReloadShapeFixture.UncheckedLongBody),
                nameof(HotReloadShapeFixture.SwitchMessage),
            };
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);
            HashSet<string> unchangedNames = new HashSet<string>();
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                unchangedNames.Add(unchanged.methodName);
            }

            Assert.That(
                unchangedNames,
                Is.SupersetOf(expectedMethods),
                "Identical snapshot must list all five shape methods in unchangedMethods; got: "
                + string.Join(", ", unchangedNames));
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
        /// (same entries as a null snapshot, empty unchangedMethods) and sets
        /// baselineDisabledByDuplicateKeys so the orchestrator can warn.
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
            Assert.That(
                withCollision.Output.baselineDisabledByDuplicateKeys,
                Is.True,
                "Duplicate-key fallback must set baselineDisabledByDuplicateKeys.");

            HashSet<string> baselineKeys = CollectEntryKeys(baseline.Output.entries);
            HashSet<string> collisionKeys = CollectEntryKeys(withCollision.Output.entries);
            Assert.That(collisionKeys, Is.EquivalentTo(baselineKeys));
        }

        /// <summary>
        /// What: after arity normalization, void F(int) and void F&lt;T&gt;(int) no longer share a
        /// syntax key, so an identical self-snapshot treats both as unchanged (no entries).
        /// </summary>
        [Test]
        public async Task Run_WithSelfSnapshotOnArityDistinctMethods_TreatsBothUnchanged()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            Assert.That(onDisk, Does.Contain("HotReloadKeyNormalizationFixture"));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.baselineDisabledByDuplicateKeys, Is.False);
            Assert.That(
                result.Output.entries,
                Is.Empty,
                "Arity-distinct methods must not disable baseline; got entries: "
                + FormatEntryMethodNames(result.Output.entries));

            int unchangedFCount = 0;
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                if (unchanged.methodName == nameof(HotReloadKeyNormalizationFixture.F)
                    && unchanged.typeMetadataName != null
                    && unchanged.typeMetadataName.Contains(
                        nameof(HotReloadKeyNormalizationFixture),
                        StringComparison.Ordinal))
                {
                    unchangedFCount++;
                }
            }

            Assert.That(
                unchangedFCount,
                Is.EqualTo(2),
                "Both F(int) and F<T>(int) must appear in unchangedMethods after arity normalization.");
        }

        /// <summary>
        /// What: after including ExplicitInterfaceSpecifier in syntax keys, IA.Run and IB.Run no
        /// longer collide, so an identical self-snapshot treats both as unchanged.
        /// </summary>
        [Test]
        public async Task Run_WithSelfSnapshotOnExplicitInterfaceMethods_TreatsBothUnchanged()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            Assert.That(onDisk, Does.Contain("HotReloadExplicitInterfaceKeyFixture"));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.baselineDisabledByDuplicateKeys, Is.False);
            Assert.That(
                result.Output.entries,
                Is.Empty,
                "Explicit-interface methods must not disable baseline; got entries: "
                + FormatEntryMethodNames(result.Output.entries));

            int unchangedRunCount = 0;
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                // Why EndsWith(".Run"): Roslyn reports explicit-interface methodSymbol.Name as
                // "IHotReloadKeyNormA.Run" (not bare "Run").
                bool isExplicitFixture =
                    unchanged.typeMetadataName != null
                    && unchanged.typeMetadataName.Contains(
                        nameof(HotReloadExplicitInterfaceKeyFixture),
                        StringComparison.Ordinal);
                bool isRunName =
                    unchanged.methodName != null
                    && unchanged.methodName.EndsWith(".Run", StringComparison.Ordinal);
                if (isExplicitFixture && isRunName)
                {
                    unchangedRunCount++;
                }
            }

            Assert.That(
                unchangedRunCount,
                Is.EqualTo(2),
                "Both IA.Run and IB.Run must appear in unchangedMethods after key qualification.");
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

            // Why absolute: the worker process cwd is not the Unity project root, so relative
            // ScriptAssemblies paths from CompilationPipeline become "Reference not found"
            // parseErrors and poison self-snapshot assertions that require parseErrors empty.
            string[] referencePaths = BuildAbsoluteReferencePaths(
                compilationAssembly.allReferences,
                targetDllPath);

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? System.Array.Empty<string>(),
                referencePaths = referencePaths,
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string[] BuildAbsoluteReferencePaths(
            string[] allReferences,
            string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                    {
                        continue;
                    }

                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            bool hasTarget = false;
            foreach (string path in paths)
            {
                if (string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    hasTarget = true;
                    break;
                }
            }

            if (!hasTarget)
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
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

        private static string ResolveShapeFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadShapeFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Shape fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveShapeFixtureProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/HotReloadShapeFixtures.cs";
        }

        private static string FormatEntryMethodNames(TransformWorkerEntryDto[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return "(none)";
            }

            List<string> names = new List<string>(entries.Length);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                names.Add(entry.methodName);
            }

            return string.Join(", ", names);
        }
    }
}
