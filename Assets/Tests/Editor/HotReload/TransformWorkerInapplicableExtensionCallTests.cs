using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Transform worker coverage for a call whose name only binds to a same-named public extension
    /// method that does not take it (CS1929), when no internal extension method of a file the run
    /// does not pass takes the call either.
    /// </summary>
    public class TransformWorkerInapplicableExtensionCallTests
    {
        private const string FixtureDirectory = "Assets/Tests/Editor/HotReload/";

        /// <summary>
        /// What: a lambda calling a same-named public extension method that does not take its
        /// receiver, with no internal extension method behind the call, is not skipped by the worker
        /// and stays an entry, so the shim compile settles it as before.
        /// </summary>
        [Test]
        public async Task LambdaCallingOnlyAnInapplicablePublicExtension_IsNotSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "HotReloadSameNameExtensionCaller",
                "() => 20",
                "() => 50L.AddDouble(1)");

            Assert.That(FindSkipReason(result, "LambdaValue("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(HasEntry(result, "LambdaValue"), Is.True, "LambdaValue must be an entry.");
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string typeName, string fragment, string replacement)
        {
            string fileName = typeName + ".cs";
            string fixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            string onDisk = File.ReadAllText(fixturePath);
            int first = onDisk.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Precondition: the fragment must be in the fixture.");
            Assert.That(
                onDisk.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Precondition: the fragment must occur once in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "InapplicableExtension" + typeName + ".cs",
                onDisk.Replace(fragment, replacement, StringComparison.Ordinal));
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureDirectory + fileName,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static bool HasEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return true;
                }
            }

            return false;
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
    }
}
