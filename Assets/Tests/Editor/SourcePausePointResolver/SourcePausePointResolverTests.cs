using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies file:line resolution against real compiled assemblies (frozen fixture scripts
    /// under Fixtures/, read back from Library/ScriptAssemblies via portable PDBs).
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointResolverTests
    {
        private const string FixturesDirectory = "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/";

        [Test]
        public void Resolve_NormalMethod_ResolvesLineWithLocalsAndParameters()
        {
            // Verifies a plain statement resolves to its own line with its local and parameters.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "NormalMethodFixture.cs", 10);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(10));
            Assert.That(result.Resolution.MethodDisplayName, Does.Contain("NormalMethodFixture").And.Contain("Add"));
            Assert.That(result.Resolution.IsStatic, Is.False);
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Is.EquivalentTo(new[] { "sum" }));
            Assert.That(result.Resolution.Locals.Single().TypeName, Is.EqualTo("System.Int32"));
            Assert.That(result.Resolution.Locals.Single().IsValueType, Is.True);
            Assert.That(result.Resolution.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "left", "right" }));
            Assert.That(result.Resolution.Parameters.Select(p => p.TypeName), Is.EqualTo(new[] { "System.Int32", "System.Int32" }));
            Assert.That(result.Resolution.Parameters.Select(p => p.IsValueType), Is.EqualTo(new[] { true, true }));
        }

        [Test]
        public void Resolve_ReferenceTypeLocalMethod_ReportsIsValueTypeFalse()
        {
            // Verifies a string (reference type) local/parameter is reported with IsValueType=false,
            // the counterpart to the int (value type) assertions in Resolve_NormalMethod above.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "ReferenceTypeLocalFixture.cs", 9);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Is.EquivalentTo(new[] { "message" }));
            Assert.That(result.Resolution.Locals.Single().IsValueType, Is.False);
            Assert.That(result.Resolution.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "label" }));
            Assert.That(result.Resolution.Parameters.Single().IsValueType, Is.False);
        }

        [Test]
        public void Resolve_BranchingMethod_WhenLineHasNoSequencePoint_RoundsForwardToNextLine()
        {
            // Verifies a comment-only line (no sequence point) rounds forward to the next statement.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "BranchingMethodFixture.cs", 9);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(10));
            Assert.That(result.Resolution.MethodDisplayName, Does.Contain("Classify"));
        }

        [Test]
        public void Resolve_TryFinallyMethod_InTryBlock_IncludesLocalDeclaredBeforeTry()
        {
            // Verifies a local declared in the method's root scope is visible inside a nested try block.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "TryFinallyMethodFixture.cs", 14);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(14));
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Does.Contain("result"));
            Assert.That(result.Resolution.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "numerator", "denominator" }));
        }

        [Test]
        public void Resolve_TryFinallyMethod_InFinallyBlock_IncludesLocalDeclaredBeforeTry()
        {
            // Verifies a local declared in the method's root scope is visible inside the finally handler too.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "TryFinallyMethodFixture.cs", 18);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(18));
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Does.Contain("result"));
        }

        [Test]
        public void Resolve_AsyncMethod_RedirectsToTheCompilerGeneratedStateMachineMoveNext()
        {
            // Verifies a line after 'await' resolves inside the state machine's MoveNext, not the async method itself.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "AsyncMethodFixture.cs", 13);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(13));
            Assert.That(result.Resolution.MethodDisplayName, Does.Contain("ComputeAsync").And.Contain("MoveNext"));
            Assert.That(result.Resolution.IsStatic, Is.False);
        }

        [Test]
        public void Resolve_CoroutineMethod_RedirectsToTheCompilerGeneratedStateMachineMoveNext()
        {
            // Verifies a line after 'yield return' resolves inside the iterator state machine's MoveNext.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "CoroutineMethodFixture.cs", 13);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(13));
            Assert.That(result.Resolution.MethodDisplayName, Does.Contain("CountUp").And.Contain("MoveNext"));
            Assert.That(result.Resolution.IsStatic, Is.False);
        }

        [Test]
        public void Resolve_LocalFunction_RedirectsToTheCompilerGeneratedLocalFunctionMethod()
        {
            // Verifies a line inside a static local function resolves to its own compiler-generated method, not the outer method.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "LocalFunctionMethodFixture.cs", 13);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(13));
            Assert.That(result.Resolution.MethodDisplayName, Does.Contain("Compute"));
            Assert.That(result.Resolution.IsStatic, Is.True);
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Is.EquivalentTo(new[] { "squared" }));
            Assert.That(result.Resolution.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void Resolve_RefStructAndByRefMethod_ExcludesNonCapturableLocalsAndParameters()
        {
            // Verifies ref-struct locals (user-defined, generic user-defined, and Span<T>) and
            // ref/out/in parameters are excluded, since none of them can be boxed for capture.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "RefStructAndByRefFixture.cs", 28);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.Locals.Select(l => l.Name), Is.EquivalentTo(new[] { "result" }));
            Assert.That(result.Resolution.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "value" }));
        }

        /// <summary>
        /// Verifies a Debug.Assert call spanning several physical lines pins EndLine to the closing parenthesis line.
        /// </summary>
        [Test]
        public void Resolve_MultiLineAssert_ReportsResolvedEndLineAfterResolvedLine()
        {
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "MultiLineAssertFixture.cs", 11);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(11));
            Assert.That(result.Resolution.ResolvedEndLine, Is.EqualTo(13));
        }

        /// <summary>
        /// What: the compiled span covers only the resolved method, not a sibling method
        /// in the same file.
        /// </summary>
        [Test]
        public void Resolve_CompiledMethodSpan_PinsStartAndEndOfResolvedMethodOnly()
        {
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "CompiledMethodSpanFixture.cs", 9);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Resolution.ResolvedLine, Is.EqualTo(9));
            Assert.That(result.Resolution.CompiledMethodStartLine, Is.EqualTo(8));
            Assert.That(result.Resolution.CompiledMethodEndLine, Is.EqualTo(11));
        }

        [Test]
        public void Resolve_WhenPathIsOutsideAnyScriptFolder_ReturnsScriptNotInAnyAssemblyFailure()
        {
            // Verifies a path outside Assets/Packages (no owning assembly) is reported with a specific
            // failure reason instead of throwing. CompilationPipeline maps paths to assemblies by folder
            // rule, not by file existence, so a path *inside* a known folder still resolves even if the
            // file itself is missing (see Resolve_WhenLineIsBeyondAllSequencePoints_* below).
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                "NotAUnityScriptFolder/Foo.cs", 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointResolveFailureReason.ScriptNotInAnyAssembly));
        }

        [Test]
        public void Resolve_WhenLineIsBeyondAllSequencePoints_ReturnsNoSequencePointFailure()
        {
            // Verifies a line past the end of the file's methods is reported with a specific failure reason.
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                FixturesDirectory + "NormalMethodFixture.cs", 9999);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine));
        }

        /// <summary>
        /// What: a line past every sequence point in the file lists the last compiled method
        /// span in that document as nearby recovery material.
        /// </summary>
        [Test]
        public void Resolve_WhenLineIsBeyondAllSequencePoints_IncludesNearbyCompiledMethodSpans()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(file, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 9999);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine));
            Assert.That(result.NearbyCompiledMethods.Count, Is.EqualTo(1));
            Assert.That(
                result.NearbyCompiledMethods[0].DisplayName,
                Is.EqualTo("CompiledMethodSpanFixture.OtherMethod"));
            Assert.That(
                result.NearbyCompiledMethods[0].StartLine,
                Is.EqualTo(otherMethod.Resolution.CompiledMethodStartLine));
            Assert.That(
                result.NearbyCompiledMethods[0].EndLine,
                Is.EqualTo(otherMethod.Resolution.CompiledMethodEndLine));
        }

        /// <summary>
        /// What: when three or more compiled methods contain the requested line, nearby
        /// recovery material is capped at two.
        /// </summary>
        [Test]
        public void FindNearbyCompiledMethods_WhenMoreThanTwoMethodsContainTheLine_ReturnsAtMostTwo()
        {
            const int line = 9;
            string file = FixturesDirectory + "NearbyContainingMethodsFixture.cs";

            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearby =
                SourcePausePointResolver.FindNearbyCompiledMethodsInFile(file, line);

            Assert.That(
                nearby.Count,
                Is.EqualTo(2),
                string.Join(", ", nearby.Select(method => method.DisplayName)));
            Assert.That(nearby[0].StartLine, Is.LessThanOrEqualTo(line));
            Assert.That(nearby[0].EndLine, Is.GreaterThanOrEqualTo(line));
            Assert.That(nearby[1].StartLine, Is.LessThanOrEqualTo(line));
            Assert.That(nearby[1].EndLine, Is.GreaterThanOrEqualTo(line));
        }

        /// <summary>
        /// What: a simple --method name keeps resolve inside that compiled method.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesSimpleName_ResolvesThatMethod()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 9, "Target");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 9);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: a Type.Method filter matches the declaring-type short name plus method name.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesQualifiedName_ResolvesThatMethod()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                file, 9, "CompiledMethodSpanFixture.Target");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 9);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: a --method name that has no sequence point on or after the line fails instead
        /// of silently arming a neighbor, and still lists nearby compiled spans.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterHasNoSequencePointOnOrAfterLine_FailsWithNamedMessage()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(file, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 16, "Target");

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine));
            string expectedMessage =
                string.Format(
                    SourcePausePointConstants.NoMethodNamedWithSequencePointMessageFormat,
                    "Target",
                    16);
            Assert.That(result.ErrorMessage, Is.EqualTo(expectedMessage));
            Assert.That(result.NearbyCompiledMethods.Count, Is.EqualTo(1));
            Assert.That(
                result.NearbyCompiledMethods[0].DisplayName,
                Is.EqualTo("CompiledMethodSpanFixture.OtherMethod"));
            Assert.That(
                result.NearbyCompiledMethods[0].StartLine,
                Is.EqualTo(otherMethod.Resolution.CompiledMethodStartLine));
            Assert.That(
                result.NearbyCompiledMethods[0].EndLine,
                Is.EqualTo(otherMethod.Resolution.CompiledMethodEndLine));
        }

        /// <summary>
        /// What: FindCompiledMethodSpans returns the named method's compiled span, not a neighbor.
        /// </summary>
        [Test]
        public void FindCompiledMethodSpans_WhenMethodFilterMatches_ReturnsThatMethodSpan()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 9, "Target");
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans =
                SourcePausePointResolver.FindCompiledMethodSpans(file, "Target");

            Assert.That(spans.Count, Is.EqualTo(1));
            Assert.That(spans[0].StartLine, Is.EqualTo(expected.Resolution.CompiledMethodStartLine));
            Assert.That(spans[0].EndLine, Is.EqualTo(expected.Resolution.CompiledMethodEndLine));
        }

        /// <summary>
        /// What: FindCompiledMethodSpans with no --method returns no spans.
        /// </summary>
        [Test]
        public void FindCompiledMethodSpans_WhenMethodFilterIsEmpty_ReturnsNoSpans()
        {
            string file = FixturesDirectory + "CompiledMethodSpanFixture.cs";

            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans =
                SourcePausePointResolver.FindCompiledMethodSpans(file, string.Empty);

            Assert.That(spans, Is.Empty);
        }

        /// <summary>
        /// What: an empty method filter accepts every compiled method name.
        /// </summary>
        [Test]
        public void MethodMatchesFilter_WhenFilterIsEmpty_ReturnsTrue()
        {
            Assert.That(SourcePausePointResolver.MethodMatchesFilter(null, "Target", "Host"), Is.True);
            Assert.That(SourcePausePointResolver.MethodMatchesFilter(string.Empty, "Target", "Host"), Is.True);
        }

        /// <summary>
        /// What: a simple name matches method.Name only, and a dotted name matches Type.Method.
        /// </summary>
        [Test]
        public void MethodMatchesFilter_WhenSimpleOrQualified_UsesDeclaringTypeShortName()
        {
            Assert.That(SourcePausePointResolver.MethodMatchesFilter("Target", "Target", "Host"), Is.True);
            Assert.That(SourcePausePointResolver.MethodMatchesFilter("Other", "Target", "Host"), Is.False);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter("Host.Target", "Target", "Host"),
                Is.True);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter("Other.Target", "Target", "Host"),
                Is.False);
        }

        /// <summary>
        /// What: --method ComputeAsync matches the async state-machine MoveNext body.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesAsyncSimpleName_ResolvesStateMachine()
        {
            string file = FixturesDirectory + "AsyncMethodFixture.cs";
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 13);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 13, "ComputeAsync");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: --method Type.ComputeAsync matches the async state-machine MoveNext body.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesAsyncQualifiedName_ResolvesStateMachine()
        {
            string file = FixturesDirectory + "AsyncMethodFixture.cs";
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 13);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                file, 13, "AsyncMethodFixture.ComputeAsync");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: an unrelated --method on an async body fails instead of matching MoveNext.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterDoesNotMatchAsync_Fails()
        {
            string file = FixturesDirectory + "AsyncMethodFixture.cs";
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 13, "CountUp");

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.NoMethodNamedWithSequencePointMessageFormat,
                        "CountUp",
                        13)));
        }

        /// <summary>
        /// What: --method CountUp matches the iterator state-machine MoveNext body.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesCoroutineSimpleName_ResolvesStateMachine()
        {
            string file = FixturesDirectory + "CoroutineMethodFixture.cs";
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 13);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(file, 13, "CountUp");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: --method Type.CountUp matches the iterator state-machine MoveNext body.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterMatchesCoroutineQualifiedName_ResolvesStateMachine()
        {
            string file = FixturesDirectory + "CoroutineMethodFixture.cs";
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(file, 13);
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                file, 13, "CoroutineMethodFixture.CountUp");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Resolution.MethodDisplayName,
                Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: an unrelated --method on a coroutine body fails instead of matching MoveNext.
        /// </summary>
        [Test]
        public void Resolve_WhenMethodFilterDoesNotMatchCoroutine_Fails()
        {
            string file = FixturesDirectory + "CoroutineMethodFixture.cs";
            SourcePausePointResolveResult result = SourcePausePointResolver.Resolve(
                file, 13, "ComputeAsync");

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.NoMethodNamedWithSequencePointMessageFormat,
                        "ComputeAsync",
                        13)));
        }

        /// <summary>
        /// What: a state-machine type name is compared as the logical method and outer type.
        /// </summary>
        [Test]
        public void MethodMatchesFilter_WhenStateMachineType_UsesLogicalOwnerNames()
        {
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "ComputeAsync",
                    "MoveNext",
                    "<ComputeAsync>d__0",
                    "AsyncMethodFixture"),
                Is.True);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "AsyncMethodFixture.ComputeAsync",
                    "MoveNext",
                    "<ComputeAsync>d__0",
                    "AsyncMethodFixture"),
                Is.True);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "CountUp",
                    "MoveNext",
                    "<ComputeAsync>d__0",
                    "AsyncMethodFixture"),
                Is.False);
        }

        /// <summary>
        /// What: a local-function mangled name is compared as the logical local name.
        /// </summary>
        [Test]
        public void MethodMatchesFilter_WhenLocalFunction_UsesLogicalLocalName()
        {
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "Compute",
                    "<Square>g__Compute|8_0",
                    "LocalFunctionMethodFixture"),
                Is.True);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "LocalFunctionMethodFixture.Compute",
                    "<Square>g__Compute|8_0",
                    "LocalFunctionMethodFixture"),
                Is.True);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "Square",
                    "<Square>g__Compute|8_0",
                    "LocalFunctionMethodFixture"),
                Is.False);
        }

        /// <summary>
        /// What: an anonymous lambda keeps no source name, so --method does not match it.
        /// </summary>
        [Test]
        public void MethodMatchesFilter_WhenLambda_DoesNotMatchOuterMethodName()
        {
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "CountUp",
                    "<CountUp>b__0",
                    "CoroutineMethodFixture"),
                Is.False);
            Assert.That(
                SourcePausePointResolver.MethodMatchesFilter(
                    "CoroutineMethodFixture.CountUp",
                    "<CountUp>b__0",
                    "CoroutineMethodFixture"),
                Is.False);
        }
    }
}
