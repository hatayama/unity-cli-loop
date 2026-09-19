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
    /// Worker coverage for bodies that reach members hot reload added through shapes the
    /// accessor rewrite refuses for compiled members: a private static property, and a private
    /// method call with ref or out arguments. Added members are rewritten to public shim calls,
    /// so only compiled members may keep those refusals.
    /// </summary>
    public class TransformWorkerAddedMemberAccessTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        private const string StaticPropertyNoShape = "inaccessible static property access has no accessor rewrite shape";
        private const string RefOutInNotRewritten = "inaccessible method calls with ref/out/in parameters are not rewritten";
        private const string EventPassedByRef = "pass a field-like event by ref/out/in";

        /// <summary>
        /// What: an added method that reads an added private static property is added, not
        /// skipped for having no static-property accessor shape.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingAnAddedPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private static int AddedStaticSeed => 4;\n\n"
                + "        public int AddedReadStaticSeed()\n        {\n            return AddedStaticSeed + 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedReadStaticSeed");
        }

        /// <summary>
        /// What: an added method that writes an added private static property is added, not
        /// skipped for having no static-property accessor shape.
        /// </summary>
        [Test]
        public async Task AddedMethod_WritingAnAddedPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private static int AddedStaticCounter { get; set; }\n\n"
                + "        public int AddedWriteStaticCounter(int value)\n        {\n"
                + "            AddedStaticCounter = value;\n            return value;\n        }");

            AssertAddedAndNotSkipped(result, "AddedWriteStaticCounter");
        }

        /// <summary>
        /// What: calling an added private method with an out argument is added whether the
        /// callee is declared before the caller or after it.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task AddedMethod_CallingAnAddedPrivateOutMethod_IsAddedInEitherDeclarationOrder(bool calleeFirst)
        {
            string callee =
                "private bool AddedTryStep(int input, out int output)\n        {\n"
                + "            output = input + 2;\n            return true;\n        }";
            string caller =
                "public int AddedStepCaller(int input)\n        {\n"
                + "            return AddedTryStep(input, out int output) ? output : -1;\n        }";
            string members = calleeFirst
                ? callee + "\n\n        " + caller
                : caller + "\n\n        " + callee;

            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(members);

            AssertAddedAndNotSkipped(result, "AddedStepCaller");
            AssertAddedAndNotSkipped(result, "AddedTryStep");
        }

        /// <summary>
        /// What: calling an added private method with a ref argument is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingAnAddedPrivateRefMethod_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedBumpCaller(int input)\n        {\n"
                + "            int value = input;\n            AddedBump(ref value);\n            return value;\n        }\n\n"
                + "        private void AddedBump(ref int value)\n        {\n            value += 3;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpCaller");
            AssertAddedAndNotSkipped(result, "AddedBump(");
        }

        /// <summary>
        /// What: passing a compiled private field-like event by ref to an added private method is
        /// still skipped for passing the event by ref, because the event read is rewritten to an
        /// accessor call that cannot be passed by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateEventByRefToAnAddedMethod_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedClearsEvent()\n        {\n            AddedClear(ref PrivateChanged);\n        }\n\n"
                + "        private void AddedClear(ref Action value)\n        {\n            value = null;\n        }");

            AssertHasSkip(result, "AddedClearsEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: an added method that passes a compiled private field-like event by ref to an
        /// accessible method is skipped, because the event read is rewritten to an accessor call
        /// that cannot be passed by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedSwapsEvent()\n        {\n"
                + "            System.Threading.Interlocked.Exchange(ref PrivateChanged, null);\n        }");

            AssertHasSkip(result, "AddedSwapsEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: wrapping the event in parentheses before passing it by ref does not get past the
        /// by-ref check, because the rewritten read is still passed by reference.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingAParenthesizedCompiledPrivateEventByRef_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedSwapsWrappedEvent()\n        {\n"
                + "            System.Threading.Interlocked.Exchange(ref ((this.PrivateChanged)), null);\n        }");

            AssertHasSkip(result, "AddedSwapsWrappedEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: an added property whose getter passes a compiled private field-like event by ref
        /// to an accessible method is skipped for the same reason as a method body.
        /// </summary>
        [Test]
        public async Task AddedPropertyGetter_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public bool AddedSwapsEventOnRead\n        {\n            get\n            {\n"
                + "                return System.Threading.Interlocked.Exchange(ref PrivateChanged, null) != null;\n"
                + "            }\n        }");

            AssertHasSkip(result, "get_AddedSwapsEventOnRead", EventPassedByRef);
        }

        /// <summary>
        /// What: a patched method that passes a compiled private field-like event by ref to an
        /// accessible method is skipped rather than producing a shim that does not compile.
        /// </summary>
        [Test]
        public async Task PatchedMethod_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string compiledBody = "            return PrivateChanged != null;\n";
            Assert.That(onDisk, Does.Contain(compiledBody));
            string edited = onDisk.Replace(
                compiledBody,
                "            return System.Threading.Interlocked.Exchange(ref PrivateChanged, null) != null;\n",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("HostPassingEventByRef.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);

            AssertHasSkip(result, "HasPrivateChangedListeners", EventPassedByRef);
        }

        /// <summary>
        /// What: passing a compiled private field by ref to an added private method is added, and
        /// the shim keeps the argument by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateFieldByRefToAnAddedMethod_IsAddedWithTheRefKept()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedBumpsSeed()\n        {\n            AddedBumpSeed(ref _privateSeed);\n            return _privateSeed;\n        }\n\n"
                + "        private void AddedBumpSeed(ref int value)\n        {\n            value += 3;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpsSeed");
            Assert.That(result.Output.shimSource, Does.Contain("(__uloopInstance, ref __F__privateSeed(__uloopInstance))"), result.Output.shimSource);
        }

        /// <summary>
        /// What: an added private method that calls itself with an out argument is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_RecursingThroughItsOwnOutParameter_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private int AddedCountDown(int remaining, out int steps)\n        {\n"
                + "            if (remaining <= 0)\n            {\n                steps = 0;\n                return 0;\n            }\n\n"
                + "            int result = AddedCountDown(remaining - 1, out int inner);\n"
                + "            steps = inner + 1;\n            return result;\n        }");

            AssertAddedAndNotSkipped(result, "AddedCountDown");
        }

        /// <summary>
        /// What: a patched method whose lambda reads an added private static property and calls an
        /// added private out method is patched, not skipped for the accessor shapes.
        /// </summary>
        [Test]
        public async Task PatchedClosure_ReachingAddedPrivateStaticPropertyAndOutMethod_IsNotSkipped()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "private static int AddedStaticSeed => 4;\n\n"
                + "        private bool AddedTryStep(int input, out int output)\n        {\n"
                + "            output = input + 2;\n            return true;\n        }");
            string existingCaller =
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";
            Assert.That(edited, Does.Contain(existingCaller));
            edited = edited.Replace(
                existingCaller,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Func<int> read = () => AddedTryStep(value, out int output) ? output + AddedStaticSeed : -1;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PatchedClosureReachesAdded.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, "ExistingCaller"), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "ExistingCaller"), Is.Not.Null);
        }

        /// <summary>
        /// What: an added method that reads a compiled private static property is still skipped,
        /// because the loaded member stays private whatever the edited source declares.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingACompiledPrivateStaticProperty_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedReadCompiledStatic()\n        {\n            return PrivateStaticSeedValue + 1;\n        }");

            AssertHasSkip(result, "AddedReadCompiledStatic", StaticPropertyNoShape);
        }

        /// <summary>
        /// What: an added method that calls a compiled private out method is still skipped, while
        /// a same-named added overload with a different out type in the same reload is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingACompiledPrivateOutMethod_StaysSkippedBesideAnAddedOverload()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private bool TryReadPrivateSeed(out long value)\n        {\n"
                + "            value = 9L;\n            return true;\n        }\n\n"
                + "        public int AddedCallsCompiledOut()\n        {\n"
                + "            return TryReadPrivateSeed(out int value) ? value : -1;\n        }\n\n"
                + "        public long AddedCallsAddedOut()\n        {\n"
                + "            return TryReadPrivateSeed(out long value) ? value : -1L;\n        }");

            AssertHasSkip(result, "AddedCallsCompiledOut", RefOutInNotRewritten);
            AssertAddedAndNotSkipped(result, "AddedCallsAddedOut");
        }

        /// <summary>
        /// What: when the added out method it calls is skipped for its own body, the caller is
        /// skipped too instead of reaching a shim method that was never emitted.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingASkippedAddedOutMethod_IsSkippedWithIt()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private bool AddedTryBase(out string text)\n        {\n"
                + "            text = base.ToString();\n            return true;\n        }\n\n"
                + "        public int AddedUsesTryBase()\n        {\n"
                + "            return AddedTryBase(out string text) ? text.Length : 0;\n        }");

            Assert.That(FindSkipReason(result, "AddedTryBase"), Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindSkipReason(result, "AddedUsesTryBase"), Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "AddedUsesTryBase"), Is.Null);
        }

        private static void AssertAddedAndNotSkipped(TransformWorkerClientResult result, string methodNameFragment)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, methodNameFragment),
                Is.Null,
                FormatSkipped(result.Output.skipped));
            string methodName = methodNameFragment.TrimEnd('(');
            TransformWorkerEntryDto entry = FindEntry(result, methodName);
            Assert.That(entry, Is.Not.Null, methodName + " must be an entry.");
            Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
        }

        private static void AssertHasSkip(
            TransformWorkerClientResult result,
            string methodNameFragment,
            string reasonFragment)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string reason = FindSkipReason(result, methodNameFragment);
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain(reasonFragment));
        }

        private static async Task<TransformWorkerClientResult> RunHostWithAddedMembersAsync(string extraMembers)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, extraMembers);
            return await RunWorkerOnSourceAsync(
                WriteEdited("HostWithAddedMemberAccess.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
        }

        private static string WithHostMembers(string onDisk, string extraMembers)
        {
            Assert.That(onDisk, Does.Contain(HostCloseMarker));
            return onDisk.Replace(
                HostCloseMarker,
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n        "
                + extraMembers
                + "\n    }",
                StringComparison.Ordinal);
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodNameFragment))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerSkippedDto[] skipped)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto row in skipped)
            {
                lines.Add(row.method + " => " + HotReloadWorkerReasonText.Render(row.reason));
            }

            return "Skipped=[" + string.Join(" | ", lines) + "]";
        }

        private static string WriteEdited(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string ResolveHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedMemberHost.cs");
            Assert.That(File.Exists(path), Is.True, "Added-member host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource)
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
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        paths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!paths.Exists(path => string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase)))
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
                paths[index] = Path.GetFullPath(Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            return paths;
        }
    }
}
