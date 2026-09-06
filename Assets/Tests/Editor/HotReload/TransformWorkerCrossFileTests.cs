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
    /// EditMode coverage for transforming two edited files of one assembly in a single worker
    /// run, where a body edited in one file uses a member added in the other.
    /// </summary>
    public class TransformWorkerCrossFileTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string HostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberHost";
        private const string CallerTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberCaller";
        // Declared by an edited source only, so the collector cannot find it on disk.
        private const string EditedGlobalUsingLine =
            "global using HotReloadEditedFileGlobalAlias = System.Text.StringBuilder;\n";

        /// <summary>
        /// What: a body edited in the caller file binds to a method added in the host file within
        /// the same run, every row names the file that declared its type, the shim source carries
        /// both documents, and files[] follows the sources order.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_CallerBindsToAddedMethodInOtherSource()
        {
            CrossFileRun run = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                CallAddedHostMethod(ReadOnDisk(CallerFileName)));

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            TransformWorkerEntryDto addedEntry = FindEntry(run, HostTypeMetadataName, "Added");
            TransformWorkerEntryDto callerEntry = FindEntry(run, CallerTypeMetadataName, "Call");
            Assert.That(addedEntry.patchKind, Is.EqualTo("addedMethod"));
            Assert.That(callerEntry.calledAddedMethodKeys, Is.Not.Null);
            Assert.That(
                callerEntry.calledAddedMethodKeys,
                Has.Some.Contains("Added"),
                "The edited caller body must record the added host method it calls.");
            AssertNoSkippedMethodNamed(run, "Added");
            AssertNoSkippedMethodNamed(run, "Call");
            AssertEveryRowNamesItsDeclaringFile(run);

            Assert.That(run.Result.Output.shimSource, Does.Contain("\"" + run.HostProjectRelativePath + "\""));
            Assert.That(run.Result.Output.shimSource, Does.Contain("\"" + run.CallerProjectRelativePath + "\""));

            Assert.That(run.Result.Output.files.Length, Is.EqualTo(2));
            Assert.That(run.Result.Output.files[0].projectRelativePath, Is.EqualTo(run.HostProjectRelativePath));
            Assert.That(run.Result.Output.files[1].projectRelativePath, Is.EqualTo(run.CallerProjectRelativePath));
            Assert.That(run.Result.Output.files[0].sourceContentSha256, Is.Not.Empty);
            Assert.That(run.Result.Output.files[1].sourceContentSha256, Is.Not.Empty);
            Assert.That(
                run.Result.Output.files[0].sourceContentSha256,
                Is.Not.EqualTo(run.Result.Output.files[1].sourceContentSha256));
        }

        /// <summary>
        /// What: a field added in the host file is readable and writable from the caller file's
        /// edited body, and only the host's per-file row lists the added field name.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_CallerReadsAndWritesFieldAddedInOtherSource()
        {
            CrossFileRun run = await RunEditedPairAsync(
                InsertIntoHostBody(ReadOnDisk(HostFileName), "        public int Counter;\n"),
                ReplaceCallerBody(ReadOnDisk(CallerFileName), "host.Counter += 1;\n            return host.Counter;"));

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            Assert.That(run.Result.Output.files[0].addedFieldNames, Has.Some.Contains("Counter"));
            Assert.That(run.Result.Output.files[1].addedFieldNames, Is.Empty);
            Assert.That(run.Result.Output.hasAddedFieldRewrites, Is.True);
            FindEntry(run, CallerTypeMetadataName, "Call");
            AssertNoSkippedMethodNamed(run, "Call");
        }

        /// <summary>
        /// What: a const added in the host file folds into the caller file's edited body, and only
        /// the host's per-file row lists the folded const name.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_CallerFoldsConstAddedInOtherSource()
        {
            CrossFileRun run = await RunEditedPairAsync(
                InsertIntoHostBody(ReadOnDisk(HostFileName), "        public const int Limit = 7;\n"),
                ReplaceCallerBody(
                    ReadOnDisk(CallerFileName),
                    "return HotReloadCrossFileAddedMemberHost.Limit;"));

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            Assert.That(run.Result.Output.files[0].addedConstNames, Has.Some.Contains("Limit"));
            Assert.That(run.Result.Output.files[1].addedConstNames, Is.Empty);
            FindEntry(run, CallerTypeMetadataName, "Call");
        }

        /// <summary>
        /// What: a global using declared in one edited file reaches the shim source, because the
        /// collector takes the edited sources from the in-memory trees instead of their pre-edit
        /// copies on disk.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_GlobalUsingDeclaredInEditedFile_ReachesShimSource()
        {
            CrossFileRun run = await RunEditedPairAsync(
                EditedGlobalUsingLine + AddHostMethod(ReadOnDisk(HostFileName)),
                ReplaceCallerBody(
                    ReadOnDisk(CallerFileName),
                    "return new HotReloadEditedFileGlobalAlias().Length;"));

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            FindEntry(run, CallerTypeMetadataName, "Call");
            AssertNoSkippedMethodNamed(run, "Call");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Contain("using HotReloadEditedFileGlobalAlias"),
                "The shim source must carry the global using the edited sibling file declares.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Not.Contain("global using"),
                "A global using is not allowed inside the shim type's namespace declaration.");
        }

        /// <summary>
        /// What: shim type and method names stay unique across the files of one run, so the single
        /// shim assembly cannot host two members under the same name.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_ShimTypeNamesAreUnique()
        {
            CrossFileRun run = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                CallAddedHostMethod(ReadOnDisk(CallerFileName)));

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            HashSet<string> shimMembers = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in run.Result.Output.entries)
            {
                Assert.That(
                    shimMembers.Add(entry.shimTypeName + "::" + entry.shimMethodName),
                    Is.True,
                    "Duplicate shim member: " + entry.shimTypeName + "::" + entry.shimMethodName);
            }
        }

        /// <summary>
        /// What: excluding the host file's added method skips the caller file's edited body in the
        /// other source, because the caller can no longer bind to it.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_WhenAddedMethodIsExcluded_SkipsOtherSourceCaller()
        {
            CrossFileRun probe = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                CallAddedHostMethod(ReadOnDisk(CallerFileName)));
            Assert.That(probe.Result.Success, Is.True, probe.Result.ErrorMessage);
            TransformWorkerEntryDto callerEntry = FindEntry(probe, CallerTypeMetadataName, "Call");
            Assert.That(callerEntry.calledAddedMethodKeys.Length, Is.EqualTo(1));
            string addedMethodKey = callerEntry.calledAddedMethodKeys[0];

            CrossFileRun run = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                CallAddedHostMethod(ReadOnDisk(CallerFileName)),
                excludedAddedMethodKeys: new[] { addedMethodKey });

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            TransformWorkerSkippedDto callerSkip = FindSkipped(run, "Call");
            Assert.That(callerSkip.calledAddedMethodKey, Is.EqualTo(addedMethodKey));
            Assert.That(
                TryFindEntry(run, CallerTypeMetadataName, "Call"),
                Is.Null,
                "An excluded added method must not leave its caller patched.");
        }

        /// <summary>
        /// What: a caller that reaches an added host method through a compiled holder property
        /// still records the added method and rewrites the invocation, because the receiver type
        /// name plus method name plus argument count is enough to bind when GetSymbolInfo cannot.
        /// </summary>
        [Test]
        public async Task Run_CallerReachesHostThroughCompiledHolder_BindsAddedMethodByReceiverType()
        {
            string editedCaller = ReplaceInSource(
                ReadOnDisk(CallerFileName),
                "return holder.Host.Value();",
                "return Twice(holder.Host.Added());");
            CrossFileRun run = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                editedCaller);

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            TransformWorkerEntryDto callerEntry = FindEntry(run, CallerTypeMetadataName, "CallThroughHolder");
            Assert.That(callerEntry.calledAddedMethodKeys, Is.Not.Null);
            Assert.That(
                callerEntry.calledAddedMethodKeys,
                Has.Some.Contains("Added"),
                "The edited caller body must record the added host method reached through the holder.");
            AssertNoSkippedMethodNamed(run, "CallThroughHolder");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Not.Contain("holder.Host.Added("),
                "The unbound metadata-receiver call must be rewritten off the added method.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Contain("__uloopInstance.Twice("),
                "The collateral unbound compiled call must be qualified onto the instance parameter.");
        }

        /// <summary>
        /// What: a metadata value receiver does not bind an added static method, so the call
        /// stays unbound instead of rewriting away the receiver evaluation (CS0176).
        /// </summary>
        [Test]
        public async Task Run_ValueReceiverDoesNotBindAddedStaticMethod()
        {
            string editedCaller = ReplaceInSource(
                ReadOnDisk(CallerFileName),
                "return holder.Host.Value();",
                "return holder.Host.AddedStatic();");
            CrossFileRun run = await RunEditedPairAsync(
                AddHostStaticMethod(ReadOnDisk(HostFileName)),
                editedCaller);

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            TransformWorkerEntryDto addedEntry = FindEntry(run, HostTypeMetadataName, "AddedStatic");
            Assert.That(addedEntry.patchKind, Is.EqualTo("addedMethod"));
            TransformWorkerEntryDto callerEntry = FindEntry(run, CallerTypeMetadataName, "CallThroughHolder");
            Assert.That(
                callerEntry.calledAddedMethodKeys == null
                || !Array.Exists(callerEntry.calledAddedMethodKeys, key => key.Contains("AddedStatic")),
                Is.True,
                "A value-receiver call must not record the added static method.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Contain("holder.Host.AddedStatic("),
                "The mismatched static call must stay on the compiled receiver.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Not.Contain("." + addedEntry.shimMethodName + "("),
                "The added static shim must not be invoked from the value-receiver call.");
        }

        /// <summary>
        /// What: a using-alias type-name receiver still binds an added static method, because
        /// GetSymbolInfo on the alias identifier returns the target type, not IAliasSymbol.
        /// </summary>
        [Test]
        public async Task Run_TypeAliasReceiverBindsAddedStaticMethod()
        {
            string editedCaller = ReplaceInSource(
                ReadOnDisk(CallerFileName),
                "using System.Runtime.CompilerServices;\n",
                "using System.Runtime.CompilerServices;\n"
                + "using HostAlias = io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberHost;\n");
            editedCaller = ReplaceInSource(
                editedCaller,
                "return holder.Host.Value();",
                "return HostAlias.AddedStatic();");
            CrossFileRun run = await RunEditedPairAsync(
                AddHostStaticMethod(ReadOnDisk(HostFileName)),
                editedCaller);

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            TransformWorkerEntryDto addedEntry = FindEntry(run, HostTypeMetadataName, "AddedStatic");
            Assert.That(addedEntry.patchKind, Is.EqualTo("addedMethod"));
            TransformWorkerEntryDto callerEntry = FindEntry(run, CallerTypeMetadataName, "CallThroughHolder");
            Assert.That(callerEntry.calledAddedMethodKeys, Is.Not.Null);
            Assert.That(
                callerEntry.calledAddedMethodKeys,
                Has.Some.Contains("AddedStatic"),
                "A type-alias receiver must record the added static method.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Not.Contain("HostAlias.AddedStatic("),
                "The type-alias static call must be rewritten off the added method.");
            Assert.That(
                run.Result.Output.shimSource,
                Does.Contain("." + addedEntry.shimMethodName + "("),
                "The added static shim must be invoked from the type-alias call.");
        }

        /// <summary>
        /// What: an unreadable source reports its failure on its own per-file row only, and the
        /// other file of the run still produces entries.
        /// </summary>
        [Test]
        public async Task Run_TwoSources_WhenOneSourceIsUnreadable_ReportsParseErrorOnlyForThatFile()
        {
            CrossFileRun run = await RunEditedPairAsync(
                AddHostMethod(ReadOnDisk(HostFileName)),
                CallAddedHostMethod(ReadOnDisk(CallerFileName)),
                missingHostSourcePath: true);

            Assert.That(run.Result.Success, Is.True, run.Result.ErrorMessage);
            Assert.That(run.Result.Output.files[0].parseErrors, Is.Not.Empty);
            Assert.That(run.Result.Output.files[1].parseErrors, Is.Empty);
            Assert.That(run.Result.Output.entries, Is.Not.Empty);
            foreach (TransformWorkerEntryDto entry in run.Result.Output.entries)
            {
                Assert.That(entry.sourceProjectRelativePath, Is.EqualTo(run.CallerProjectRelativePath));
            }
        }

        private static string ReadOnDisk(string fileName)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
        }

        private static string AddHostMethod(string hostSource)
        {
            return InsertIntoHostBody(
                hostSource,
                "        public int Added()\n        {\n            return 41;\n        }\n\n");
        }

        private static string AddHostStaticMethod(string hostSource)
        {
            return InsertIntoHostBody(
                hostSource,
                "        public static int AddedStatic()\n        {\n            return 41;\n        }\n\n");
        }

        private static string InsertIntoHostBody(string hostSource, string memberText)
        {
            const string anchor = "        public int Value()";
            Assert.That(hostSource, Does.Contain(anchor), "Precondition: host anchor must exist.");
            return hostSource.Replace(anchor, memberText + anchor, StringComparison.Ordinal);
        }

        private static string CallAddedHostMethod(string callerSource)
        {
            return ReplaceCallerBody(callerSource, "return host.Added() + 1;");
        }

        private static string ReplaceCallerBody(string callerSource, string bodyText)
        {
            return ReplaceInSource(callerSource, "return host.Value();", bodyText);
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static TransformWorkerEntryDto FindEntry(
            CrossFileRun run,
            string typeMetadataName,
            string methodName)
        {
            TransformWorkerEntryDto entry = TryFindEntry(run, typeMetadataName, methodName);
            Assert.That(entry, Is.Not.Null, "Missing entry: " + typeMetadataName + "." + methodName);
            return entry;
        }

        private static TransformWorkerEntryDto TryFindEntry(
            CrossFileRun run,
            string typeMetadataName,
            string methodName)
        {
            foreach (TransformWorkerEntryDto entry in run.Result.Output.entries)
            {
                if (entry.typeMetadataName == typeMetadataName && entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static TransformWorkerSkippedDto FindSkipped(CrossFileRun run, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in run.Result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return skipped;
                }
            }

            Assert.Fail("Missing skipped row for " + methodName);
            return null;
        }

        private static void AssertNoSkippedMethodNamed(CrossFileRun run, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in run.Result.Output.skipped)
            {
                Assert.That(
                    skipped.method,
                    Does.Not.Contain("." + methodName + "("),
                    "Unexpected skip: " + skipped.method + " (" + skipped.reason + ")");
            }
        }

        private static void AssertEveryRowNamesItsDeclaringFile(CrossFileRun run)
        {
            foreach (TransformWorkerEntryDto entry in run.Result.Output.entries)
            {
                Assert.That(
                    entry.sourceProjectRelativePath,
                    Is.EqualTo(ExpectedPathOfType(run, entry.typeMetadataName)),
                    "Entry row names the wrong file: " + entry.methodName);
            }

            foreach (TransformWorkerSkippedDto skipped in run.Result.Output.skipped)
            {
                Assert.That(
                    skipped.sourceProjectRelativePath == run.HostProjectRelativePath
                    || skipped.sourceProjectRelativePath == run.CallerProjectRelativePath,
                    Is.True,
                    "Skipped row names a file outside the run: " + skipped.method);
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in run.Result.Output.unchangedMethods)
            {
                Assert.That(
                    unchanged.sourceProjectRelativePath,
                    Is.EqualTo(ExpectedPathOfType(run, unchanged.typeMetadataName)),
                    "Unchanged row names the wrong file: " + unchanged.methodName);
            }
        }

        private static string ExpectedPathOfType(CrossFileRun run, string typeMetadataName)
        {
            return typeMetadataName == HostTypeMetadataName
                ? run.HostProjectRelativePath
                : run.CallerProjectRelativePath;
        }

        private static async Task<CrossFileRun> RunEditedPairAsync(
            string editedHostSource,
            string editedCallerSource,
            string[] excludedAddedMethodKeys = null,
            bool missingHostSourcePath = false)
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

            string temporaryDirectory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "CrossFile");
            Directory.CreateDirectory(temporaryDirectory);
            string hostSourcePath = Path.Combine(temporaryDirectory, HostFileName);
            string callerSourcePath = Path.Combine(temporaryDirectory, CallerFileName);
            File.WriteAllText(hostSourcePath, editedHostSource);
            File.WriteAllText(callerSourcePath, editedCallerSource);

            CrossFileRun run = new CrossFileRun
            {
                HostProjectRelativePath = "Assets/Tests/Editor/HotReload/" + HostFileName,
                CallerProjectRelativePath = "Assets/Tests/Editor/HotReload/" + CallerFileName
            };
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = missingHostSourcePath
                            ? Path.Combine(temporaryDirectory, "MissingCrossFileHost.cs")
                            : hostSourcePath,
                        projectRelativePath = run.HostProjectRelativePath,
                        snapshotSource = ReadOnDisk(HostFileName)
                    },
                    new TransformWorkerSourceDto
                    {
                        sourcePath = callerSourcePath,
                        projectRelativePath = run.CallerProjectRelativePath,
                        snapshotSource = ReadOnDisk(CallerFileName)
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedAddedMethodKeys = excludedAddedMethodKeys
            };

            run.Result = await TransformWorkerClient.RunAsync(input, CancellationToken.None);
            return run;
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
            if (!paths.Contains(fullTarget))
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
                paths[index] = Path.GetFullPath(
                    Path.Combine(projectRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            return paths;
        }

        // One cross-file worker run: the paths the sources were sent under and the run's result.
        private sealed class CrossFileRun
        {
            public string HostProjectRelativePath { get; set; }

            public string CallerProjectRelativePath { get; set; }

            public TransformWorkerClientResult Result { get; set; }
        }
    }
}
