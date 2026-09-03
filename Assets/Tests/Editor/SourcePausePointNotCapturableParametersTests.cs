using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using Mono.Cecil;
using Mono.Cecil.Cil;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that parameters excluded from capture are reported by name with the reason their
    /// type cannot be boxed, against the real Roslyn IL emitted for the editor test assembly.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointNotCapturableParametersTests
    {
        private const string AssemblyRelativePath = "Library/ScriptAssemblies/UnityCLILoop.Tests.Editor.dll";
        private const string FixtureTypeName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.SourcePausePointNotCapturableParameterFixture";

        /// <summary>
        /// What: the Cecil path names every ref/out/in and ref-struct parameter of the real
        /// compiled fixture, in declaration order, with the reason each cannot be boxed.
        /// </summary>
        [Test]
        public void CollectNotCapturableParameters_RealAssemblyByRefAndRefStruct_NamesEachWithReason()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            MethodDefinition method = FindFixtureMethod(assembly, "Combine");

            List<string> notCapturable =
                SourcePausePointCaptureEligibility.CollectNotCapturableParameters(method);

            Assert.That(notCapturable, Is.EqualTo(ExpectedCombineNotCapturableNames()));
        }

        /// <summary>
        /// What: the Cecil not-capturable names stay disjoint from the captured parameters, so
        /// the same parameter never appears in both lists.
        /// </summary>
        [Test]
        public void CollectParameters_RealAssemblyByRefAndRefStruct_KeepsOnlyTheCapturableParameter()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            MethodDefinition method = FindFixtureMethod(assembly, "Combine");

            List<SourcePausePointParameter> parameters =
                SourcePausePointCaptureEligibility.CollectParameters(method);

            Assert.That(parameters.Select(parameter => parameter.Name), Is.EqualTo(new[] { "value" }));
        }

        /// <summary>
        /// What: a Cecil method whose parameters are all capturable reports an empty list.
        /// </summary>
        [Test]
        public void CollectNotCapturableParameters_RealAssemblyCapturableParametersOnly_ReturnsEmpty()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            MethodDefinition method = FindFixtureMethod(assembly, "Add");

            List<string> notCapturable =
                SourcePausePointCaptureEligibility.CollectNotCapturableParameters(method);

            Assert.That(notCapturable, Is.Empty);
        }

        /// <summary>
        /// What: the reflection path reports the same names and reasons as the Cecil path for the
        /// same method, so shim-resolved markers do not disagree with compiled-resolved ones.
        /// </summary>
        [Test]
        public void CollectNotCapturableParametersFromReflection_ByRefAndRefStruct_NamesEachWithReason()
        {
            MethodBase method = FindReflectionFixtureMethod("Combine");

            List<string> notCapturable =
                SourcePausePointCaptureEligibility.CollectNotCapturableParametersFromReflection(
                    method,
                    skipFirstParameter: false);

            Assert.That(notCapturable, Is.EqualTo(ExpectedCombineNotCapturableNames()));
        }

        /// <summary>
        /// What: the reflection not-capturable names stay disjoint from the captured parameters.
        /// </summary>
        [Test]
        public void CollectParametersFromReflection_ByRefAndRefStruct_KeepsOnlyTheCapturableParameter()
        {
            MethodBase method = FindReflectionFixtureMethod("Combine");

            List<SourcePausePointParameter> parameters =
                SourcePausePointCaptureEligibility.CollectParametersFromReflection(
                    method,
                    skipFirstParameter: false);

            Assert.That(parameters.Select(parameter => parameter.Name), Is.EqualTo(new[] { "value" }));
        }

        /// <summary>
        /// What: skipFirstParameter drops the leading delegation argument from the reflection
        /// not-capturable list the same way it drops it from the captured list.
        /// </summary>
        [Test]
        public void CollectNotCapturableParametersFromReflection_SkipFirstParameter_SkipsTheLeadingParameter()
        {
            MethodBase method = FindReflectionFixtureMethod("Combine");

            List<string> notCapturable =
                SourcePausePointCaptureEligibility.CollectNotCapturableParametersFromReflection(
                    method,
                    skipFirstParameter: true);

            // "value" is the skipped leading parameter and was capturable anyway, so the list is
            // unchanged; the assertion pins that skipping never shifts or renames the entries.
            Assert.That(notCapturable, Is.EqualTo(ExpectedCombineNotCapturableNames()));
        }

        /// <summary>
        /// What: a reflection method whose parameters are all capturable reports an empty list.
        /// </summary>
        [Test]
        public void CollectNotCapturableParametersFromReflection_CapturableParametersOnly_ReturnsEmpty()
        {
            MethodBase method = FindReflectionFixtureMethod("Add");

            List<string> notCapturable =
                SourcePausePointCaptureEligibility.CollectNotCapturableParametersFromReflection(
                    method,
                    skipFirstParameter: false);

            Assert.That(notCapturable, Is.Empty);
        }

        /// <summary>
        /// What: a pointer type reports the pointer reason on the reflection path. Pointer
        /// parameters cannot be declared in this assembly (unsafe code is off), so the reason
        /// mapper is exercised directly.
        /// </summary>
        [Test]
        public void DescribeNotCapturableReasonFromReflection_PointerType_ReturnsPointerReason()
        {
            string reason = SourcePausePointCaptureEligibility.DescribeNotCapturableReasonFromReflection(
                typeof(int).MakePointerType());

            Assert.That(reason, Is.EqualTo(SourcePausePointConstants.NotCapturablePointerReason));
        }

        /// <summary>
        /// What: a capturable type reports no reason on the reflection path.
        /// </summary>
        [Test]
        public void DescribeNotCapturableReasonFromReflection_CapturableType_ReturnsEmpty()
        {
            string reason = SourcePausePointCaptureEligibility.DescribeNotCapturableReasonFromReflection(typeof(int));

            Assert.That(reason, Is.Empty);
        }

        /// <summary>
        /// What: a Cecil pointer type reports the pointer reason, covering the branch the
        /// compiled fixture cannot reach without unsafe code.
        /// </summary>
        [Test]
        public void DescribeNotCapturableReason_CecilPointerType_ReturnsPointerReason()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            TypeReference pointerType = new PointerType(assembly.MainModule.TypeSystem.Int32);

            string reason = SourcePausePointCaptureEligibility.DescribeNotCapturableReason(pointerType);

            Assert.That(reason, Is.EqualTo(SourcePausePointConstants.NotCapturablePointerReason));
        }

        /// <summary>
        /// What: a capturable Cecil type reports no reason.
        /// </summary>
        [Test]
        public void DescribeNotCapturableReason_CecilCapturableType_ReturnsEmpty()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();

            string reason = SourcePausePointCaptureEligibility.DescribeNotCapturableReason(
                assembly.MainModule.TypeSystem.Int32);

            Assert.That(reason, Is.Empty);
        }

        private static string[] ExpectedCombineNotCapturableNames()
        {
            return new[]
            {
                "accumulator (" + SourcePausePointConstants.NotCapturableByRefParameterReason + ")",
                "doubled (" + SourcePausePointConstants.NotCapturableByRefParameterReason + ")",
                "multiplier (" + SourcePausePointConstants.NotCapturableByRefParameterReason + ")",
                "scratch (" + SourcePausePointConstants.NotCapturableRefStructReason + ")"
            };
        }

        private static MethodBase FindReflectionFixtureMethod(string methodName)
        {
            MethodInfo method = typeof(SourcePausePointNotCapturableParameterFixture).GetMethod(methodName);
            Debug.Assert(method != null, "The fixture must declare the requested method.");
            return method;
        }

        private static AssemblyDefinition ReadEditorTestAssembly()
        {
            // Unity's working directory is not guaranteed to be the project root, so anchor the
            // path on Assets instead of the process cwd.
            string projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            string assemblyPath = Path.Combine(projectRoot, AssemblyRelativePath);
            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider()
            };

            return AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
        }

        private static MethodDefinition FindFixtureMethod(AssemblyDefinition assembly, string methodName)
        {
            TypeDefinition fixture = assembly.MainModule.GetType(FixtureTypeName);
            Debug.Assert(fixture != null, "The compiled fixture type must exist.");
            return fixture.Methods.Single(method => method.Name == methodName);
        }
    }
}
