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
        /// which has no global:: prefix.
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
        }

        /// <summary>What: respelling the interface through a using alias still yields the compiled name.</summary>
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

        private static TransformWorkerEntryDto AssertEntry(
            TransformWorkerClientResult result,
            Type fixtureType,
            string expectedMethodName,
            string memberName)
        {
            TransformWorkerEntryDto entry = result.Output.entries.FirstOrDefault(candidate =>
                candidate.typeMetadataName == fixtureType.FullName);
            Assert.That(entry, Is.Not.Null, "Expected an entry for " + fixtureType.Name + ". Skipped=" + FormatSkipped(result));
            Assert.That(entry.methodName, Is.EqualTo(expectedMethodName));
            Assert.That(
                entry.shimMethodName,
                Does.Match("^" + Regex.Escape(memberName) + "__shim[0-9]+$"),
                "The shim name must be a legal identifier taken from the interface member.");
            Assert.That(FormatSkipped(result), Does.Not.Contain(fixtureType.Name), "An emitted method must not also be Skipped.");
            return entry;
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
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fixturePath = Path.Combine(
                projectRoot,
                FixtureProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = edit(onDisk);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: snapshot must differ.");

            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, fileName);
            File.WriteAllText(sourcePath, editedSource);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(sourcePath, onDisk, excludedMethodKeys);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string snapshotSource,
            string[] excludedMethodKeys = null)
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
                        projectRelativePath = FixtureProjectRelativePath,
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
