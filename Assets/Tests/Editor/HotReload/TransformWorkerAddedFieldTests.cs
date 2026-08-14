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
    /// Worker coverage for added/removed field classification, store rewrite, const folding,
    /// initializer visibility skip, and added-field skip/warning reasons.
    /// </summary>
    public class TransformWorkerAddedFieldTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        private const string ExistingCallerOriginal =
            "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";

        /// <summary>
        /// What: a field present only in the edited source is rewritten to the store, an existing
        /// field stays a real field access, and a snapshot-only field is reported removed.
        /// </summary>
        [Test]
        public async Task Classify_AddedExistingAndRemovedFields_MatchCompiledAssemblyGroundTruth()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                "        public HotReloadAddedMemberHost Inner;\n\n",
                string.Empty,
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return PublicSeed + AddedCount + value;\n        }",
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("ClassifyAddedExistingRemovedFields.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("HotReloadAddedFieldStore"));
            Assert.That(slice, Does.Contain("::AddedCount"));
            Assert.That(slice, Does.Contain("PublicSeed"));
            Assert.That(slice, Does.Not.Contain("::PublicSeed"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);

            bool foundRemoved = false;
            foreach (TransformWorkerRemovedMemberDto removed in result.Output.removedMembers)
            {
                if (removed.kind == HotReloadConstants.RemovedMemberKindField
                    && removed.name == nameof(HotReloadAddedMemberHost.Inner))
                {
                    foundRemoved = true;
                }
            }

            Assert.That(foundRemoved, Is.True, "Inner must be reported as a removed field.");
        }

        /// <summary>
        /// What: uses of an added const fold to a value literal so the shim does not need the
        /// missing const member, and the store flag stays false.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedConst_FoldsToLiteralWithoutStoreFlag()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public const int AddedConst = 4;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedConst + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedConstFold.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("4"));
            Assert.That(slice, Does.Not.Contain("HotReloadAddedFieldStore"));
            Assert.That(slice, Does.Not.Contain("AddedConst"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: nameof(added field) folds to the field name string, including added consts.
        /// </summary>
        [Test]
        public async Task Rewrite_NameofAddedField_FoldsToStringLiteral()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedCount;\n        public const int AddedConst = 4;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return nameof(AddedCount).Length + nameof(AddedConst).Length + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("NameofAddedField.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("\"AddedCount\""));
            Assert.That(slice, Does.Contain("\"AddedConst\""));
            Assert.That(slice, Does.Not.Contain("nameof("));
        }

        /// <summary>
        /// What: added-field reads and writes emit GetOrInit/Set, and an initializer becomes a
        /// static lambda on the GetOrInit call.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedFieldReadWriteAndInitializer_UsesStoreAndStaticLambda()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount = 5;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedCount = value;\n            return AddedCount;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldReadWrite.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(slice, Does.Contain("static () =>"));
            Assert.That(slice, Does.Contain("5"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: static added fields use GetOrInitStatic/SetStatic instead of the instance store.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedStaticField_UsesStaticStore()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public static int AddedStatic;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedStatic = value;\n            return AddedStatic;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedStaticField.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInitStatic<"));
            Assert.That(slice, Does.Contain("SetStatic<"));
            Assert.That(slice, Does.Not.Contain("GetOrInit<"));
        }

        /// <summary>
        /// What: compound assignment and increment on an added field expand to Get then Set.
        /// </summary>
        [Test]
        public async Task Rewrite_CompoundAndIncrement_ExpandToGetThenSet()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedCount += value;\n            AddedCount++;\n            ++AddedCount;\n"
                + "            return AddedCount;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldCompound.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(slice, Does.Contain("+ value"));
            Assert.That(slice, Does.Contain("+ 1"));
        }

        /// <summary>
        /// What: adding a field without other outside-body edits does not fire the drift warning
        /// because handled added field declarations are stripped before comparison.
        /// </summary>
        [Test]
        public async Task Drift_AddedFieldOnly_DoesNotFireOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldNoDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.declarationDriftWarnings, Is.Empty);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: an added field initializer that touches a private member skips referencing
        /// methods so the initializer lambda cannot throw FieldAccessException at runtime.
        /// </summary>
        [Test]
        public async Task Skip_PrivateInitializer_UsesInaccessibleReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedFromPrivate = _privateSeed;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedFromPrivate + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldPrivateInit.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "inaccessible");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: passing an added field by ref, out, or in skips the referencing method.
        /// </summary>
        [Test]
        public async Task Skip_RefOutInAddedField_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int.TryParse(\"1\", out AddedCount);\n            return AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldRef.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "ref, out, or in");
        }

        /// <summary>
        /// What: a compound assignment whose value is consumed skips, because Set returns void.
        /// </summary>
        [Test]
        public async Task Skip_ConsumedCompoundAssignment_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int consumed = AddedCount += value;\n            return consumed;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldConsumed.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "consumed");
        }

        /// <summary>
        /// What: assigning through a receiver that may have side effects skips so Get and Set
        /// do not evaluate it twice.
        /// </summary>
        [Test]
        public async Task Skip_DoubleEvalReceiver_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Get().AddedCount = value;\n            return value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldDoubleEval.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "side effects");
        }

        /// <summary>
        /// What: writing a member of an added value-type field skips because GetOrInit returns
        /// a copy, so the write would not persist.
        /// </summary>
        [Test]
        public async Task Skip_ValueTypeAddedFieldMemberWrite_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public HotReloadAddedFieldStructHost AddedValue;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedValue.Existing = value;\n            return AddedValue.Existing;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldN2.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "value-type");
        }

        /// <summary>
        /// What: added fields on a compiled struct type skip referencing methods because the
        /// store cannot keep identity without boxing.
        /// </summary>
        [Test]
        public async Task Skip_StructHostAddedField_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Existing;\n",
                "        public int Existing;\n\n        public int Added;\n",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            HotReloadAddedFieldStructHost local = default;\n"
                + "            return local.Added + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldStructHost.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "struct");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: a [SerializeField] added field still rewrites to the store and emits an
        /// Inspector/serialization warning.
        /// </summary>
        [Test]
        public async Task Warning_SerializeField_StillRewrites()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "[SerializeField] public int AddedSerialized;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedSerialized = value;\n            return AddedSerialized;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldSerialize.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("HotReloadAddedFieldStore"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);

            bool foundWarning = false;
            foreach (string warning in result.Output.declarationDriftWarnings)
            {
                if (warning != null
                    && warning.Contains("AddedSerialized")
                    && warning.Contains("Inspector"))
                {
                    foundWarning = true;
                }
            }

            Assert.That(foundWarning, Is.True, "SerializeField added fields must warn about Inspector visibility.");
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
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(
                    compilationAssembly.allReferences,
                    targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
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
    }
}
