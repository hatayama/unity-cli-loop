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
    /// Transform worker coverage for a target assembly whose only InterpolatedStringHandlerAttribute
    /// is its own internal declaration in a file the run does not pass. The fixture assembly
    /// references no other assembly that declares the attribute, which is the user layout.
    /// </summary>
    public class TransformWorkerHandlerAttributeTests
    {
        private const string FixtureAssemblyName = "UnityCLILoop.Tests.Editor.HotReloadHandlerAttributeFixture";
        private const string FixtureDirectoryName = "HotReloadHandlerAttributeFixture";
        private const string FixtureFileName = "HotReloadHandlerAttributeFixture.cs";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/" + FixtureDirectoryName + "/" + FixtureFileName;

        /// <summary>
        /// What: a lambda or async body whose handler has an internal AppendFormatted is skipped with
        /// the implicit-call reason naming AppendFormatted, although the attribute's file is not
        /// passed, and produces no entry.
        /// </summary>
        [TestCase("1105", "ClosureBuildsHandler")]
        [TestCase("1115", "AsyncBuildsHandler")]
        public async Task HandlerWithInternalAttributeInUnpassedFile_IsSkippedWithTheImplicitCallReason(
            string token,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, "." + methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                HotReloadWorkerReasonText.Render(skipped.reason),
                Does.Contain(
                    "inaccessible method 'AppendFormatted' that the compiler calls for an interpolated string handler"
                    + " has no accessor rewrite shape."));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", FixtureDirectoryName, FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(token, token.Substring(0, 3) + "6", StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("HandlerAttribute" + token + ".cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk,
                targetAssemblyName: FixtureAssemblyName);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
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

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodNameFragment))
                {
                    return skipped;
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
