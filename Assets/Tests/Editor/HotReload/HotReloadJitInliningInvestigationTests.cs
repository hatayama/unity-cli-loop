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
    /// Documents Mono's code-optimization-dependent inlining of tiny getters into warmed callers
    /// (PR-4 To-Do 12 measurement kept as a lasting regression).
    /// </summary>
    public class HotReloadJitInliningInvestigationTests
    {
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: after warming ReadProbe, patching Probe is visible under Debug and invisible
        /// under Release — matching the measured Mono inlining behavior that drives branch (a).
        /// </summary>
        [Test]
        public async Task SmallGetter_AfterCallerWarmup_PatchVisibilityMatchesCodeOptimizationMode()
        {
            CodeOptimization mode = CompilationPipeline.codeOptimization;
            string fixturePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "public static int Probe => 1;",
                "public static int Probe => 2;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: Probe body must differ.");

            int warmSum = 0;
            for (int index = 0; index < 64; index++)
            {
                warmSum += HotReloadJitInliningInvestigationFixture.ReadProbe();
            }

            Assert.That(warmSum, Is.EqualTo(64), "Precondition: warmed caller must see the compiled Probe.");

            string editedPath = WriteEditedSource("JitInliningInvestigationProbe.cs", editedSource);
            HotReloadOrchestratorResult patched = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            bool probePatched = false;
            foreach (HotReloadMethodOutcome outcome in patched.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains("get_Probe"))
                {
                    probePatched = true;
                    break;
                }
            }

            Assert.That(
                probePatched,
                Is.True,
                "Probe getter must patch.\n" + FormatOutcomes(patched));

            int afterPatch = HotReloadJitInliningInvestigationFixture.ReadProbe();
            if (mode == CodeOptimization.Release)
            {
                Assert.That(
                    afterPatch,
                    Is.EqualTo(1),
                    "Release: Mono inlines the tiny getter into the warmed caller, so the patch is invisible.");
            }
            else
            {
                Assert.That(
                    afterPatch,
                    Is.EqualTo(2),
                    "Debug: the warmed caller reaches the patched getter body.");
            }
        }

        private static string ResolveShapeFixturePath()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "Tests",
                    "Editor",
                    "HotReload",
                    "HotReloadShapeFixtures.cs"));
        }

        private static string WriteEditedSource(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
