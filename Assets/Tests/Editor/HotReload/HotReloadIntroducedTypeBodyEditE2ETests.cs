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
        private const string SeedExpression = "_seed";
        private const string EditedExpression = "_seed * 2";
        private const string NoExtraMembers = "";

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

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateIntroducingEdits(hostPath, callerPath));

                AssertCallerIsPatched(first);
                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult second = await RunReloadAsync(
                    hostPath,
                    callerPath,
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

                HotReloadResponse response = BuildResponse(second);
                AssertBoundOneDeclaration(response);
                Assert.That(
                    response.Message,
                    Does.Contain("bound 1 introduced type"),
                    "The message must say the declaration came from an assembly already held.");
                Assert.That(
                    response.Message,
                    Does.Contain("2 method body(ies) were patched"),
                    "The message must count every body this reload patched: the edited body of "
                    + "the introduced type and the caller edited against it.");
                Assert.That(
                    response.Message,
                    Does.Not.Contain("no method body needed patching"),
                    "A reload that patched a body must not claim none needed patching.");

                AssertComputedValue(
                    readArtifact(),
                    EditedComputedValue,
                    "A call into the retained assembly must run the edited body.");
            });
        }

        /// <summary>
        /// Verifies that restoring an edited body of an already introduced type back to the source the
        /// introducing reload compiled removes the patch the previous reload installed: the type is
        /// still reported AlreadyActive, no body of the introduced type is reported as patched, and a
        /// reflection call on the retained type returns the value the introducing reload compiled.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeMethodBodyRestored_RevertsThePatchOnTheRetainedArtifact()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                await RunReloadAsync(hostPath, callerPath, CreateBodyEditedEdits(hostPath, callerPath));
                AssertComputedValue(
                    readArtifact(),
                    EditedComputedValue,
                    "Precondition: the second reload must have patched the edited body.");

                HotReloadOrchestratorResult third = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateRestoredEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(third),
                    Is.EqualTo(0),
                    "Restoring the body of an introduced type must not fail the reload.\n"
                    + DescribeOutcomes(third));
                Assert.That(
                    CountPatchedIntroducedMethods(third),
                    Is.EqualTo(0),
                    "A body that matches the retained assembly must not be reported as patched.\n"
                    + DescribeOutcomes(third));
                AssertBoundOneDeclaration(BuildResponse(third));

                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "A restored body must leave the retained assembly running its own code again, "
                    + "which means the patch the previous reload installed has to be reverted.");
            });
        }

        /// <summary>
        /// Verifies that adding a member to an already introduced type still asks for a compile: the
        /// type row fails with the changed-declaration reason, which names the type and lists the
        /// declaration difference that made the reload refuse it.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeDeclarationChanged_FailsAndAsksForACompile()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult changed = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateDeclarationChangedEdits(hostPath, callerPath));

                HotReloadIntroducedTypeOutcome outcome = FindIntroducedTypeOutcome(changed);
                Assert.That(
                    outcome.Kind,
                    Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed),
                    "Adding a member to an introduced type changes its declaration, which needs a "
                    + "compile.\n"
                    + DescribeOutcomes(changed));
                Assert.That(
                    outcome.Reason,
                    Does.StartWith("Changed introduced type requires a compile: " + IntroducedTypeMetadataName),
                    "The reason must name the type whose declaration changed.\n"
                    + DescribeOutcomes(changed));
                Assert.That(
                    outcome.Reason,
                    Does.Contain("Declaration differences: "),
                    "The reason must hand over the differences the comparison found.\n"
                    + DescribeOutcomes(changed));
                Assert.That(
                    outcome.Reason,
                    Does.Contain("added:"),
                    "The differences must name the added member.\n"
                    + DescribeOutcomes(changed));
            });
        }

        // Why one helper owns both scopes: every reload of a run has to see the same domain and the
        // same captured artifact, and the artifact assembly only lives while the scopes are open.
        private static async Task RunInIntroducedTypeDomainAsync(
            Func<Func<HotReloadIntroducedTypeArtifact>, Task> runReloads)
        {
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
                    await runReloads(() => artifact);
                }
            }
        }

        private static Task<HotReloadOrchestratorResult> RunReloadAsync(
            string hostPath,
            string callerPath,
            Dictionary<string, string> edits)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath, callerPath },
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private static HotReloadResponse BuildResponse(HotReloadOrchestratorResult result)
        {
            return HotReloadApplyResponseBuilder.Build(HotReloadCompositionRoot.Services, result, null);
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

        private static void AssertComputedValue(
            HotReloadIntroducedTypeArtifact artifact,
            int expected,
            string because)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to introduce the type before this check.");
            Assert.That(ReadComputedValue(artifact), Is.EqualTo(expected), because);
        }

        private static void AssertBoundOneDeclaration(HotReloadResponse response)
        {
            Assert.That(
                response.IntroducedTypes.Count,
                Is.EqualTo(1),
                "The reload bound one declaration from the retained artifact.");
            Assert.That(
                response.IntroducedTypes[0].Kind,
                Is.EqualTo("AlreadyActive"),
                "A declaration bound from a retained artifact was not introduced by this run.");
            Assert.That(
                response.IntroducedTypes[0].TypeName,
                Is.EqualTo(IntroducedTypeMetadataName),
                "The type row must name the declaration the reload bound.");
        }

        private static HotReloadIntroducedTypeOutcome FindIntroducedTypeOutcome(
            HotReloadOrchestratorResult result)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.MetadataName == IntroducedTypeMetadataName)
                {
                    return outcome;
                }
            }

            Assert.Fail("The reload must report a row for " + IntroducedTypeMetadataName + ".\n"
                + DescribeOutcomes(result));
            return null;
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
                        IntroducedTypeMetadataName + ".Compute()",
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
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, NoExtraMembers)),
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
                    InsertIntroducedType(File.ReadAllText(hostPath), EditedExpression, NoExtraMembers)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyEditedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why distinct file names for the same source as the introducing reload: the run has to be
        // told about an edit, and reusing the first reload's path would let a cached read decide
        // the answer instead of the body comparison this test is about.
        private static Dictionary<string, string> CreateRestoredEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyRestoredHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, NoExtraMembers)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeBodyRestoredCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The declaration of the introduced type with one member more than the retained assembly
        // holds, which is the change a reload cannot apply without a compile.
        private static Dictionary<string, string> CreateDeclarationChangedEdits(
            string hostPath,
            string callerPath)
        {
            string extraMember =
                "        public int Twice()\n"
                + "        {\n"
                + "            return _seed * 2;\n"
                + "        }\n"
                + "\n";
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeDeclarationChangedHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, extraMember)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeDeclarationChangedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why NoInlining: the test reads the patched body back through a reflection call, and the
        // declaration must stay byte-identical between the two reloads apart from the body itself.
        private static string InsertIntroducedType(
            string hostSource,
            string computedExpression,
            string extraMembers)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        private readonly int _seed = " + IntroducedSeed.ToString() + ";\n"
                + "\n"
                + extraMembers
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
