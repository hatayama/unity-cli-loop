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
    /// Worker coverage for added/removed method classification, shim emit, call-site rewrite,
    /// skip reasons, Unity-message warnings, outside-body drift stripping, and isolation G1.
    /// </summary>
    public class TransformWorkerAddedMemberTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        /// <summary>
        /// What: a method present only in the edited source is classified Added, an existing
        /// edited method stays transplant, and a snapshot-only method is reported removed.
        /// </summary>
        [Test]
        public async Task Classify_AddedExistingAndRemovedMethods_MatchCompiledAssemblyGroundTruth()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedPing(int value)\n        {\n            return value + 1;\n        }");
            edited = edited.Replace(
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ExistingFail(int value)\n        {\n            return value;\n        }\n\n",
                string.Empty,
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("ClassifyAddedExistingRemoved.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto added = FindEntry(result, "AddedPing");
            Assert.That(added, Is.Not.Null, "AddedPing must be an entry.");
            Assert.That(added.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));

            TransformWorkerEntryDto existing = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingValue));
            Assert.That(existing, Is.Null, "Unedited ExistingValue must not be an entry when snapshot matches.");

            Assert.That(result.Output.removedMembers, Is.Not.Null);
            bool foundRemoved = false;
            foreach (TransformWorkerRemovedMemberDto removed in result.Output.removedMembers)
            {
                if (removed.kind == HotReloadConstants.RemovedMemberKindMethod
                    && removed.name == nameof(HotReloadAddedMemberHost.ExistingFail))
                {
                    foundRemoved = true;
                }
            }

            Assert.That(foundRemoved, Is.True, "ExistingFail must be reported as a removed method.");
        }

        /// <summary>
        /// What: an added method entry is emitted as a public static shim method in shimSource.
        /// </summary>
        [Test]
        public async Task Emit_AddedInstanceMethod_ProducesStaticShimMethod()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedPing(int value)\n        {\n            return value + 1;\n        }");
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto added = FindEntry(result, "AddedPing");
            Assert.That(added, Is.Not.Null);
            Assert.That(added.shimMethodName, Does.Contain("__shim"));
            Assert.That(result.Output.shimSource, Does.Contain("public static int " + added.shimMethodName));
            Assert.That(result.Output.shimSource, Does.Contain("__uloopInstance"));
        }

        /// <summary>
        /// What: implicit-this, explicit-this, receiver-expression, and static added-method calls
        /// plus mutual and recursive added-method calls are rewritten to the shim static form.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedMethodCallSites_UseShimStaticInvocations()
        {
            string extra =
                "public int AddedInstance(int value)\n        {\n            return AddedStatic(value);\n        }\n\n"
                + "        public static int AddedStatic(int value)\n        {\n            return value + 1;\n        }\n\n"
                + "        public int AddedRecursive(int value)\n        {\n"
                + "            if (value <= 0) return 0;\n"
                + "            return AddedRecursive(value - 1);\n        }\n\n"
                + "        public int AddedMutualA(int value)\n        {\n            return AddedMutualB(value);\n        }\n\n"
                + "        public int AddedMutualB(int value)\n        {\n            return value;\n        }";
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, extra);
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedInstance(value) + this.AddedInstance(value) + AddedStatic(value);\n"
                + "        }",
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("RewriteCallSites.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto addedInstance = FindEntry(result, "AddedInstance");
            TransformWorkerEntryDto addedStatic = FindEntry(result, "AddedStatic");
            TransformWorkerEntryDto addedRecursive = FindEntry(result, "AddedRecursive");
            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(addedInstance, Is.Not.Null);
            Assert.That(addedStatic, Is.Not.Null);
            Assert.That(addedRecursive, Is.Not.Null);
            Assert.That(caller, Is.Not.Null);

            string callerSlice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(callerSlice, Does.Contain(addedInstance.shimMethodName + "(__uloopInstance, value)"));
            Assert.That(callerSlice, Does.Contain(addedStatic.shimMethodName + "(value)"));

            string instanceSlice = SliceShimMethod(result.Output.shimSource, addedInstance.shimMethodName);
            Assert.That(instanceSlice, Does.Contain(addedStatic.shimMethodName + "(value)"));

            string recursiveSlice = SliceShimMethod(result.Output.shimSource, addedRecursive.shimMethodName);
            Assert.That(recursiveSlice, Does.Contain(addedRecursive.shimMethodName + "(__uloopInstance, value - 1)"));

            TransformWorkerEntryDto mutualA = FindEntry(result, "AddedMutualA");
            TransformWorkerEntryDto mutualB = FindEntry(result, "AddedMutualB");
            Assert.That(mutualA, Is.Not.Null);
            Assert.That(mutualB, Is.Not.Null);
            string mutualSlice = SliceShimMethod(result.Output.shimSource, mutualA.shimMethodName);
            Assert.That(mutualSlice, Does.Contain(mutualB.shimMethodName));
            Assert.That(
                caller.calledAddedMethodKeys,
                Does.Contain(BuildHostMethodKey("AddedInstance", "System.Int32")));
        }

        /// <summary>
        /// What: nameof(added method) is folded to a string literal so shim compile does not
        /// need to resolve the added name.
        /// </summary>
        [Test]
        public async Task Rewrite_NameofAddedMethod_FoldsToStringLiteral()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedPing(int value)\n        {\n            return value;\n        }");
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return nameof(AddedPing).Length + value;\n        }",
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("NameofAddedMethod.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("\"AddedPing\""));
            Assert.That(slice, Does.Not.Contain("nameof("));
        }

        /// <summary>
        /// What: added virtual, override, generic, and method-group-capturing methods are skipped
        /// with the documented reasons; the captured added instance method itself still emits.
        /// </summary>
        [Test]
        public async Task Skip_UnsupportedAddedMethods_UseDedicatedReasons()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public virtual int ExistingVirtual(int value)\n        {\n            return value;\n        }",
                "        public virtual int ExistingVirtual(int value)\n        {\n            return value;\n        }\n\n"
                + "        public virtual int AddedVirtual(int value)\n        {\n            return value;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ChildExisting()\n        {\n            return 1;\n        }",
                "        public int ChildExisting()\n        {\n            return 1;\n        }\n\n"
                + "        public override int ExistingVirtual(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            edited = WithHostMembers(
                edited,
                "public int AddedGeneric<T>(T value)\n        {\n            return 0;\n        }\n\n"
                + "        public int AddedInstance(int value)\n        {\n            return value;\n        }\n\n"
                + "        public int CaptureAdded()\n        {\n"
                + "            System.Func<int, int> bound = AddedInstance;\n"
                + "            return bound(1);\n        }");
            string sourcePath = WriteEdited("SkipUnsupportedAdded.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            AssertHasSkip(result, "AddedVirtual", "vtable slot");
            AssertHasSkip(result, "ExistingVirtual", "vtable slot");
            AssertHasSkip(result, "AddedGeneric", "Generic");
            AssertHasSkip(result, "CaptureAdded", "method group");
            Assert.That(FindEntry(result, "AddedInstance"), Is.Not.Null, "Added instance method must still emit.");
        }

        /// <summary>
        /// What: an added method that reads a private field becomes a delegation-style accessor
        /// rewrite while keeping patchKind addedMethod, and hasAccessorDelegates is true.
        /// </summary>
        [Test]
        public async Task AddedMethod_PrivateFieldAccess_EmitsAccessorsAndAddedMethodKind()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedReadPrivate()\n        {\n"
                + "            System.Func<int> read = () => _privateSeed;\n"
                + "            return read();\n        }");
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto added = FindEntry(result, "AddedReadPrivate");
            Assert.That(added, Is.Not.Null);
            Assert.That(added.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
            Assert.That(result.Output.hasAccessorDelegates, Is.True);
            Assert.That(result.Output.shimSource, Does.Contain("__BindAccessors"));
        }

        /// <summary>
        /// What: adding Update on a compiled MonoBehaviour-derived type keeps the entry and emits
        /// the Unity-message warning.
        /// </summary>
        [Test]
        public async Task Warn_AddedUnityMessageOnMonoBehaviour_KeepsEntryAndEmitsWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public void ExistingTick()\n        {\n        }",
                "        public void ExistingTick()\n        {\n        }\n\n"
                + "        public void Update()\n        {\n        }",
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("AddedUnityMessage.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto added = FindEntry(result, "Update");
            Assert.That(added, Is.Not.Null, "Added Update must still be an entry.");
            Assert.That(added.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
            Assert.That(
                result.Output.declarationDriftWarnings,
                Has.Some.Contain("Update").And.Contain("uloop compile"));
        }

        /// <summary>
        /// What: adding or removing a method does not emit the outside-method-body drift warning;
        /// a field-initializer change still does.
        /// </summary>
        [Test]
        public async Task Drift_HandledAddedAndRemovedMethods_DoNotFireOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string addedOnly = WithHostMembers(
                onDisk,
                "public int AddedPing(int value)\n        {\n            return value;\n        }");
            TransformWorkerClientResult addedResult = await RunWorkerOnSourceAsync(
                WriteEdited("DriftAddedOnly.cs", addedOnly),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(addedResult.Success, Is.True, addedResult.ErrorMessage);
            Assert.That(
                addedResult.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Handled method addition must not fire outside-body drift.\n"
                + string.Join("\n", addedResult.Output.declarationDriftWarnings ?? Array.Empty<string>()));

            string removedOnly = onDisk.Replace(
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ExistingFail(int value)\n        {\n            return value;\n        }\n\n",
                string.Empty,
                StringComparison.Ordinal);
            TransformWorkerClientResult removedResult = await RunWorkerOnSourceAsync(
                WriteEdited("DriftRemovedOnly.cs", removedOnly),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(removedResult.Success, Is.True, removedResult.ErrorMessage);
            Assert.That(
                removedResult.Output.declarationDriftWarnings,
                Has.None.Contain("Edits outside method bodies"),
                "Handled method removal must not fire outside-body drift.\n"
                + string.Join("\n", removedResult.Output.declarationDriftWarnings ?? Array.Empty<string>()));

            string fieldAndAdded = addedOnly.Replace(
                "public int PublicSeed = 3;",
                "public int PublicSeed = 4;",
                StringComparison.Ordinal);
            TransformWorkerClientResult fieldResult = await RunWorkerOnSourceAsync(
                WriteEdited("DriftFieldAndAdded.cs", fieldAndAdded),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(fieldResult.Success, Is.True, fieldResult.ErrorMessage);
            Assert.That(
                fieldResult.Output.declarationDriftWarnings,
                Has.Some.Contain("Edits outside method bodies"),
                "Field initializer drift must still fire after handled method additions are stripped.");
        }

        /// <summary>
        /// What: excluding an added method key still emits that added method so remaining callers
        /// can compile on isolation retry (G1).
        /// </summary>
        [Test]
        public async Task Isolation_ExcludedAddedMethodKey_StillEmitsAddedMethodShim()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedPing(int value)\n        {\n            return value + 1;\n        }");
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }",
                StringComparison.Ordinal);
            string addedKey = BuildHostMethodKey("AddedPing", "System.Int32");
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("IsolationKeepAdded.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk,
                excludedMethodKeys: new[] { addedKey });
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto added = FindEntry(result, "AddedPing");
            Assert.That(added, Is.Not.Null, "Added method must not be dropped by excludedMethodKeys.");
            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain(added.shimMethodName));
        }

        /// <summary>
        /// What: baseline null does not report removed members even when the edited source drops
        /// a compiled method.
        /// </summary>
        [Test]
        public async Task Classify_NoBaseline_DoesNotReportRemovedMembers()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ExistingFail(int value)\n        {\n            return value;\n        }\n\n",
                string.Empty,
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("NoBaselineRemoved.cs", edited),
                HostProjectRelativePath,
                snapshotSource: null);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.removedMembers, Is.Not.Null);
            Assert.That(result.Output.removedMembers, Is.Empty);
        }

        private static async Task<TransformWorkerClientResult> RunHostWithAddedMembersAsync(string extraMembers)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, extraMembers);
            return await RunWorkerOnSourceAsync(
                WriteEdited("HostWithAddedMembers.cs", edited),
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

        private static string BuildHostMethodKey(string methodName, params string[] parameterTypeFullNames)
        {
            return typeof(HotReloadAddedMemberHost).FullName
                + "::" + methodName + "("
                + string.Join(",", parameterTypeFullNames) + ")";
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
                + reasonFragment + "'. Skipped="
                + FormatSkipped(result.Output.skipped));
        }

        private static string FormatSkipped(TransformWorkerSkippedDto[] skipped)
        {
            if (skipped == null || skipped.Length == 0)
            {
                return "(none)";
            }

            List<string> rows = new List<string>();
            foreach (TransformWorkerSkippedDto entry in skipped)
            {
                rows.Add(entry.method + " :: " + entry.reason);
            }

            return string.Join("\n", rows);
        }

        private static string SliceShimMethod(string shimSource, string shimMethodName)
        {
            int nameIndex = shimSource.IndexOf(shimMethodName, StringComparison.Ordinal);
            Assert.That(nameIndex, Is.GreaterThanOrEqualTo(0), "Shim method missing: " + shimMethodName);
            int declarationStart = shimSource.LastIndexOf("public static", nameIndex, StringComparison.Ordinal);
            int openBrace = shimSource.IndexOf('{', nameIndex);
            Assert.That(openBrace, Is.GreaterThan(0));
            int depth = 0;
            for (int index = openBrace; index < shimSource.Length; index++)
            {
                if (shimSource[index] == '{')
                {
                    depth++;
                }
                else if (shimSource[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return shimSource.Substring(declarationStart, index - declarationStart + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced shim method: " + shimMethodName);
            return string.Empty;
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
            string snapshotSource = null,
            string[] excludedMethodKeys = null)
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

            string[] referencePaths = BuildAbsoluteReferencePaths(
                compilationAssembly.allReferences,
                targetDllPath);
            string[] assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles);

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths,
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = assemblySourcePaths,
                excludedMethodKeys = excludedMethodKeys ?? Array.Empty<string>()
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
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
    }
}
