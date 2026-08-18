using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

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
        /// start line (not the declaration keyword line when they differ). Uses the compiled
        /// HotReloadAddedMemberHost.ArrowRead so the type is present in the test assembly.
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_ArrowBodyMethod_LineDirectiveUsesArrowExpressionLine()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string hostPath = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedMemberHost.cs");
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ArrowRead() => 1;",
                "        public int ArrowRead()\n            => 42;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: ArrowRead must become multi-line.");

            string tempDirectory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "ArrowLineDirective");
            Directory.CreateDirectory(tempDirectory);
            string sourcePath = Path.Combine(tempDirectory, "ArrowBodyFixture.cs");
            File.WriteAllText(sourcePath, edited);

            string projectRelativePath = "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                projectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Empty);

            TransformWorkerEntryDto arrowEntry = null;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == nameof(HotReloadAddedMemberHost.ArrowRead))
                {
                    arrowEntry = entry;
                    break;
                }
            }

            Assert.That(arrowEntry, Is.Not.Null, "ArrowRead entry missing.");
            // Why a separate arrow line: FindLineNumberContaining("=> 42;") must not match the
            // method-keyword line. Keep ArrowRead as `public int ArrowRead()\n            => 42;`
            // so the #line target stays the expression start, not the declaration.
            int expectedLine = FindLineNumberContaining(edited, "=> 42;");
            Assert.That(expectedLine, Is.GreaterThan(0));
            // Why not SliceShimMethod: expression-bodied shims have no `{` block to bound a slice.
            string expectedLineDirective = "#line " + expectedLine + " \"" + projectRelativePath + "\"";
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
        /// end token (balanced <c>}</c>, or terminating <c>;</c> for expression-bodied shims) so
        /// slice and post-method gap checks share one scan.
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

            int arrowIndex = shimSource.IndexOf("=>", nameIndex, StringComparison.Ordinal);
            int openBrace = shimSource.IndexOf('{', nameIndex);
            // Why arrow-before-brace: expression-bodied shims have no '{'; IndexOf('{') would
            // otherwise latch onto the next method and falsely include its #line directives.
            if (arrowIndex >= 0 && (openBrace < 0 || arrowIndex < openBrace))
            {
                int semicolonIndex = FindExpressionBodiedShimSemicolon(shimSource, arrowIndex);
                if (semicolonIndex < 0)
                {
                    return (false, -1, -1);
                }

                return (true, declarationStart, semicolonIndex);
            }

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

        /// <summary>
        /// What: locates the terminating semicolon of an expression-bodied shim after its <c>=&gt;</c>.
        /// </summary>
        private static int FindExpressionBodiedShimSemicolon(string shimSource, int arrowIndex)
        {
            int parenDepth = 0;
            int bracketDepth = 0;
            int braceDepth = 0;
            for (int index = arrowIndex + 2; index < shimSource.Length; index++)
            {
                char character = shimSource[index];
                if (character == '(')
                {
                    parenDepth++;
                }
                else if (character == ')')
                {
                    parenDepth--;
                }
                else if (character == '[')
                {
                    bracketDepth++;
                }
                else if (character == ']')
                {
                    bracketDepth--;
                }
                else if (character == '{')
                {
                    braceDepth++;
                }
                else if (character == '}')
                {
                    braceDepth--;
                }
                else if (character == ';'
                    && parenDepth == 0
                    && bracketDepth == 0
                    && braceDepth == 0)
                {
                    return index;
                }
            }

            return -1;
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
                // Why skip: a global-using-only file has no methods to mark unchanged; the worker
                // correctly emits empty entries/skipped/unchanged for it.
                if (!ContainsTypeDeclaration(onDisk))
                {
                    continue;
                }

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
        /// What: editing only a const value emits the dedicated const-drift warning and does not
        /// also emit the generic outside-method-body warning.
        /// </summary>
        [Test]
        public async Task Run_WithConstValueOnlyEdit_EmitsConstDriftWithoutOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string editedSource = onDisk.Replace(
                "private const int TuningConst = 3;",
                "private const int TuningConst = 4;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: const value must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "ConstOnlyDriftWarning.cs");
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.declarationDriftWarnings, Is.Not.Null);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.Some.Contain("TuningConst").And.Contain("is 4 in the edited source but 3"),
                "Const-only edits must keep the dedicated const-drift warning.\n"
                + string.Join("\n", result.Output.declarationDriftWarnings));
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Const-only edits must not also emit the generic outside-body warning.");
        }

        /// <summary>
        /// What: editing only an enum member value emits the dedicated const-drift warning and
        /// does not also emit the generic outside-method-body warning.
        /// </summary>
        [Test]
        public async Task Run_WithEnumMemberValueOnlyEdit_EmitsConstDriftWithoutOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string editedSource = onDisk.Replace(
                "Active = 1",
                "Active = 2",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: enum member value must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "EnumMemberOnlyDriftWarning.cs");
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.declarationDriftWarnings, Is.Not.Null);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.Some.Contain("HotReloadE2EMode.Active").And.Contain("is 2 in the edited source but 1"),
                "Enum-member-only edits must keep the dedicated const-drift warning.\n"
                + string.Join("\n", result.Output.declarationDriftWarnings));
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Enum-member-only edits must not also emit the generic outside-body warning.");
        }

        /// <summary>
        /// What: a non-const field initializer change still emits the generic outside-method-body
        /// warning (const stripping must not hide ordinary field initializer drift).
        /// </summary>
        [Test]
        public async Task Run_WithNonConstFieldInitializerEdit_StillEmitsOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string editedSource = onDisk.Replace(
                "private int _secret = 10;",
                "private int _secret = 12;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: field initializer must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "NonConstFieldInitializerDrift.cs");
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.declarationDriftWarnings, Is.Not.Null);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.Some.Contain("Edits outside method bodies"),
                "Non-const field initializer edits must still emit the outside-body warning.");
            AssertContainsOutsideMethodBodyDriftWarning(result, "NonConstFieldInitializerDrift.cs");
        }

        /// <summary>
        /// What: adding a using directive plus a method-body edit does not emit the
        /// outside-method-body drift warning, and the body edit is still Patched.
        /// </summary>
        [Test]
        public async Task Run_UsingDirectiveOnlyPlusBodyEdit_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UsingDirectiveOnlyPlusBodyEdit.cs";
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string editedSource = onDisk.Replace(
                "using UnityEngine;\n",
                "using UnityEngine;\nusing System.Text;\n",
                StringComparison.Ordinal);
            editedSource = editedSource.Replace(
                "return _secret + delta;",
                "return _secret + delta + 1;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: using and body must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);

            bool foundCompute = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    break;
                }
            }

            Assert.That(foundCompute, Is.True, "Body edit must still emit a Patched ComputeWithPrivate entry.");
        }

        private const string ExpectedAddedPropertySkipReason =
            "Added properties are out of scope for hot reload; the compiled assembly has no such member. "
            + "Use a 'const' or a plain added field for the value, or run 'uloop compile'.";

        private const string ExpectedExplicitAccessorSkipReason =
            "Property setter, init, or indexer accessors are out of scope for v1; "
            + "run 'uloop compile' to apply accessor edits.";

        /// <summary>
        /// What: adding a get-accessor property plus a method-body edit skips the getter with
        /// the added-property reason and does not emit the outside-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_AddedExpressionBodiedPropertyPlusBodyEdit_SkipsGetterWithoutOutsideBodyWarning()
        {
            const string fileName = "AddedExpressionBodiedPropertyDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnEditedE2ECopyAsync(
                fileName,
                editedSource =>
                {
                    string next = editedSource.Replace(
                        "        public int Counter;",
                        "        public int AddedProbe => 7;\n\n        public int Counter;",
                        StringComparison.Ordinal);
                    return next.Replace(
                        "return _secret + delta;",
                        "return _secret + delta + 1;",
                        StringComparison.Ordinal);
                });

            AssertSkippedContains(result, "get_AddedProbe", ExpectedAddedPropertySkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
            AssertPatchedComputeWithPrivate(result);
        }

        /// <summary>
        /// What: adding a setter-only property plus a method-body edit skips the setter with
        /// the explicit-accessor reason and does not emit the outside-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_AddedSetterOnlyPropertyPlusBodyEdit_SkipsSetterWithoutOutsideBodyWarning()
        {
            const string fileName = "AddedSetterOnlyPropertyDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnEditedE2ECopyAsync(
                fileName,
                editedSource =>
                {
                    string next = editedSource.Replace(
                        "        public int Counter;",
                        "        public int AddedSetterOnly { set { } }\n\n        public int Counter;",
                        StringComparison.Ordinal);
                    return next.Replace(
                        "return _secret + delta;",
                        "return _secret + delta + 1;",
                        StringComparison.Ordinal);
                });

            AssertSkippedContains(result, "set_AddedSetterOnly", ExpectedExplicitAccessorSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
            AssertPatchedComputeWithPrivate(result);
        }

        /// <summary>
        /// What: adding an auto-property emits no skip row, so the outside-body drift warning
        /// remains the only signal that the new member was not applied.
        /// </summary>
        [Test]
        public async Task Run_AddedAutoPropertyPlusBodyEdit_EmitsOutsideBodyWarningWithoutSkipRow()
        {
            const string fileName = "AddedAutoPropertyDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnEditedE2ECopyAsync(
                fileName,
                editedSource =>
                {
                    string next = editedSource.Replace(
                        "        public int Counter;",
                        "        public int AddedAuto { get; set; }\n\n        public int Counter;",
                        StringComparison.Ordinal);
                    return next.Replace(
                        "return _secret + delta;",
                        "return _secret + delta + 1;",
                        StringComparison.Ordinal);
                });

            AssertSkippedDoesNotContain(result, "AddedAuto");
            AssertContainsOutsideMethodBodyDriftWarning(result, fileName);
            AssertPatchedComputeWithPrivate(result);
        }

        /// <summary>
        /// What: rewriting an existing property getter body still emits that getter as Patched
        /// and does not emit the outside-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_ExistingPropertyGetterBodyEdit_KeepsPatchedGetterWithoutOutsideBodyWarning()
        {
            const string fileName = "ExistingPropertyGetterBodyEdit.cs";
            TransformWorkerClientResult result = await RunWorkerOnEditedE2ECopyAsync(
                fileName,
                editedSource => editedSource.Replace(
                    "get { return _secret; }",
                    "get { return _secret + 1; }",
                    StringComparison.Ordinal));

            bool foundGetter = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "get_ExplicitBodyGetter")
                {
                    foundGetter = true;
                    break;
                }
            }

            Assert.That(foundGetter, Is.True, "Existing getter body edit must stay Patched.");
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: editing only the getter of an existing getter+setter property still emits the
        /// setter skip row and does not emit outside-body drift. Pins the snapshot ContainsKey
        /// guard so an existing property is not stripped from the current tree alone.
        /// </summary>
        [Test]
        public async Task Run_ExistingGetterAndSetterProperty_GetterBodyEdit_SkipsSetterWithoutOutsideBodyWarning()
        {
            const string fileName = "ExistingGetterAndSetterPropertyGetterEdit.cs";
            string onDisk = File.ReadAllText(ResolveShapeFixturePath());
            string editedSource = onDisk.Replace(
                "get { return _value; }",
                "get { return _value + 1; }",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: getter body must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedContains(result, "set_Value", ExpectedExplicitAccessorSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
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

        /// <summary>
        /// What: private void Start on MonoBehaviour gets a direct lifecycleNote; POCO Start and
        /// public/parameterized Start on MonoBehaviour do not (name-only notes would flag all).
        /// </summary>
        [Test]
        public async Task Run_WithStartLifecycleGates_EmitsNoteOnlyForPrivateVoidMonoBehaviour()
        {
            string monoPrivateSource =
                "using UnityEngine;\n"
                + "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n{\n"
                + "public class HotReloadLifecycleMonoPrivateStartFixture : MonoBehaviour\n"
                + "{\n"
                + "    private void Start()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x += 1;\n"
                + "    }\n"
                + "}\n}\n";
            string pocoSource =
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n{\n"
                + "public class HotReloadLifecyclePocoStartFixture\n"
                + "{\n"
                + "    private void Start()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x += 1;\n"
                + "    }\n"
                + "}\n}\n";
            string monoPublicSource =
                "using UnityEngine;\n"
                + "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n{\n"
                + "public class HotReloadLifecycleMonoPublicStartFixture : MonoBehaviour\n"
                + "{\n"
                + "    public void Start()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x += 1;\n"
                + "    }\n"
                + "}\n}\n";
            string monoParameterizedSource =
                "using UnityEngine;\n"
                + "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n{\n"
                + "public class HotReloadLifecycleMonoParamStartFixture : MonoBehaviour\n"
                + "{\n"
                + "    private void Start(int delay)\n"
                + "    {\n"
                + "        int x = delay;\n"
                + "        x += 1;\n"
                + "    }\n"
                + "}\n}\n";

            TransformWorkerEntryDto monoPrivateEntry =
                await RunWorkerAndFindEntryAsync(
                    monoPrivateSource, "LifecycleMonoPrivateStart.cs", "Start");
            Assert.That(monoPrivateEntry.lifecycleNote, Is.Not.Null.And.Not.Empty);
            Assert.That(
                monoPrivateEntry.lifecycleNote,
                Does.Contain("Start is a one-shot lifecycle method"));

            TransformWorkerEntryDto pocoEntry =
                await RunWorkerAndFindEntryAsync(pocoSource, "LifecyclePocoStart.cs", "Start");
            Assert.That(
                pocoEntry.lifecycleNote,
                Is.Null.Or.Empty,
                "POCO Start must not get a lifecycle note.");

            TransformWorkerEntryDto monoPublicEntry =
                await RunWorkerAndFindEntryAsync(
                    monoPublicSource, "LifecycleMonoPublicStart.cs", "Start");
            Assert.That(
                monoPublicEntry.lifecycleNote,
                Is.Null.Or.Empty,
                "public void Start on MonoBehaviour must not get a lifecycle note.");

            TransformWorkerEntryDto monoParameterizedEntry =
                await RunWorkerAndFindEntryAsync(
                    monoParameterizedSource, "LifecycleMonoParamStart.cs", "Start");
            Assert.That(
                monoParameterizedEntry.lifecycleNote,
                Is.Null.Or.Empty,
                "private void Start(int) must not get a lifecycle note.");
        }

        /// <summary>
        /// What: with an identical snapshot, property getters with bodies are listed in
        /// unchangedMethods as get_&lt;Name&gt; (Skipped-only accessors would leave them out).
        /// </summary>
        [Test]
        public async Task Run_WithIdenticalSnapshotOnPropertyGetterFixture_ListsGettersUnchanged()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            Assert.That(onDisk, Does.Contain(nameof(HotReloadPropertyGetterFixture)));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.unchangedMethods, Is.Not.Null);

            HashSet<string> unchangedNames = new HashSet<string>();
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                if (unchanged.typeMetadataName != null
                    && unchanged.typeMetadataName.Contains(nameof(HotReloadPropertyGetterFixture)))
                {
                    unchangedNames.Add(unchanged.methodName);
                }
            }

            Assert.That(
                unchangedNames,
                Does.Contain("get_HeightAmplitude"),
                "Unedited expression-bodied getter must appear in unchangedMethods; got: "
                + string.Join(", ", unchangedNames));
            Assert.That(
                unchangedNames,
                Does.Contain("get_Score"),
                "Unedited block getter must appear in unchangedMethods; got: "
                + string.Join(", ", unchangedNames));
        }

        /// <summary>
        /// What: when only a property getter body differs from the snapshot, the worker emits a
        /// get_&lt;Name&gt; entry (Skipped for accessors would leave entries empty for that edit).
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyInGetter_EmitsGetAccessorEntry()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            string snapshotSource = onDisk.Replace(
                "public static float HeightAmplitude => 5f;",
                "public static float HeightAmplitude => 6f;",
                StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: snapshotSource);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Null);

            bool foundGetter = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "get_HeightAmplitude")
                {
                    foundGetter = true;
                    Assert.That(
                        entry.parameterTypeFullNames == null
                        || entry.parameterTypeFullNames.Length == 0,
                        "Getter entries must have zero parameters.");
                }
            }

            Assert.That(
                foundGetter,
                Is.True,
                "Edited get_HeightAmplitude must appear in entries; got: "
                + FormatEntryMethodNames(result.Output.entries)
                + "; skipped="
                + FormatSkippedMethodNames(result.Output.skipped));
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Getter-body-only edits must not emit the outside-method-body drift warning.");
        }

        /// <summary>
        /// What: editing only an instance block getter emits get_Score with a successful shim
        /// compile (covers __instance injection + private field rewrite + BlockSyntax path).
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyInBlockGetter_EmitsGetScoreEntry()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            string snapshotSource = onDisk.Replace(
                "return _score;",
                "return _score + 1;",
                StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: snapshotSource);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Block getter-body-only edits must not emit outside-method-body drift.");

            bool foundGetter = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "get_Score")
                {
                    foundGetter = true;
                }
            }

            Assert.That(
                foundGetter,
                Is.True,
                "Edited get_Score must appear in entries; got: "
                + FormatEntryMethodNames(result.Output.entries));
        }

        /// <summary>
        /// What: a namespace-scoped using alias of the same name as a sibling global using alias
        /// keeps only the local alias in the shim. Flattening both into one namespace is CS1537,
        /// even though C# lets the inner alias shadow the global one.
        /// </summary>
        [Test]
        public async Task Run_WithLocalAliasShadowingGlobalAlias_KeepsLocalAliasOnly()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);

            string editedSource =
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    using AliasProbe = System.Text.StringBuilder;\n"
                + "\n"
                + "    internal class HotReloadAliasShadowFixture\n"
                + "    {\n"
                + "        public string Build()\n"
                + "        {\n"
                + "            AliasProbe builder = new AliasProbe();\n"
                + "            builder.Append(\"ok\");\n"
                + "            return builder.ToString();\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
            string globalSource = "global using AliasProbe = System.Collections.Generic.List<int>;\n";
            string sourcePath = Path.Combine(directory, "AliasShadowEdited.cs");
            string globalPath = Path.Combine(directory, "AliasShadowGlobal.cs");
            File.WriteAllText(sourcePath, editedSource);
            File.WriteAllText(globalPath, globalSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                "Library/UloopHotReload/TestSources/AliasShadowEdited.cs",
                snapshotSource: null,
                additionalAssemblySourcePaths: new[] { globalPath });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.parseErrors, Is.Empty);
            Assert.That(
                result.Output.shimSource,
                Does.Contain("using AliasProbe = System.Text.StringBuilder"));
            Assert.That(
                result.Output.shimSource,
                Does.Not.Contain("using AliasProbe = System.Collections.Generic.List<int>"),
                "Local AliasProbe must shadow the sibling global alias; both in one namespace is CS1537.");
        }

        /// <summary>
        /// What: when only a property setter body differs from the snapshot, the unchanged getter
        /// stays out of entries (baseline compare is getter-scoped, not whole-property).
        /// </summary>
        [Test]
        public async Task Run_WithSnapshotDifferingOnlyInSetter_DoesNotEmitUnchangedGetter()
        {
            string sourcePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(sourcePath);
            string snapshotSource = onDisk.Replace(
                "set { _value = value; }",
                "set { _value = value + 1; }",
                StringComparison.Ordinal);
            Assert.That(snapshotSource, Is.Not.EqualTo(onDisk), "Precondition: setter must differ.");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveShapeFixtureProjectRelativePath(),
                snapshotSource: snapshotSource);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            if (result.Output.entries != null)
            {
                foreach (TransformWorkerEntryDto entry in result.Output.entries)
                {
                    Assert.That(
                        entry.methodName,
                        Is.Not.EqualTo("get_Value"),
                        "Setter-only edits must not emit Patched get_Value.");
                }
            }

            bool foundUnchangedGetter = false;
            if (result.Output.unchangedMethods != null)
            {
                foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
                {
                    if (unchanged.methodName == "get_Value")
                    {
                        foundUnchangedGetter = true;
                    }
                }
            }

            Assert.That(
                foundUnchangedGetter,
                Is.True,
                "Unedited get_Value must appear in unchangedMethods after a setter-only edit.");
        }

        /// <summary>
        /// What: patching Awake itself emits the direct one-shot lifecycle note.
        /// </summary>
        [Test]
        public async Task Run_WithAwakeMethod_EmitsDirectLifecycleNote()
        {
            string source =
                "using UnityEngine;\n"
                + "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n{\n"
                + "public class HotReloadLifecycleAwakeFixture : MonoBehaviour\n"
                + "{\n"
                + "    private void Awake()\n"
                + "    {\n"
                + "        int x = 1;\n"
                + "        x += 1;\n"
                + "    }\n"
                + "}\n}\n";

            TransformWorkerEntryDto awakeEntry =
                await RunWorkerAndFindEntryAsync(source, "LifecycleAwake.cs", "Awake");
            Assert.That(awakeEntry.lifecycleNote, Is.Not.Null.And.Not.Empty);
            Assert.That(
                awakeEntry.lifecycleNote,
                Does.Contain("Awake is a one-shot lifecycle method"));
        }

        private const string ExpectedUnsupportedMemberKindSkipReason =
            "Constructors, operators, and event accessors are out of scope for v1; "
            + "run 'uloop compile' to apply these edits.";

        // Keep in sync with OutsideMethodBodyDriftWarningFormat in
        // Packages/src/Editor/FirstPartyTools/HotReload/TransformWorker~/TransformWorker.cs.
        // That constant lives in the Unity-ignored worker process and is not visible here.
        private const string OutsideMethodBodyDriftWarningFormat =
            "Edits outside method bodies in {0} (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up.";

        /// <summary>
        /// What: editing one instance constructor reports that .ctor as Skipped and omits an
        /// unedited overload in the same type.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_InstanceCtor_SkipsEditedOmitsUnedited()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindInstanceCtor.cs",
                "Marker = 11;",
                "Marker = 111;");

            AssertSkippedContains(
                result,
                ".ctor()",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, ".ctor(System.Int32)");
        }

        /// <summary>
        /// What: editing one type's static constructor reports that .cctor as Skipped and omits
        /// an unedited static constructor on a sibling type in the same file.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_StaticCtor_SkipsEditedOmitsUnedited()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindStaticCtor.cs",
                "Marker = 21;",
                "Marker = 211;");

            AssertSkippedContains(
                result,
                "HotReloadUnsupportedKindStaticCtorEdited..cctor()",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, "HotReloadUnsupportedKindStaticCtorUnedited");
        }

        /// <summary>
        /// What: editing one operator reports that operator as Skipped and omits an unedited
        /// operator in the same type.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_Operator_SkipsEditedOmitsUnedited()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindOperator.cs",
                "left.Marker = 31;",
                "left.Marker = 311;");

            AssertSkippedContains(
                result,
                "op_Addition",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, "op_Subtraction");
        }

        /// <summary>
        /// What: editing one conversion operator reports that conversion as Skipped and omits an
        /// unedited conversion in the same type.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ConversionOperator_SkipsEditedOmitsUnedited()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindConversion.cs",
                "value.Marker = 41;",
                "value.Marker = 411;");

            AssertSkippedContains(
                result,
                "op_Implicit",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, "op_Explicit");
        }

        /// <summary>
        /// What: editing one explicit event accessor reports that event's add and remove as
        /// Skipped (member-level equivalence, same granularity as property accessors) and omits
        /// accessors of an unedited event in the same type.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_EventAccessor_SkipsEditedOmitsUnedited()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindEventAccessor.cs",
                "Marker = 51;",
                "Marker = 511;");

            AssertSkippedContains(
                result,
                "add_Edited",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedContains(
                result,
                "remove_Edited",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, "add_Unedited");
            AssertSkippedDoesNotContain(result, "remove_Unedited");
        }

        /// <summary>
        /// What: a constructor body-only edit reports the ctor as Skipped and does not emit
        /// the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_InstanceCtorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindInstanceCtorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "Marker = 11;",
                "Marker = 111;");

            AssertSkippedContains(
                result,
                ".ctor()",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an operator body-only edit reports the operator as Skipped and does not emit
        /// the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_OperatorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindOperatorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "left.Marker = 31;",
                "left.Marker = 311;");

            AssertSkippedContains(
                result,
                "op_Addition",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: a conversion-operator body-only edit reports the conversion as Skipped and
        /// does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ConversionOperatorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindConversionBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "value.Marker = 41;",
                "value.Marker = 411;");

            AssertSkippedContains(
                result,
                "op_Implicit",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an event-accessor body-only edit reports the edited accessors as Skipped and
        /// does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_EventAccessorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindEventAccessorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "Marker = 51;",
                "Marker = 511;");

            AssertSkippedContains(
                result,
                "add_Edited",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedContains(
                result,
                "remove_Edited",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an expression-bodied constructor body-only edit reports the ctor as Skipped
        /// and does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ExpressionCtorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindExpressionCtorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "Marker = 61;",
                "Marker = 611;");

            AssertSkippedContains(
                result,
                "HotReloadUnsupportedKindExpressionCtorFixture..ctor()",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an expression-bodied operator body-only edit reports the operator as Skipped
        /// and does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ExpressionOperatorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindExpressionOperatorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "left.Marker = 71;",
                "left.Marker = 711;");

            AssertSkippedContains(
                result,
                "op_Multiply",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an expression-bodied conversion body-only edit reports the conversion as
        /// Skipped and does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ExpressionConversionBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindExpressionConversionBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "(value.Marker = 81)",
                "(value.Marker = 811)");

            AssertSkippedContains(
                result,
                "HotReloadUnsupportedKindExpressionConversionFixture.op_Implicit",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: an expression-bodied event-accessor body-only edit reports the accessors as
        /// Skipped and does not emit the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ExpressionEventAccessorBodyOnly_DoesNotEmitOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindExpressionEventAccessorBodyOnly.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "Marker = 91;",
                "Marker = 911;");

            AssertSkippedContains(
                result,
                "add_ArrowEdited",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertDoesNotContainOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: adding a constructor initializer (declaration-only) still emits the
        /// outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_CtorInitializerEdit_EmitsOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindCtorInitializerDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "        public HotReloadUnsupportedKindCtorFixture()\n        {",
                "        public HotReloadUnsupportedKindCtorFixture() : this(0)\n        {");

            AssertContainsOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: adding an attribute to an operator (declaration-only) still emits the
        /// outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_OperatorAttributeEdit_EmitsOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindOperatorAttributeDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "        public static HotReloadUnsupportedKindOperatorFixture operator +(",
                "        [Obsolete]\n        public static HotReloadUnsupportedKindOperatorFixture operator +(");

            AssertContainsOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: adding an attribute to a conversion operator (declaration-only) still emits
        /// the outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_ConversionAttributeEdit_EmitsOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindConversionAttributeDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "        public static implicit operator int(HotReloadUnsupportedKindConversionFixture value)",
                "        [Obsolete]\n        public static implicit operator int(HotReloadUnsupportedKindConversionFixture value)");

            AssertContainsOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: adding an attribute to an event (declaration-only) still emits the
        /// outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_EventAttributeEdit_EmitsOutsideBodyWarning()
        {
            const string fileName = "UnsupportedKindEventAttributeDrift.cs";
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                fileName,
                "        public event Action Edited",
                "        [Obsolete]\n        public event Action Edited");

            AssertContainsOutsideMethodBodyDriftWarning(result, fileName);
        }

        /// <summary>
        /// What: adding an instance constructor that is absent from the verified snapshot
        /// reports that .ctor as Skipped with the unsupported-member reason (same path as an
        /// edit) and omits unedited overloads already in the snapshot.
        /// </summary>
        [Test]
        public async Task Run_UnsupportedMemberKind_AddedInstanceCtor_IsSkipped()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "UnsupportedKindAddedInstanceCtor.cs",
                "            Marker = value;\n        }",
                "            Marker = value;\n        }\n\n"
                + "        public HotReloadUnsupportedKindCtorFixture(int a, int b)\n"
                + "        {\n"
                + "            Marker = a + b;\n"
                + "        }");

            AssertSkippedContains(
                result,
                "HotReloadUnsupportedKindCtorFixture..ctor(System.Int32,System.Int32)",
                ExpectedUnsupportedMemberKindSkipReason);
            AssertSkippedDoesNotContain(result, ".ctor()");
            AssertSkippedDoesNotContain(result, ".ctor(System.Int32)");
        }

        /// <summary>
        /// What: editing only a local function body emits the parent method as a patch entry,
        /// not Skipped, and does not emit a separate local-function row.
        /// </summary>
        [Test]
        public async Task Run_LocalFunction_EmitsParentMethodOnly()
        {
            TransformWorkerClientResult result = await RunWorkerOnUnsupportedKindEditAsync(
                "LocalFunctionParent.cs",
                "return 41;",
                "return 42;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Null);
            Assert.That(
                result.Output.entries.Length,
                Is.EqualTo(1),
                "Local-function body edits must emit exactly one parent-method entry; got: "
                + FormatEntryMethodNames(result.Output.entries)
                + "; skipped="
                + FormatSkippedMethodNames(result.Output.skipped));
            Assert.That(result.Output.entries[0].methodName, Is.EqualTo("Compute"));
            AssertSkippedDoesNotContain(result, "Compute");
            AssertSkippedDoesNotContain(result, "Local");
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnUnsupportedKindEditAsync(
            string fileName,
            string originalFragment,
            string editedFragment)
        {
            string fixturePath = ResolveUnsupportedKindFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string currentSource = onDisk.Replace(
                originalFragment,
                editedFragment,
                StringComparison.Ordinal);
            Assert.That(currentSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, currentSource);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveUnsupportedKindFixtureProjectRelativePath(),
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnEditedE2ECopyAsync(
            string fileName,
            Func<string, string> edit)
        {
            string onDisk = File.ReadAllText(ResolveE2EFixturePath());
            string editedSource = edit(onDisk);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ResolveE2EFixtureProjectRelativePath(),
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static void AssertPatchedComputeWithPrivate(TransformWorkerClientResult result)
        {
            bool foundCompute = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    break;
                }
            }

            Assert.That(
                foundCompute,
                Is.True,
                "Body edit must still emit a Patched ComputeWithPrivate entry.");
        }

        private static void AssertDoesNotContainOutsideMethodBodyDriftWarning(
            TransformWorkerClientResult result,
            string fileName)
        {
            string expectedWarning = string.Format(OutsideMethodBodyDriftWarningFormat, fileName);
            string[] warnings = result.Output.declarationDriftWarnings ?? Array.Empty<string>();
            Assert.That(
                warnings,
                Does.Not.Contain(expectedWarning),
                "Body-only unsupported-kind edits must not emit the outside-body warning.\n"
                + string.Join("\n", warnings));
        }

        private static void AssertContainsOutsideMethodBodyDriftWarning(
            TransformWorkerClientResult result,
            string fileName)
        {
            string expectedWarning = string.Format(OutsideMethodBodyDriftWarningFormat, fileName);
            string[] warnings = result.Output.declarationDriftWarnings ?? Array.Empty<string>();
            Assert.That(
                warnings,
                Does.Contain(expectedWarning),
                "Declaration-only unsupported-kind edits must emit the outside-body warning.\n"
                + string.Join("\n", warnings));
        }

        private static void AssertSkippedContains(
            TransformWorkerClientResult result,
            string methodFragment,
            string expectedReason)
        {
            Assert.That(result.Output.skipped, Is.Not.Null, "Expected a skipped list from the worker.");
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(methodFragment)
                    && skipped.reason == expectedReason)
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected skip containing '" + methodFragment + "' with the unsupported-member reason; got: "
                + FormatSkippedMethodNames(result.Output.skipped));
        }

        private static void AssertSkippedDoesNotContain(
            TransformWorkerClientResult result,
            string methodFragment)
        {
            if (result.Output.skipped == null)
            {
                return;
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(
                    skipped.method,
                    Does.Not.Contain(methodFragment),
                    "Unedited member '" + methodFragment + "' must not appear in skipped; got: "
                    + FormatSkippedMethodNames(result.Output.skipped));
            }
        }

        private static async Task<TransformWorkerEntryDto> RunWorkerAndFindEntryAsync(
            string source,
            string fileName,
            string methodName)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, source);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HotReloadConstants.TestSourcesRelativeDirectory.Replace('\\', '/') + "/" + fileName);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries, Is.Not.Null);

            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            Assert.Fail(
                "Expected entry for " + methodName + "; got: "
                + FormatEntryMethodNames(result.Output.entries));
            return null;
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
            string snapshotSource = null,
            string[] additionalAssemblySourcePaths = null)
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

            string[] assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(
                compilationAssembly.sourceFiles);
            if (additionalAssemblySourcePaths != null && additionalAssemblySourcePaths.Length > 0)
            {
                List<string> merged = new List<string>(assemblySourcePaths);
                foreach (string additionalPath in additionalAssemblySourcePaths)
                {
                    merged.Add(Path.GetFullPath(additionalPath));
                }

                assemblySourcePaths = merged.ToArray();
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? System.Array.Empty<string>(),
                referencePaths = referencePaths,
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = assemblySourcePaths
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

        private static string[] BuildAbsoluteAssemblySourcePaths(string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                string absoluteSourcePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absoluteSourcePath);
            }

            return paths;
        }

        private static bool ContainsTypeDeclaration(string sourceText)
        {
            return sourceText.IndexOf(" class ", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" struct ", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" interface ", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" enum ", StringComparison.Ordinal) >= 0
                || sourceText.IndexOf(" record ", StringComparison.Ordinal) >= 0;
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

        private static string ResolveUnsupportedKindFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadUnsupportedMemberKindFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Unsupported-kind fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveUnsupportedKindFixtureProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/HotReloadUnsupportedMemberKindFixtures.cs";
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

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment))
                {
                    return index + 1;
                }
            }

            return -1;
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

        private static string FormatSkippedMethodNames(TransformWorkerSkippedDto[] skipped)
        {
            if (skipped == null || skipped.Length == 0)
            {
                return "(none)";
            }

            List<string> names = new List<string>(skipped.Length);
            foreach (TransformWorkerSkippedDto entry in skipped)
            {
                names.Add(entry.method);
            }

            return string.Join(", ", names);
        }

        /// <summary>
        /// What: worker System.Text.Json may omit null nested arrays while Unity Newtonsoft
        /// deserializes omitted fields as null; empty arrays round-trip as empty. Client coalesce
        /// must turn both into non-null empty arrays so later readers never see null.
        /// </summary>
        [Test]
        public void Deserialize_RemovedMembersAndCalledAddedMethodKeys_NullAndEmptyRoundTrip()
        {
            string omittedJson =
                "{\"shimSource\":\"\",\"entries\":[{\"methodName\":\"Caller\"}],\"skipped\":[],\"parseErrors\":[]}";
            TransformWorkerOutputDto omitted =
                JsonConvert.DeserializeObject<TransformWorkerOutputDto>(omittedJson);
            Assert.That(omitted.removedMembers, Is.Null, "Omitted removedMembers must deserialize as null.");
            Assert.That(omitted.addedFieldNames, Is.Null, "Omitted addedFieldNames must deserialize as null.");
            Assert.That(
                omitted.entries[0].calledAddedMethodKeys,
                Is.Null,
                "Omitted calledAddedMethodKeys must deserialize as null.");

            TransformWorkerClient.CoalesceOutput(omitted);
            Assert.That(omitted.removedMembers, Is.Not.Null);
            Assert.That(omitted.removedMembers, Is.Empty);
            Assert.That(omitted.removedMethodSignatures, Is.Not.Null);
            Assert.That(omitted.removedMethodSignatures, Is.Empty);
            Assert.That(omitted.addedFieldNames, Is.Not.Null);
            Assert.That(omitted.addedFieldNames, Is.Empty);
            string nullNamesJson = "{\"shimSource\":\"\",\"addedFieldNames\":null}";
            TransformWorkerOutputDto nullNames =
                JsonConvert.DeserializeObject<TransformWorkerOutputDto>(nullNamesJson);
            Assert.That(nullNames.addedFieldNames, Is.Null);
            TransformWorkerClient.CoalesceOutput(nullNames);
            Assert.That(nullNames.addedFieldNames, Is.Not.Null);
            Assert.That(nullNames.addedFieldNames, Is.Empty);
            Assert.That(omitted.entries[0].calledAddedMethodKeys, Is.Not.Null);
            Assert.That(omitted.entries[0].calledAddedMethodKeys, Is.Empty);
            Assert.That(omitted.declarationDriftWarnings, Is.Not.Null);
            Assert.That(omitted.unchangedMethods, Is.Not.Null);
            Assert.That(omitted.parseErrors, Is.Not.Null);
            Assert.That(omitted.skipped, Is.Not.Null);
            Assert.That(omitted.entries[0].parameterTypeFullNames, Is.Not.Null);
            Assert.That(omitted.shimSource, Is.Not.Null);

            string emptyJson =
                "{\"shimSource\":\"\",\"entries\":[{\"methodName\":\"Caller\",\"calledAddedMethodKeys\":[]}],"
                + "\"removedMembers\":[]}";
            TransformWorkerOutputDto empty = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(emptyJson);
            Assert.That(empty.removedMembers, Is.Not.Null);
            Assert.That(empty.removedMembers.Length, Is.EqualTo(0));
            Assert.That(empty.entries[0].calledAddedMethodKeys, Is.Not.Null);
            Assert.That(empty.entries[0].calledAddedMethodKeys.Length, Is.EqualTo(0));

            string nestedJson =
                "{\"removedMembers\":[{\"kind\":\"method\",\"name\":\"Gone\"}],"
                + "\"entries\":[{\"methodName\":\"Caller\",\"calledAddedMethodKeys\":[\"T::Added()\"]}]}";
            TransformWorkerOutputDto nested = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(nestedJson);
            Assert.That(nested.removedMembers.Length, Is.EqualTo(1));
            Assert.That(nested.removedMembers[0].kind, Is.EqualTo("method"));
            Assert.That(nested.removedMembers[0].name, Is.EqualTo("Gone"));
            Assert.That(nested.entries[0].calledAddedMethodKeys, Is.EqualTo(new[] { "T::Added()" }));
        }

        /// <summary>
        /// What: omitted hasAddedFieldRewrites deserializes as false, and an explicit true
        /// survives Newtonsoft so isolation retry can inject the store assembly.
        /// </summary>
        [Test]
        public void Deserialize_HasAddedFieldRewrites_OmittedFalseAndTrueRoundTrip()
        {
            string omittedJson = "{\"shimSource\":\"\",\"entries\":[],\"skipped\":[],\"parseErrors\":[]}";
            TransformWorkerOutputDto omitted =
                JsonConvert.DeserializeObject<TransformWorkerOutputDto>(omittedJson);
            Assert.That(omitted.hasAddedFieldRewrites, Is.False);

            string trueJson =
                "{\"shimSource\":\"\",\"entries\":[],\"skipped\":[],\"parseErrors\":[],"
                + "\"hasAddedFieldRewrites\":true}";
            TransformWorkerOutputDto enabled =
                JsonConvert.DeserializeObject<TransformWorkerOutputDto>(trueJson);
            Assert.That(enabled.hasAddedFieldRewrites, Is.True);
        }
    }
}
