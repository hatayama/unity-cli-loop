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
    /// Worker coverage for methods that raise, read, or clear a field-like event: they become
    /// delegation entries whose event access is rewritten to a Harmony backing-field accessor,
    /// while += / -= keep using the publicized add/remove accessors.
    /// </summary>
    public class TransformWorkerEventAccessorTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        // The shim method's receiver parameter name, emitted by the transform worker.
        private const string InstanceParameterName = "__uloopInstance";

        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadEventAccessorHost.cs";

        private const string RaiseScoredOriginal =
            "        public void RaiseScored(int amount)\n        {\n            Total = amount;\n        }";

        private const string RaiseStaticScoredOriginal =
            "        public void RaiseStaticScored(int amount)\n        {\n            Total = amount;\n        }";

        private const string ClearScoredOriginal =
            "        public void ClearScored()\n        {\n            Total = 0;\n        }";

        private const string RaiseHiddenScoredOriginal =
            "        public void RaiseHiddenScored(int amount)\n        {\n            Total = amount;\n        }";

        private const string DelegationPatchKind = "delegation";

        private const string EventAccessorFieldName = "__EV_Scored";

        private const string StaticEventAccessorFieldName = "__EV_StaticScored";

        /// <summary>
        /// What: 'E?.Invoke(a)' turns the method into a delegation entry whose event read goes
        /// through the Harmony backing-field accessor instead of the event member.
        /// </summary>
        [Test]
        public async Task Rewrite_ConditionalInvoke_ReadsEventThroughBackingFieldAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventRaiseConditionalInvoke.cs",
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            Scored?.Invoke(amount);\n        }");

            TransformWorkerEntryDto entry = FindEntry(result, nameof(HotReloadEventAccessorHost.RaiseScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Raising a field-like event must patch, not skip. Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(entry.patchKind, Is.EqualTo(DelegationPatchKind));
            string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(slice, Does.Contain(EventAccessorFieldName + "("));
            Assert.That(result.Output.hasAccessorDelegates, Is.True);
        }

        /// <summary>
        /// What: the bare-delegate raise form 'E(a)' is rewritten the same way as E?.Invoke(a).
        /// </summary>
        [Test]
        public async Task Rewrite_DirectDelegateInvoke_ReadsEventThroughBackingFieldAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventRaiseDirectInvoke.cs",
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            Scored(amount);\n        }");

            TransformWorkerEntryDto entry = FindEntry(result, nameof(HotReloadEventAccessorHost.RaiseScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(entry.patchKind, Is.EqualTo(DelegationPatchKind));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, entry.shimMethodName),
                Does.Contain(EventAccessorFieldName + "("));
        }

        /// <summary>
        /// What: a null check on the event is a read as well, so 'if (E != null) E(a)' patches
        /// and both reads go through the accessor.
        /// </summary>
        [Test]
        public async Task Rewrite_NullCheckThenInvoke_ReadsEventThroughBackingFieldAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventRaiseNullCheck.cs",
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            if (Scored != null)\n            {\n"
                + "                Scored(amount);\n            }\n        }");

            TransformWorkerEntryDto entry = FindEntry(result, nameof(HotReloadEventAccessorHost.RaiseScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Skipped=" + FormatSkipped(result.Output.skipped));
            string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(slice, Does.Not.Contain(InstanceParameterName + ".Scored"));
            Assert.That(slice, Does.Contain(EventAccessorFieldName + "("));
        }

        /// <summary>
        /// What: a static field-like event binds through StaticFieldRefAccess, so the accessor
        /// call takes no receiver argument.
        /// </summary>
        [Test]
        public async Task Rewrite_StaticEventRaise_BindsThroughStaticFieldRefAccess()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventRaiseStatic.cs",
                RaiseStaticScoredOriginal,
                "        public void RaiseStaticScored(int amount)\n        {\n"
                + "            StaticScored?.Invoke(amount);\n        }");

            TransformWorkerEntryDto entry =
                FindEntry(result, nameof(HotReloadEventAccessorHost.RaiseStaticScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Skipped=" + FormatSkipped(result.Output.skipped));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, entry.shimMethodName),
                Does.Contain(StaticEventAccessorFieldName + "()"));
            Assert.That(result.Output.shimSource, Does.Contain("StaticFieldRefAccess"));
        }

        /// <summary>
        /// What: 'E = null' writes through the ref-returning accessor rather than the event
        /// member, which C# would reject outside the declaring type.
        /// </summary>
        [Test]
        public async Task Rewrite_NullAssignment_WritesThroughBackingFieldAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventClearAssignment.cs",
                ClearScoredOriginal,
                "        public void ClearScored()\n        {\n"
                + "            Scored = null;\n        }");

            TransformWorkerEntryDto entry = FindEntry(result, nameof(HotReloadEventAccessorHost.ClearScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Skipped=" + FormatSkipped(result.Output.skipped));
            string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(slice, Does.Contain(EventAccessorFieldName + "(" + InstanceParameterName + ") = null"));
        }

        /// <summary>
        /// What: when a body both subscribes and raises, the subscription keeps the publicized
        /// add accessor (preserving Interlocked semantics) while only the raise goes through the
        /// backing-field accessor.
        /// </summary>
        [Test]
        public async Task Rewrite_SubscribeAndRaiseInSameBody_KeepsSubscriptionOnEventAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventSubscribeAndRaise.cs",
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            Scored += OnScored;\n"
                + "            Scored -= OnScored;\n"
                + "            Scored += OnScored;\n"
                + "            Scored?.Invoke(amount);\n        }");

            TransformWorkerEntryDto entry = FindEntry(result, nameof(HotReloadEventAccessorHost.RaiseScored));
            Assert.That(
                entry,
                Is.Not.Null,
                "Skipped=" + FormatSkipped(result.Output.skipped));
            string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(slice, Does.Contain(InstanceParameterName + ".Scored += "));
            Assert.That(slice, Does.Contain(InstanceParameterName + ".Scored -= "));
            Assert.That(slice, Does.Contain(EventAccessorFieldName + "("));
            Assert.That(slice, Does.Not.Contain(EventAccessorFieldName + "(" + InstanceParameterName + ") +="));
        }

        /// <summary>
        /// What: an event whose delegate type is not visible outside the assembly is skipped,
        /// because the shim cannot name the accessor's field type.
        /// </summary>
        [Test]
        public async Task Skip_EventWithNonVisibleDelegateType_ReportsDelegateTypeReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventRaiseHiddenDelegateType.cs",
                RaiseHiddenScoredOriginal,
                "        public void RaiseHiddenScored(int amount)\n        {\n"
                + "            HiddenScored?.Invoke(amount);\n        }");

            AssertHasSkip(
                result,
                nameof(HotReloadEventAccessorHost.RaiseHiddenScored),
                "delegate type is not visible from an external assembly");
        }

        /// <summary>
        /// What: an event declared in this edit has no backing field in the compiled assembly, so
        /// the raiser is skipped with a reason that names compiling as the way forward.
        /// </summary>
        [Test]
        public async Task Skip_EventAddedInThisEdit_ReportsMissingCompiledBackingField()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Total;",
                "        public event HotReloadScoreDelegate AddedScored;\n\n        public int Total;",
                StringComparison.Ordinal);
            edited = ReplaceMember(
                edited,
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            AddedScored?.Invoke(amount);\n        }");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("EventRaiseAddedInThisEdit.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(
                result,
                nameof(HotReloadEventAccessorHost.RaiseScored),
                "the compiled assembly has no backing field yet");
        }

        /// <summary>
        /// What: nameof(E) stays Skipped — the shim is a different static type, so the bare event
        /// name it would have to keep does not resolve there.
        /// </summary>
        [Test]
        public async Task Skip_NameofEvent_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "EventNameofUse.cs",
                RaiseScoredOriginal,
                "        public void RaiseScored(int amount)\n        {\n"
                + "            Total = amount + nameof(Scored).Length;\n        }");

            AssertHasSkip(result, nameof(HotReloadEventAccessorHost.RaiseScored), "nameof");
        }

        private static async Task<TransformWorkerClientResult> RunEditedHostAsync(
            string editedFileName,
            string originalMember,
            string replacementMember)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = ReplaceMember(onDisk, originalMember, replacementMember);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited(editedFileName, edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static string ReplaceMember(string source, string originalMember, string replacementMember)
        {
            Assert.That(source, Does.Contain(originalMember), "Host fixture member drifted: " + originalMember);
            return source.Replace(originalMember, replacementMember, StringComparison.Ordinal);
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
                + reasonFragment + "'. Skipped=" + FormatSkipped(result.Output.skipped));
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
                Application.dataPath, "Tests", "Editor", "HotReload", "HotReloadEventAccessorHost.cs");
            Assert.That(File.Exists(path), Is.True, "Event accessor host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
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
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
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
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            List<string> paths = new List<string>();
            bool hasTarget = false;
            foreach (string reference in allReferences ?? Array.Empty<string>())
            {
                string full = Path.IsPathRooted(reference)
                    ? Path.GetFullPath(reference)
                    : Path.GetFullPath(Path.Combine(projectRoot, reference));
                if (string.Equals(full, Path.GetFullPath(targetDllPath), StringComparison.Ordinal))
                {
                    hasTarget = true;
                }

                paths.Add(full);
            }

            if (!hasTarget)
            {
                paths.Add(Path.GetFullPath(targetDllPath));
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string[] sourceFiles)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            List<string> paths = new List<string>();
            foreach (string sourceFile in sourceFiles ?? Array.Empty<string>())
            {
                paths.Add(Path.IsPathRooted(sourceFile)
                    ? Path.GetFullPath(sourceFile)
                    : Path.GetFullPath(Path.Combine(projectRoot, sourceFile)));
            }

            return paths.ToArray();
        }
    }
}
