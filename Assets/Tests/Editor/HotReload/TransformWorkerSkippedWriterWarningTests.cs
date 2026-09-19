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
    /// Worker coverage for the warning a reload reports when an applied method reads an added
    /// field that only skipped methods assign, so the field keeps its default value until the
    /// next compile. Every case runs the cross-file host and caller pair, because a writer in
    /// another file must keep the warning quiet.
    /// </summary>
    public class TransformWorkerSkippedWriterWarningTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string HostLabelPrefix =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberHost.";
        private const string WarningMarker = "which this reload skipped";

        private const string AddedField = "        public int AddedValue;\n\n";
        private const string SkippedWriter =
            "        public void AddedSetUp()\n        {\n            AddedValue = StoredRef;\n        }\n\n";
        private const string ValueBody = "        public int Value()\n        {\n            return 1;\n        }";

        /// <summary>
        /// What: when the only method assigning an added field is skipped and an applied method
        /// reads it, the host file reports one warning naming the field, the skipped writer and
        /// the applied reader.
        /// </summary>
        [Test]
        public async Task SkippedOnlyWriter_WithAppliedReader_Warns()
        {
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + SkippedWriter, "return AddedValue;"),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            Assert.That(FindEntry(result, "Value"), Is.Not.Null);
            List<string> warnings = FindWarnings(result);
            Assert.That(warnings, Has.Count.EqualTo(1), string.Join(" | ", warnings));
            Assert.That(warnings[0], Does.Contain("'AddedValue'"));
            Assert.That(warnings[0], Does.Contain(HostLabelPrefix + "AddedSetUp()"));
            Assert.That(warnings[0], Does.Contain(HostLabelPrefix + "Value()"));
            Assert.That(warnings[0], Does.Contain("uloop compile"));
        }

        /// <summary>
        /// What: an added field with an initializer does not warn, because it does not keep its
        /// default value when its only writer is skipped.
        /// </summary>
        [Test]
        public async Task FieldWithInitializer_DoesNotWarn()
        {
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader("        public int AddedValue = 1;\n\n" + SkippedWriter, "return AddedValue;"),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: a constructor that also assigns the field keeps the warning quiet, because a
        /// writer that is not a skipped method cannot be proven to leave the field unassigned.
        /// </summary>
        [Test]
        public async Task ConstructorAlsoWrites_DoesNotWarn()
        {
            string constructor =
                "        public HotReloadCrossFileAddedMemberHost()\n        {\n            AddedValue = 2;\n        }\n\n";
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + constructor + SkippedWriter, "return AddedValue;"),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: an applied method that also assigns the field keeps the warning quiet.
        /// </summary>
        [Test]
        public async Task AppliedMethodAlsoWrites_DoesNotWarn()
        {
            string appliedWriter =
                "        public void AddedReset()\n        {\n            AddedValue = 0;\n        }\n\n";
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + appliedWriter + SkippedWriter, "return AddedValue;"),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            Assert.That(FindEntry(result, "AddedReset"), Is.Not.Null);
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: no warning when every method that reads the field is skipped as well, because
        /// nothing applied observes the default value.
        /// </summary>
        [Test]
        public async Task NoAppliedReader_DoesNotWarn()
        {
            string skippedReader =
                "        public int AddedRead()\n        {\n            return AddedValue + StoredRef;\n        }\n\n";
            TransformWorkerClientResult result = await RunAsync(
                InsertIntoHostBody(ReadOnDisk(HostFileName), AddedField + SkippedWriter + skippedReader),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            Assert.That(FindSkipped(result, "AddedRead"), Is.Not.Null, FormatSkipped(result));
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: an ordinary reload with no skipped method does not warn, even though an applied
        /// method reads the added field.
        /// </summary>
        [Test]
        public async Task NothingSkipped_DoesNotWarn()
        {
            string appliedWriter =
                "        public void AddedSetUp()\n        {\n            AddedValue = 4;\n        }\n\n";
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + appliedWriter, "return AddedValue;"),
                ReadOnDisk(CallerFileName));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.skipped, Is.Empty, FormatSkipped(result));
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: an applied method of another type in another edited file that assigns the field
        /// keeps the warning quiet, so writers are looked for across the whole run.
        /// </summary>
        [Test]
        public async Task AppliedWriterInOtherFile_DoesNotWarn()
        {
            string caller = ReplaceInSource(
                ReadOnDisk(CallerFileName),
                "return host.Value();",
                "host.AddedValue = 3;\n            return 0;");
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + SkippedWriter, "return AddedValue;"),
                caller);

            AssertWriterSkipped(result);
            Assert.That(FindEntry(result, "Call"), Is.Not.Null);
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: an applied method that names the field only inside nameof does not count as a
        /// reader, because nameof folds to a string and never reads the value.
        /// </summary>
        [Test]
        public async Task AppliedMethodUsesOnlyNameof_DoesNotWarn()
        {
            TransformWorkerClientResult result = await RunAsync(
                HostWithReader(AddedField + SkippedWriter, "return nameof(AddedValue).Length;"),
                ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            Assert.That(FindEntry(result, "Value"), Is.Not.Null);
            AssertNoWarning(result);
        }

        /// <summary>
        /// What: two applied readers of the same field still give one warning line, which names
        /// the first reader in declaration order and counts the rest.
        /// </summary>
        [Test]
        public async Task TwoAppliedReaders_WarnOnceForTheField()
        {
            string host = ReplaceInSource(
                HostWithReader(AddedField + SkippedWriter, "return AddedValue;"),
                "            return factor;",
                "            return factor + AddedValue;");
            TransformWorkerClientResult result = await RunAsync(host, ReadOnDisk(CallerFileName));

            AssertWriterSkipped(result);
            Assert.That(FindEntry(result, "Scaled"), Is.Not.Null);
            List<string> warnings = FindWarnings(result);
            Assert.That(warnings, Has.Count.EqualTo(1), string.Join(" | ", warnings));
            Assert.That(warnings[0], Does.Contain(HostLabelPrefix + "Value() and 1 more"));
        }

        private static string HostWithReader(string addedMembers, string valueBody)
        {
            string host = InsertIntoHostBody(ReadOnDisk(HostFileName), addedMembers);
            return ReplaceInSource(
                host,
                ValueBody,
                "        public int Value()\n        {\n            " + valueBody + "\n        }");
        }

        private static string InsertIntoHostBody(string hostSource, string memberText)
        {
            const string anchor = "        public int Value()";
            Assert.That(hostSource, Does.Contain(anchor), "Precondition: host anchor must exist.");
            return hostSource.Replace(anchor, memberText + anchor, StringComparison.Ordinal);
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ReadOnDisk(string fileName)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
        }

        private static void AssertWriterSkipped(TransformWorkerClientResult result)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, "AddedSetUp"), Is.Not.Null, FormatSkipped(result));
        }

        private static void AssertNoWarning(TransformWorkerClientResult result)
        {
            List<string> warnings = FindWarnings(result);
            Assert.That(warnings, Is.Empty, string.Join(" | ", warnings));
        }

        private static List<string> FindWarnings(TransformWorkerClientResult result)
        {
            List<string> warnings = new List<string>();
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (string warning in file.declarationDriftWarnings ?? Array.Empty<string>())
                {
                    if (warning.Contains(WarningMarker))
                    {
                        warnings.Add(warning);
                    }
                }
            }

            return warnings;
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

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto row in result.Output.skipped)
            {
                if (row.method != null && row.method.Contains("." + methodName + "("))
                {
                    return row;
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto row in result.Output.skipped)
            {
                lines.Add(row.method + " => " + HotReloadWorkerReasonText.Render(row.reason));
            }

            return "Skipped=[" + string.Join(" | ", lines) + "]";
        }

        private static async Task<TransformWorkerClientResult> RunAsync(string editedHostSource, string editedCallerSource)
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
                "SkippedWriterWarning");
            Directory.CreateDirectory(temporaryDirectory);
            string hostSourcePath = Path.Combine(temporaryDirectory, HostFileName);
            string callerSourcePath = Path.Combine(temporaryDirectory, CallerFileName);
            File.WriteAllText(hostSourcePath, editedHostSource);
            File.WriteAllText(callerSourcePath, editedCallerSource);

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = hostSourcePath,
                        projectRelativePath = "Assets/Tests/Editor/HotReload/" + HostFileName,
                        snapshotSource = ReadOnDisk(HostFileName)
                    },
                    new TransformWorkerSourceDto
                    {
                        sourcePath = callerSourcePath,
                        projectRelativePath = "Assets/Tests/Editor/HotReload/" + CallerFileName,
                        snapshotSource = ReadOnDisk(CallerFileName)
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
    }
}
