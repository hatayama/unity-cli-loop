using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a body-only edit of a type an earlier reload introduced: the edited
    /// body is patched on the assembly that reload retained, instead of the run demanding a compile.
    /// </summary>
    /// <remarks>
    /// Why the introduced type reads a private field of its own: the shim compiled for the edited
    /// body lives in an assembly of its own, so it can only reach that field if the run publicizes
    /// the retained artifact the same way it publicizes a script assembly.
    /// </remarks>
    public class HotReloadIntroducedTypeBodyEditE2ETests
    {
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";
        private const string CallerBodyAnchor = "return host.Value();";
        private const string IntroducedTypeSimpleName = "HotReloadBodyEditIntroducedValue";
        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload." + IntroducedTypeSimpleName;
        private const int IntroducedSeed = 5;
        private const int EditedComputedValue = IntroducedSeed * 2;

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why reverting here as well: a run of this class patches a method of an artifact assembly
        // that only lives while the run's scope is open, so leaving the patch active would fail
        // the first later test that reaches that assembly.
        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// Verifies that editing only an ordinary method body of an already introduced type patches
        /// that body on the assembly the earlier reload retained: the type is reported AlreadyActive,
        /// the method is reported Patched, a reflection call on the retained type returns the edited
        /// value, and the message says how many bodies of the bound types were patched.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeMethodBodyEdited_PatchesTheBodyOnTheRetainedArtifact()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            HotReloadIntroducedTypeArtifact artifact = null;

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadServicesTestScope.BeginWithDependencies(collaborators =>
                    CreateArtifactCapturingDependencies(
                        collaborators,
                        prepared =>
                        {
                            if (prepared != null)
                            {
                                artifact = prepared;
                            }
                        })))
                {
                    HotReloadOrchestratorResult first =
                        await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                            new[] { hostPath, callerPath },
                            contentPathOverride: null,
                            CancellationToken.None,
                            CreateIntroducingEdits(hostPath, callerPath));

                    AssertCallerIsPatched(first);
                    Assert.That(artifact, Is.Not.Null, "The first reload had to introduce the type.");
                    Assert.That(
                        ReadComputedValue(artifact),
                        Is.EqualTo(IntroducedSeed),
                        "Precondition: the retained assembly must run the body the first reload compiled.");

                    HotReloadOrchestratorResult second =
                        await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                            new[] { hostPath, callerPath },
                            contentPathOverride: null,
                            CancellationToken.None,
                            CreateBodyEditedEdits(hostPath, callerPath));

                    Assert.That(
                        CountFailures(second),
                        Is.EqualTo(0),
                        "A body-only edit of an introduced type must not fail the reload.\n"
                        + DescribeOutcomes(second));
                    Assert.That(
                        CountPatchedIntroducedMethods(second),
                        Is.EqualTo(1),
                        "The edited body of the introduced type must be reported as patched.\n"
                        + DescribeOutcomes(second));

                    HotReloadResponse response = HotReloadApplyResponseBuilder.Build(
                        HotReloadCompositionRoot.Services,
                        second,
                        null);
                    Assert.That(
                        response.IntroducedTypes.Count,
                        Is.EqualTo(1),
                        "The second reload bound one declaration from the retained artifact.");
                    Assert.That(
                        response.IntroducedTypes[0].Kind,
                        Is.EqualTo("AlreadyActive"),
                        "A declaration bound from a retained artifact was not introduced by this run.");
                    Assert.That(
                        response.IntroducedTypes[0].TypeName,
                        Is.EqualTo(IntroducedTypeMetadataName),
                        "The type row must name the declaration the reload bound.");
                    Assert.That(
                        response.Message,
                        Does.Contain("bound 1 introduced type"),
                        "The message must say the declaration came from an assembly already held.");
                    Assert.That(
                        response.Message,
                        Does.Contain("1 method body(ies) were patched"),
                        "The message must say how many bodies this reload patched.");
                    Assert.That(
                        response.Message,
                        Does.Not.Contain("no method body needed patching"),
                        "A reload that patched a body must not claim none needed patching.");

                    Assert.That(
                        ReadComputedValue(artifact),
                        Is.EqualTo(EditedComputedValue),
                        "A call into the retained assembly must run the edited body.");
                }
            }
        }

        // Why the preparation stage is the only one wrapped: the test reads the edited body back
        // through the artifact assembly, and the prepared artifact is the only handle on it.
        private static HotReloadGroupProcessorDependencies CreateArtifactCapturingDependencies(
            HotReloadGroupStageCollaborators collaborators,
            Action<HotReloadIntroducedTypeArtifact> captureArtifact)
        {
            return HotReloadGroupProcessorDependencies.Create(
                files => HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(collaborators, files),
                async (files, input, ct) =>
                {
                    HotReloadIntroducedTypePreparationResult preparation =
                        await HotReloadIntroducedTypePreparation.PrepareAsync(collaborators, files, input, ct);
                    captureArtifact(preparation.Prepared?.Artifact);
                    return preparation;
                },
                HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync,
                (context, ct) => HotReloadGroupProcessor.GateAndCompileAsync(collaborators, context, ct),
                (context, compileResult, entriesToPatch) => HotReloadGroupEntryPreparation.PrepareGroup(
                    collaborators, context, compileResult, entriesToPatch),
                HotReloadCompositionRoot.Services.EntryApplier.ApplyPreparedEntries);
        }

        private static int ReadComputedValue(HotReloadIntroducedTypeArtifact artifact)
        {
            Type introducedType = artifact.Assembly.GetType(IntroducedTypeMetadataName, throwOnError: false);
            Assert.That(introducedType, Is.Not.Null, "The artifact must hold " + IntroducedTypeMetadataName + ".");

            MethodInfo compute = introducedType.GetMethod(
                "Compute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(compute, Is.Not.Null, "The introduced type must declare Compute().");

            return (int)compute.Invoke(Activator.CreateInstance(introducedType), Array.Empty<object>());
        }

        private static int CountPatchedIntroducedMethods(HotReloadOrchestratorResult result)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched || outcome.Method == null)
                {
                    continue;
                }

                if (outcome.Method.Contains(
                        IntroducedTypeMetadataName + "::Compute",
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountFailures(HotReloadOrchestratorResult result)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    count++;
                }
            }

            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertCallerIsPatched(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method != null
                    && outcome.Method.Contains("Call", StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail("The caller edited against the introduced type must be patched.\n"
                + DescribeOutcomes(result));
        }

        private static string DescribeOutcomes(HotReloadOrchestratorResult result)
        {
            string description = "Methods:";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                description += "\n  " + outcome.Kind + " " + outcome.Method + " " + outcome.Reason;
            }

            description += "\nIntroducedTypes:";
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                description += "\n  " + outcome.Kind + " " + outcome.MetadataName + " " + outcome.Reason;
            }

            return description;
        }

        private static Dictionary<string, string> CreateIntroducingEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyEditHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), IntroducedSeed.ToString())),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyEditCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The same declaration with only the body of Compute() edited, which is what the reload
        // has to patch on the assembly the first reload retained.
        private static Dictionary<string, string> CreateBodyEditedEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyEditedHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), "_seed * 2")),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyEditedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why NoInlining: the test reads the patched body back through a reflection call, and the
        // declaration must stay byte-identical between the two reloads apart from the body itself.
        private static string InsertIntroducedType(string hostSource, string computedExpression)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        private readonly int _seed = " + IntroducedSeed.ToString() + ";\n"
                + "\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + computedExpression + ";\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        private static string CallIntroducedType(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new " + IntroducedTypeSimpleName + "().Compute() + host.Value();",
                StringComparison.Ordinal);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }
    }
}
