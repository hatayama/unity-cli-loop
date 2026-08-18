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
    }
}
