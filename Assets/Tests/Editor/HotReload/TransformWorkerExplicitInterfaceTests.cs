using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for edited explicit interface method implementations: an implementation the
    /// compiled type already has becomes a transplant entry named by its compiled metadata name,
    /// with a legal shim identifier, whatever spelling the edit gives the interface.
    /// </summary>
    public class TransformWorkerExplicitInterfaceTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadExplicitInterfaceFixtures.cs";
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string RunnerBody = "int IHotReloadExplicitRunner.Run() { return 8101; }";
        private const string ValueSpecifier = "int IHotReloadExplicitValue.Value";
        private const string HiddenFixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadHiddenExplicitInterfaceFixtures.cs";

        /// <summary>
        /// What: an edited explicit implementation of a namespaced interface is an entry named by the
        /// compiled metadata name, with a shim named after the interface member.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementation_EmitsEntryWithCompiledNameAndLegalShimName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitRunner.cs",
                source => ReplaceUniqueFragment(source, "8101", "9101"));

            AssertEntry(result, typeof(HotReloadExplicitRunnerFixture), Namespace + ".IHotReloadExplicitRunner.Run", "Run");
        }

        /// <summary>What: an edited explicit implementation of a closed generic interface is an entry.</summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfGenericInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitBox.cs",
                source => ReplaceUniqueFragment(source, "8102", "9102"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitBoxFixture),
                Namespace + ".IHotReloadExplicitBox<System.Int32>.Take",
                "Take");
        }

        /// <summary>
        /// What: an edited explicit implementation of a nested interface is an entry whose name joins
        /// the outer and nested type with a dot, as the compiler emits it.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfNestedInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitNested.cs",
                source => ReplaceUniqueFragment(source, "8103", "9103"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitNestedFixture),
                Namespace + ".HotReloadExplicitOuter.IInner.Run",
                "Run");
        }

        /// <summary>
        /// What: respelling the interface with global:: in the edit still yields the compiled name,
        /// which has no global:: prefix, and the respelling is not reported as an edit outside method
        /// bodies or as a removed member.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationRespelledWithGlobal_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGlobal.cs",
                source => ReplaceUniqueFragment(
                    source,
                    RunnerBody,
                    "int global::" + Namespace + ".IHotReloadExplicitRunner.Run() { return 9101; }"));

            AssertEntry(result, typeof(HotReloadExplicitRunnerFixture), Namespace + ".IHotReloadExplicitRunner.Run", "Run");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an explicit implementation whose edit only respells the interface with global:: is
        /// reported unchanged under the compiled name, with no entry and no warning, because only the
        /// spelling of its interface differs from the last compile.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationOnlyRespelledWithGlobal_IsUnchangedWithoutWarnings()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGlobalOnlyRespelled.cs",
                source => ReplaceUniqueFragment(
                    source,
                    RunnerBody,
                    "int global::" + Namespace + ".IHotReloadExplicitRunner.Run() { return 8101; }"));

            AssertNoEntryFor(result, typeof(HotReloadExplicitRunnerFixture));
            Assert.That(
                result.Output.unchangedMethods
                    .Where(row => row.typeMetadataName == typeof(HotReloadExplicitRunnerFixture).FullName)
                    .Select(row => row.methodName),
                Is.EqualTo(new[] { Namespace + ".IHotReloadExplicitRunner.Run" }));
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an explicit implementation moved to another interface that declares the same member
        /// is still reported as a removed member, because the two specifiers name different
        /// interfaces, not two spellings of one.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationMovedToAnotherInterface_StillReportsRemovedMember()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitMovedInterface.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, RunnerBody, "int IHotReloadExplicitA.Run() { return 8101; }"),
                    "public class HotReloadExplicitRunnerFixture : IHotReloadExplicitRunner",
                    "public class HotReloadExplicitRunnerFixture : IHotReloadExplicitA"));

            Assert.That(RemovedMemberLabels(result), Does.Contain("method:Run"));
        }

        /// <summary>
        /// What: an explicit implementation whose interface a retargeted using alias moves to another
        /// interface is still reported as a removed member when the edit also respells it, because
        /// the old spelling is bound where it was written, not where the new one stands.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationRetargetedByAliasAndRespelled_StillReportsRemovedMember()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitRetargetedInterface.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        source,
                        "HotReloadExplicitRetargetedInterface = " + Namespace + ".IHotReloadExplicitA;",
                        "HotReloadExplicitRetargetedInterface = " + Namespace + ".IHotReloadExplicitB;"),
                    "int HotReloadExplicitRetargetedInterface.Run()",
                    "int global::" + Namespace + ".IHotReloadExplicitB.Run()"));

            Assert.That(RemovedMemberLabels(result), Does.Contain("method:Run"));
        }

        /// <summary>
        /// What: an explicit implementation that a retargeted using alias moves to another overload of
        /// the same interface is still reported as a removed member when the edit also respells the
        /// interface, because the old argument type is bound where it was written.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationMovedToAnotherOverloadAndRespelled_StillReportsRemovedMember()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitRetargetedOverload.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        source,
                        "HotReloadExplicitRetargetedArgument = " + Namespace + ".HotReloadExplicitArgA;",
                        "HotReloadExplicitRetargetedArgument = " + Namespace + ".HotReloadExplicitArgB;"),
                    "int IHotReloadExplicitOverloaded.Take(",
                    "int global::" + Namespace + ".IHotReloadExplicitOverloaded.Take("));

            Assert.That(RemovedMemberLabels(result), Does.Contain("method:Take"));
        }

        /// <summary>
        /// What: an explicit property moved to another interface that declares a property of the same
        /// name is still reported as a removed compiled property, because the source implements a
        /// member of another interface.
        /// </summary>
        [Test]
        public async Task Run_ExplicitPropertyMovedToAnotherInterface_ReportsTheCompiledPropertyRemoved()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitPropertyMovedInterface.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        source,
                        "public class HotReloadExplicitArrowValueFixture : IHotReloadExplicitArrowValue",
                        "public class HotReloadExplicitArrowValueFixture : IHotReloadExplicitValue"),
                    "int IHotReloadExplicitArrowValue.Value => 8203;",
                    "int IHotReloadExplicitValue.Value { get => 8203; set { } }"));

            Assert.That(
                result.Output.files.SelectMany(file => file.declarationDriftWarnings ?? Array.Empty<string>()),
                Has.Some.Contains("IHotReloadExplicitArrowValue.Value' was removed"));
        }

        /// <summary>
        /// What: an explicit indexer and an explicit event whose edits only respell the interface with
        /// global:: are not reported as edits outside method bodies or as removed members.
        /// </summary>
        [TestCase("ExplicitIndexerRespelled.cs", "int ", "IHotReloadExplicitIndexed.this[int index]")]
        [TestCase("ExplicitEventRespelled.cs", "event Action ", "IHotReloadExplicitNotifier.Changed")]
        public async Task Run_ExplicitIndexerOrEventOnlyRespelledWithGlobal_ReportsNoWarning(
            string fileName,
            string declarationPrefix,
            string specifiedMember)
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                fileName,
                source => ReplaceUniqueFragment(
                    source,
                    declarationPrefix + specifiedMember,
                    declarationPrefix + "global::" + Namespace + "." + specifiedMember));

            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: respelling the interface through a using alias still yields the compiled name, and
        /// the respelled member is not reported as removed or named as an edit outside method bodies.
        /// The added using directive is such an edit, so the file may still be reported for it.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationRespelledWithAlias_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitAlias.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, RunnerBody, "int RunnerAlias.Run() { return 9101; }"),
                    "using System;",
                    "using System; using RunnerAlias = " + Namespace + ".IHotReloadExplicitRunner;"));

            AssertEntry(result, typeof(HotReloadExplicitRunnerFixture), Namespace + ".IHotReloadExplicitRunner.Run", "Run");
            Assert.That(RemovedMemberLabels(result), Is.Empty);
            Assert.That(
                result.Output.files.SelectMany(file => file.declarationDriftWarnings ?? Array.Empty<string>()),
                Has.None.Contains("method: "));
        }

        /// <summary>
        /// What: an explicit implementation whose body calls a base method is an entry whose shim calls
        /// the base-call stand-in.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationCallingBase_EmitsEntryWithStandIn()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitBaseCaller.cs",
                source => ReplaceUniqueFragment(source, "8108", "9108"));

            TransformWorkerEntryDto entry = AssertEntry(
                result,
                typeof(HotReloadExplicitBaseCallerFixture),
                Namespace + ".IHotReloadExplicitBaseCaller.Value",
                "Value");
            Assert.That(result.Output.shimSource, Does.Contain("__uloopBaseCall__d1__Compute__"), result.Output.shimSource);
            Assert.That(entry.patchKind, Is.EqualTo("transplant"));
        }

        /// <summary>
        /// What: an async explicit implementation whose lambda reads a private field is a delegation
        /// entry, so the delegation patch shape also resolves the compiled metadata name.
        /// </summary>
        [Test]
        public async Task Run_EditedAsyncExplicitImplementationReadingPrivateField_EmitsDelegationEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitAsync.cs",
                source => ReplaceUniqueFragment(source, "8107", "9107"));

            TransformWorkerEntryDto entry = AssertEntry(
                result,
                typeof(HotReloadExplicitAsyncFixture),
                Namespace + ".IHotReloadExplicitAsync.RunAsync",
                "RunAsync");
            Assert.That(entry.patchKind, Is.EqualTo("delegation"));
        }

        /// <summary>
        /// What: a synchronous explicit implementation whose lambda reads a private field is a
        /// transplant entry that binds closure accessors.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationWithClosureOverPrivateField_EmitsTransplantWithAccessors()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitClosure.cs",
                source => ReplaceUniqueFragment(source, "8109", "9109"));

            TransformWorkerEntryDto entry = AssertEntry(
                result,
                typeof(HotReloadExplicitClosureFixture),
                Namespace + ".IHotReloadExplicitClosure.Read",
                "Read");
            Assert.That(entry.patchKind, Is.EqualTo("transplant"));
            Assert.That(result.Output.shimSource, Does.Contain("_seed"), result.Output.shimSource);
        }

        /// <summary>
        /// What: an edited explicit implementation on a generic class stays Skipped with the generic
        /// reason and emits no entry.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOnGenericClass_IsSkippedAsGeneric()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGenericHost.cs",
                source => ReplaceUniqueFragment(source, "8110", "9110"));

            AssertSkippedAsGeneric(result, "HotReloadExplicitGenericHostFixture");
        }

        /// <summary>
        /// What: an edited generic explicit method stays Skipped with the generic reason and emits no
        /// entry.
        /// </summary>
        [Test]
        public async Task Run_EditedGenericExplicitMethod_IsSkippedAsGeneric()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGenericMethod.cs",
                source => ReplaceUniqueFragment(source, "8111", "9111"));

            AssertSkippedAsGeneric(result, "HotReloadExplicitGenericMethodFixture");
        }

        private static void AssertSkippedAsGeneric(TransformWorkerClientResult result, string fixtureTypeName)
        {
            Assert.That(
                result.Output.entries.Any(entry => entry.typeMetadataName.Contains(fixtureTypeName)),
                Is.False,
                "A generic explicit implementation must not be emitted.");
            Assert.That(
                result.Output.skipped.Any(skipped =>
                    skipped.method.Contains(fixtureTypeName)
                    && skipped.reason?.code == HotReloadWorkerReasonCode.MethodTransformGenericMethodOrType),
                Is.True,
                "Skipped=" + FormatSkipped(result));
        }

        /// <summary>
        /// What: an unchanged explicit implementation spelled with global:: in both the baseline and
        /// the current source is reported unchanged under the compiled metadata name, the same name an
        /// entry for it carries, so restoring the body can revert its patch.
        /// </summary>
        [Test]
        public async Task Run_UnchangedExplicitImplementationRespelledWithGlobal_ReportsCompiledName()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string onDisk = File.ReadAllText(Path.Combine(
                projectRoot,
                FixtureProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string respelled = ReplaceUniqueFragment(
                onDisk,
                RunnerBody,
                "int global::" + Namespace + ".IHotReloadExplicitRunner.Run() { return 8101; }");
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "ExplicitGlobalUnchanged.cs");
            File.WriteAllText(sourcePath, respelled);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(sourcePath, respelled);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string[] runnerRows = result.Output.unchangedMethods
                .Where(row => row.typeMetadataName == typeof(HotReloadExplicitRunnerFixture).FullName)
                .Select(row => row.methodName)
                .ToArray();
            Assert.That(runnerRows, Is.EqualTo(new[] { Namespace + ".IHotReloadExplicitRunner.Run" }));
        }

        /// <summary>
        /// What: with the Editor's key for an explicit implementation excluded, a global:: respelled
        /// edit of it emits no entry, because the worker keys the method by its compiled name too.
        /// </summary>
        [Test]
        public async Task Run_RespelledExplicitImplementationExcludedByEditorKey_EmitsNoEntry()
        {
            string excludedKey = HotReloadMethodKeys.BuildMethodKeyParts(
                typeof(HotReloadExplicitRunnerFixture).FullName,
                Namespace + ".IHotReloadExplicitRunner.Run",
                Array.Empty<string>(),
                0);

            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGlobalExcluded.cs",
                source => ReplaceUniqueFragment(
                    source,
                    RunnerBody,
                    "int global::" + Namespace + ".IHotReloadExplicitRunner.Run() { return 9101; }"),
                new[] { excludedKey });

            Assert.That(
                result.Output.entries.Any(entry => entry.typeMetadataName == typeof(HotReloadExplicitRunnerFixture).FullName),
                Is.False,
                "The excluded explicit implementation must not be emitted.");
        }

        /// <summary>
        /// What: a global:: respelled explicit implementation that is Skipped is labelled with the
        /// compiled name, the label the Editor builds from the compiled method.
        /// </summary>
        [Test]
        public async Task Run_RespelledExplicitImplementationSkipped_LabelsRowWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitGlobalSkipped.cs",
                source => ReplaceUniqueFragment(
                    source,
                    "int IHotReloadExplicitBaseCaller.Value(int input) { return base.Compute(input) + 8108; }",
                    "int global::" + Namespace + ".IHotReloadExplicitBaseCaller.Value(int input) "
                    + "{ Func<int> call = () => base.Compute(input); return call() + 9108; }"));

            string expectedLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(HotReloadExplicitBaseCallerFixture).FullName),
                Namespace + ".IHotReloadExplicitBaseCaller.Value",
                new[] { "System.Int32" },
                0);
            Assert.That(
                result.Output.skipped.Select(skipped => skipped.method),
                Does.Contain(expectedLabel),
                FormatSkipped(result));
        }

        /// <summary>
        /// What: an edited getter of an explicit interface property is an entry named by the compiled
        /// accessor name, with no parameters and a shim named after the interface accessor.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitPropertyGetter_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitValueGetter.cs",
                source => ReplaceUniqueFragment(source, "8201", "9201"));

            TransformWorkerEntryDto entry = AssertEntry(
                result,
                typeof(HotReloadExplicitValueFixture),
                Namespace + ".IHotReloadExplicitValue.get_Value",
                "get_Value");
            Assert.That(entry.parameterTypeFullNames, Is.Empty);
        }

        /// <summary>
        /// What: an edited setter of an explicit interface property is an entry named by the compiled
        /// accessor name, whose single parameter is the property type.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitPropertySetter_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitValueSetter.cs",
                source => ReplaceUniqueFragment(source, "8202", "9202"));

            TransformWorkerEntryDto entry = AssertEntry(
                result,
                typeof(HotReloadExplicitValueFixture),
                Namespace + ".IHotReloadExplicitValue.set_Value",
                "set_Value");
            Assert.That(entry.parameterTypeFullNames, Is.EqualTo(new[] { "System.Int32" }));
        }

        /// <summary>What: an edited expression-bodied explicit property is a getter entry.</summary>
        [Test]
        public async Task Run_EditedExpressionBodiedExplicitProperty_EmitsGetterEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitArrowValue.cs",
                source => ReplaceUniqueFragment(source, "8203", "9203"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitArrowValueFixture),
                Namespace + ".IHotReloadExplicitArrowValue.get_Value",
                "get_Value");
        }

        /// <summary>What: an edited explicit property getter of a closed generic interface is an entry.</summary>
        [Test]
        public async Task Run_EditedExplicitPropertyGetterOfGenericInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitBoxValue.cs",
                source => ReplaceUniqueFragment(source, "8206", "9206"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitBoxValueFixture),
                Namespace + ".IHotReloadExplicitBoxValue<System.Int32>.get_Value",
                "get_Value");
        }

        /// <summary>
        /// What: an edited explicit property getter of a nested interface is an entry whose name joins
        /// the outer and nested type with a dot.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitPropertyGetterOfNestedInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitNestedValue.cs",
                source => ReplaceUniqueFragment(source, "8207", "9207"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitNestedValueFixture),
                Namespace + ".HotReloadExplicitValueOuter.IInnerValue.get_Value",
                "get_Value");
        }

        /// <summary>
        /// What: an explicit property respelled with global:: in the edit still yields a getter entry
        /// under the compiled name, leaves the unedited setter without an entry, and the respelling
        /// is not reported as an edit outside method bodies or as a removed member.
        /// </summary>
        [Test]
        public async Task Run_ExplicitPropertyRespelledWithGlobal_EmitsGetterEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitValueGlobal.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, "8201", "9201"),
                    ValueSpecifier,
                    "int global::" + Namespace + ".IHotReloadExplicitValue.Value"));

            AssertEntry(
                result,
                typeof(HotReloadExplicitValueFixture),
                Namespace + ".IHotReloadExplicitValue.get_Value",
                "get_Value");
            Assert.That(
                result.Output.entries.Where(entry => entry.typeMetadataName == typeof(HotReloadExplicitValueFixture).FullName)
                    .Select(entry => entry.methodName),
                Has.None.EqualTo(Namespace + ".IHotReloadExplicitValue.set_Value"));
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an unchanged explicit property spelled with global:: in both the baseline and the
        /// current source reports its getter and setter unchanged under the compiled names.
        /// </summary>
        [Test]
        public async Task Run_UnchangedExplicitPropertyRespelledWithGlobal_ReportsCompiledNames()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string onDisk = File.ReadAllText(Path.Combine(
                projectRoot,
                FixtureProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string respelled = ReplaceUniqueFragment(
                onDisk,
                ValueSpecifier,
                "int global::" + Namespace + ".IHotReloadExplicitValue.Value");
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "ExplicitValueGlobalUnchanged.cs");
            File.WriteAllText(sourcePath, respelled);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(sourcePath, respelled);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string[] valueRows = result.Output.unchangedMethods
                .Where(row => row.typeMetadataName == typeof(HotReloadExplicitValueFixture).FullName)
                .Select(row => row.methodName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(
                valueRows,
                Is.EqualTo(new[]
                {
                    Namespace + ".IHotReloadExplicitValue.get_Value",
                    Namespace + ".IHotReloadExplicitValue.set_Value"
                }));
        }

        /// <summary>
        /// What: with the Editor's key for an explicit property getter excluded, a global:: respelled
        /// edit of it emits no entry, because the worker keys the getter by its compiled name too, and
        /// the unedited setter emits none either.
        /// </summary>
        [Test]
        public async Task Run_RespelledExplicitPropertyGetterExcludedByEditorKey_EmitsNoEntry()
        {
            string excludedKey = HotReloadMethodKeys.BuildMethodKeyParts(
                typeof(HotReloadExplicitValueFixture).FullName,
                Namespace + ".IHotReloadExplicitValue.get_Value",
                Array.Empty<string>(),
                0);

            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitValueGlobalExcluded.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, "8201", "9201"),
                    ValueSpecifier,
                    "int global::" + Namespace + ".IHotReloadExplicitValue.Value"),
                new[] { excludedKey });

            // Why no entry at all: the getter is excluded, and the setter is unedited once the respelled
            // specifier pairs with the snapshot.
            AssertNoEntryFor(result, typeof(HotReloadExplicitValueFixture));
        }

        /// <summary>
        /// What: an edited init accessor of an explicit property stays Skipped as an unsupported
        /// accessor and emits no entry.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitPropertyInitAccessor_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitInitValue.cs",
                source => ReplaceUniqueFragment(source, "8205", "9205"));

            AssertNoEntryFor(result, typeof(HotReloadExplicitInitValueFixture));
            AssertSkippedWith(
                result,
                Namespace + ".IHotReloadExplicitInitValue.set_Value",
                HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor);
        }

        /// <summary>
        /// What: a setter added by the edit to an explicit read-only property is Skipped and emits no
        /// entry, because the compiled type has no setter to patch.
        /// </summary>
        [Test]
        public async Task Run_SetterAddedToExplicitReadOnlyProperty_IsSkippedWithoutEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitReadOnlyAddsSetter.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(source, "int Reading { get; }", "int Reading { get; set; }"),
                    "get { return 8204; }",
                    "get { return 8204; } set { }"));

            AssertNoEntryFor(result, typeof(HotReloadExplicitReadOnlyValueFixture));
            Assert.That(
                result.Output.skipped.Any(skipped => skipped.method.Contains("IHotReloadExplicitReadOnlyValue.set_Reading", StringComparison.Ordinal)),
                Is.True,
                FormatSkipped(result));
        }

        /// <summary>
        /// What: a getter added by the edit to an explicit write-only property is Skipped with the
        /// explicit-implementation reason and emits no entry, because the compiled property has no
        /// getter; the other edits of the file still apply.
        /// </summary>
        [Test]
        public async Task Run_GetterAddedToExplicitSetOnlyProperty_IsSkippedWithoutEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitSetOnlyAddsGetter.cs",
                source => ReplaceUniqueFragment(
                    ReplaceUniqueFragment(
                        ReplaceUniqueFragment(source, "int Written { set; }", "int Written { get; set; }"),
                        "set { _stored = value + 8208; }",
                        "set { _stored = value + 8208; } get { return _stored; }"),
                    "8101",
                    "9101"));

            AssertNoEntryFor(result, typeof(HotReloadExplicitSetOnlyValueFixture));
            AssertSkippedWith(
                result,
                Namespace + ".IHotReloadExplicitSetOnlyValue.get_Written",
                HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation);
            AssertEntry(result, typeof(HotReloadExplicitRunnerFixture), Namespace + ".IHotReloadExplicitRunner.Run", "Run");
        }

        /// <summary>
        /// What: a getter added by the edit to an ordinary write-only property is Skipped as an
        /// unsupported accessor and emits no entry, because the compiled property has no getter for
        /// the Editor to find.
        /// </summary>
        [Test]
        public async Task Run_GetterAddedToOrdinarySetOnlyProperty_IsSkippedWithoutEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "OrdinarySetOnlyAddsGetter.cs",
                source => ReplaceUniqueFragment(
                    source,
                    "set { _stored = value + 8209; }",
                    "get { return _stored; } set { _stored = value + 8209; }"));

            AssertNoEntryFor(result, typeof(HotReloadOrdinarySetOnlyValueFixture));
            AssertSkippedWith(result, "get_OrdinaryWritten", HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor);
        }

        /// <summary>
        /// What: an edited accessor of an explicit interface indexer stays Skipped with the
        /// explicit-implementation reason and emits no entry.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitIndexerAccessor_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedFixtureAsync(
                "ExplicitIndexed.cs",
                source => ReplaceUniqueFragment(source, "8210", "9210"));

            AssertNoEntryFor(result, typeof(HotReloadExplicitIndexedFixture));
            AssertSkippedWith(
                result,
                Namespace + ".IHotReloadExplicitIndexed.get_Item",
                HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation);
        }

        /// <summary>
        /// What: editing only a class beside explicit implementations of internal interfaces that the
        /// reload was not given leaves those implementations out of the result, with no skipped row
        /// and no edit-outside-method-bodies warning, because none of them was edited.
        /// </summary>
        [Test]
        public async Task Run_EditOfNeighborOfHiddenInterfaceImplementations_LeavesThemOutWithoutWarnings()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenNeighbor.cs",
                source => ReplaceUniqueFragment(source, "8304", "9304"));

            AssertEntry(result, typeof(HotReloadHiddenNeighborFixture), "Value", "Value");
            AssertNoEntryFor(result, typeof(HotReloadHiddenRunnerFixture));
            AssertNoEntryFor(result, typeof(HotReloadHiddenNestedInterfaceFixture));
            AssertNoEntryFor(result, typeof(HotReloadHiddenImplementerHolder.Nested));
            AssertNoEntryFor(result, typeof(HotReloadHiddenValueFixture));
            AssertNoEntryFor(result, typeof(HotReloadHiddenIndexedFixture));
            AssertNoEntryFor(result, typeof(HotReloadHiddenNotifierFixture));
            AssertNoEntryFor(result, typeof(HotReloadHiddenCopierFixture));
            Assert.That(FormatSkipped(result), Is.EqualTo("(none)"), "No unedited implementation may be skipped.");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited explicit implementation of an internal interface that the reload was not
        /// given is an entry named by the compiled metadata name, as it is for a public interface.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfHiddenInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenRunner.cs",
                source => ReplaceUniqueFragment(source, "8301", "9301"));

            AssertEntry(result, typeof(HotReloadHiddenRunnerFixture), Namespace + ".IHotReloadHiddenRunner.Run", "Run");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited explicit implementation of an internal interface that the reload was not
        /// given, whose signature names the type the reloaded file declares (as a parameter, a ref
        /// parameter's neighbor and the return type), is an entry named by the compiled metadata name.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfHiddenInterfaceNamingTheReloadedType_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenCopier.cs",
                source => ReplaceUniqueFragment(source, "8311", "9311"));

            AssertEntry(result, typeof(HotReloadHiddenCopierFixture), Namespace + ".IHotReloadHiddenCopier.Copy", "Copy");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited explicit implementation of an internal interface nested in a type, declared
        /// in a file the reload was not given, is an entry whose name joins the outer and nested type
        /// with a dot.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfNestedHiddenInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenNestedInterface.cs",
                source => ReplaceUniqueFragment(source, "8302", "9302"));

            AssertEntry(
                result,
                typeof(HotReloadHiddenNestedInterfaceFixture),
                Namespace + ".HotReloadHiddenOuter.IInner.Run",
                "Run");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: a nested type's edited explicit implementation of an internal interface that the
        /// reload was not given is an entry on the nested type, named by the compiled metadata name.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOnNestedImplementerOfHiddenInterface_EmitsEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenNestedImplementer.cs",
                source => ReplaceUniqueFragment(source, "8303", "9303"));

            AssertEntry(
                result,
                typeof(HotReloadHiddenImplementerHolder.Nested),
                Namespace + ".IHotReloadHiddenRunner.Run",
                "Run");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited explicit property implementation of an internal interface that the reload
        /// was not given is an entry for its getter under the compiled metadata name, not under a
        /// name built as if the property were an ordinary one.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitPropertyOfHiddenInterface_EmitsGetterEntryWithCompiledName()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenValue.cs",
                source => ReplaceUniqueFragment(source, "8305", "9305"));

            AssertEntry(
                result,
                typeof(HotReloadHiddenValueFixture),
                Namespace + ".IHotReloadHiddenValue.get_Value",
                "get_Value");
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited accessor of an explicit indexer or event of an internal interface that the
        /// reload was not given stays Skipped as it does for a public interface, under the name the
        /// compiled assembly gives the accessor, with no edit-outside-method-bodies warning.
        /// </summary>
        [TestCase("8309", "9309", "IHotReloadHiddenIndexed.get_Item(System.Int32)", "MethodTransformExplicitInterfaceImplementation")]
        [TestCase("8310", "9310", "IHotReloadHiddenNotifier.add_Changed(System.Action)", "UnsupportedMemberUnsupportedKind")]
        public async Task Run_EditedExplicitIndexerOrEventAccessorOfHiddenInterface_StaysSkippedUnderTheCompiledName(
            string token,
            string replacement,
            string accessorLabelEnd,
            string codeName)
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenIndexerOrEvent.cs",
                source => ReplaceUniqueFragment(source, token, replacement));

            Assert.That(
                result.Output.skipped.Select(skipped => skipped.method + " :: " + skipped.reason?.code),
                Is.EqualTo(new[] { Namespace + "." + FixtureTypeOf(token) + "." + Namespace + "." + accessorLabelEnd + " :: " + codeName }));
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an edited method of an implementer whose body relies on the class implementing an
        /// internal interface that the reload was not given (a conversion to it, an extension on it,
        /// an indexer taking it) is an entry, with no row for the implementer's explicit method.
        /// </summary>
        [TestCase("8306", "9306", "RunThroughInterface")]
        [TestCase("8307", "9307", "RunThroughExtension")]
        [TestCase("8308", "9308", "RunThroughIndexer")]
        public async Task Run_EditedBodyRelyingOnTheHiddenInterface_EmitsOnlyThatEntry(
            string token,
            string replacement,
            string methodName)
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenRelation.cs",
                source => ReplaceUniqueFragment(source, token, replacement));

            Assert.That(
                result.Output.entries.Select(entry => entry.typeMetadataName + "::" + entry.methodName),
                Is.EqualTo(new[] { typeof(HotReloadHiddenRunnerFixture).FullName + "::" + methodName }));
            Assert.That(FormatSkipped(result), Is.EqualTo("(none)"));
            AssertNoDriftOrRemovedMember(result);
        }

        /// <summary>
        /// What: an explicit implementation of an internal interface that the reload was not given,
        /// whose edit only respells the interface with global::, is reported unchanged under the
        /// compiled name with no entry and no warning, as it is for a public interface.
        /// </summary>
        [Test]
        public async Task Run_HiddenInterfaceImplementationOnlyRespelledWithGlobal_IsUnchangedWithoutWarnings()
        {
            TransformWorkerClientResult result = await RunWorkerOnEditedHiddenFixtureAsync(
                "HiddenGlobalOnlyRespelled.cs",
                source => ReplaceUniqueFragment(
                    source,
                    "int IHotReloadHiddenRunner.Run() { return 8301; }",
                    "int global::" + Namespace + ".IHotReloadHiddenRunner.Run() { return 8301; }"));

            AssertNoEntryFor(result, typeof(HotReloadHiddenRunnerFixture));
            Assert.That(
                result.Output.unchangedMethods
                    .Where(row => row.typeMetadataName == typeof(HotReloadHiddenRunnerFixture).FullName)
                    .Select(row => row.methodName),
                Does.Contain(Namespace + ".IHotReloadHiddenRunner.Run"));
            Assert.That(FormatSkipped(result), Is.EqualTo("(none)"));
            AssertNoDriftOrRemovedMember(result);
        }

        private static string FixtureTypeOf(string token)
        {
            return token == "8309" ? nameof(HotReloadHiddenIndexedFixture) : nameof(HotReloadHiddenNotifierFixture);
        }

        private static void AssertNoDriftOrRemovedMember(TransformWorkerClientResult result)
        {
            Assert.That(
                result.Output.files.SelectMany(file => file.declarationDriftWarnings ?? Array.Empty<string>()),
                Is.Empty,
                "A respelled interface alone is not an edit outside method bodies.");
            Assert.That(RemovedMemberLabels(result), Is.Empty, "A respelled interface alone removes no member.");
        }

        private static IEnumerable<string> RemovedMemberLabels(TransformWorkerClientResult result)
        {
            return result.Output.files
                .SelectMany(file => file.removedMembers ?? Array.Empty<TransformWorkerRemovedMemberDto>())
                .Select(member => member.kind + ":" + member.name);
        }

        private static void AssertNoEntryFor(TransformWorkerClientResult result, Type fixtureType)
        {
            Assert.That(
                result.Output.entries.Where(entry => entry.typeMetadataName == MetadataNameOf(fixtureType)).Select(entry => entry.methodName),
                Is.Empty,
                "No entry expected for " + fixtureType.Name);
        }

        private static void AssertSkippedWith(TransformWorkerClientResult result, string methodFragment, HotReloadWorkerReasonCode code)
        {
            Assert.That(
                result.Output.skipped.Any(skipped =>
                    skipped.method.Contains(methodFragment, StringComparison.Ordinal) && skipped.reason?.code == code),
                Is.True,
                FormatSkipped(result));
        }

        private static TransformWorkerEntryDto AssertEntry(
            TransformWorkerClientResult result,
            Type fixtureType,
            string expectedMethodName,
            string memberName)
        {
            TransformWorkerEntryDto entry = result.Output.entries.FirstOrDefault(candidate =>
                candidate.typeMetadataName == MetadataNameOf(fixtureType));
            Assert.That(entry, Is.Not.Null, "Expected an entry for " + fixtureType.Name + ". Skipped=" + FormatSkipped(result));
            Assert.That(entry.methodName, Is.EqualTo(expectedMethodName));
            Assert.That(
                entry.shimMethodName,
                Does.Match("^" + Regex.Escape(memberName) + "__shim[0-9]+$"),
                "The shim name must be a legal identifier taken from the interface member.");
            Assert.That(FormatSkipped(result), Does.Not.Contain(fixtureType.Name), "An emitted method must not also be Skipped.");
            return entry;
        }

        // Why: the worker names a nested type with '/', as metadata does, and reflection with '+'.
        private static string MetadataNameOf(Type fixtureType)
        {
            return fixtureType.FullName.Replace('+', '/');
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            if (result.Output.skipped == null || result.Output.skipped.Length == 0)
            {
                return "(none)";
            }

            return string.Join(" | ", result.Output.skipped.Select(skipped => skipped.method + " :: " + skipped.reason?.code));
        }

        private static string ReplaceUniqueFragment(string source, string fragment, string replacement)
        {
            int index = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Precondition: the fixture must hold " + fragment);
            Assert.That(
                source.LastIndexOf(fragment, StringComparison.Ordinal),
                Is.EqualTo(index),
                "Precondition: the fixture must hold " + fragment + " exactly once.");
            return source.Substring(0, index) + replacement + source.Substring(index + fragment.Length);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnEditedFixtureAsync(
            string fileName,
            Func<string, string> edit,
            string[] excludedMethodKeys = null)
        {
            return await RunWorkerOnEditedFileAsync(FixtureProjectRelativePath, fileName, edit, excludedMethodKeys);
        }

        // Why a separate file: its internal interfaces live in a file the reload is not given, so
        // the worker sees them only through the compiled assembly.
        private static async Task<TransformWorkerClientResult> RunWorkerOnEditedHiddenFixtureAsync(
            string fileName,
            Func<string, string> edit)
        {
            return await RunWorkerOnEditedFileAsync(HiddenFixtureProjectRelativePath, fileName, edit, null);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnEditedFileAsync(
            string fixtureProjectRelativePath,
            string fileName,
            Func<string, string> edit,
            string[] excludedMethodKeys)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fixturePath = Path.Combine(
                projectRoot,
                fixtureProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = edit(onDisk);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                onDisk,
                excludedMethodKeys,
                fixtureProjectRelativePath);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string snapshotSource,
            string[] excludedMethodKeys = null,
            string projectRelativePath = FixtureProjectRelativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(projectRoot, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
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

            List<string> referencePaths = new List<string>();
            foreach (string reference in compilationAssembly.allReferences ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    referencePaths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!referencePaths.Contains(fullTarget))
            {
                referencePaths.Add(fullTarget);
            }

            List<string> assemblySourcePaths = new List<string>();
            foreach (string sourceFile in compilationAssembly.sourceFiles ?? Array.Empty<string>())
            {
                assemblySourcePaths.Add(Path.GetFullPath(Path.Combine(projectRoot, sourceFile)));
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = projectRelativePath,
                        snapshotSource = snapshotSource
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = assemblySourcePaths.ToArray(),
                excludedMethodKeys = excludedMethodKeys ?? Array.Empty<string>(),
                // No sibling is passed, which stands for a scan that compared every sibling and
                // found none changed.
                changedSiblingScanComplete = true
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }
    }
}
