using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a type introduced from a file the last compile never listed: the
    /// declaration lives only in the reload's own copy of the source, so nothing but the reload
    /// itself can say which assembly it belongs to.
    /// </summary>
    /// <remarks>
    /// Why the owner is not a fixture on disk: a .cs under Assets/ is compiled into the test
    /// assembly, which would make it one of the files Unity vouches for and would never exercise
    /// the path this class is about.
    /// </remarks>
    public class HotReloadIntroducedTypeNewFileSiblingE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        // The path the reload is asked for. Nothing is ever written here.
        private const string OwnerRequestedPath =
            "Assets/Tests/Editor/HotReload/UncompiledIntroducedOwner.cs";

        private const string CallerBodyAnchor = "return host.Value();";
        private const string IntroducedTypeSimpleName = "HotReloadNewFileIntroducedValue";
        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload." + IntroducedTypeSimpleName;
        private const int PingValue = 4;
        private const int EditedPingValue = 6;
        private const int PongValue = 9;
        private const int PongFactor = 2;
        private const int HostValue = 1;
        private const string NoExtraMembers = "";

        // The member the second reload adds to the introduced type, which lands in that reload's
        // shim rather than in the artifact the first reload retained.
        private static readonly string PongMember =
            "\n"
            + "        [System.Runtime.CompilerServices.MethodImpl(\n"
            + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
            + "        public int Pong()\n"
            + "        {\n"
            + "            return " + PongValue.ToString() + ";\n"
            + "        }\n";

        /// <summary>
        /// What: a type declared in a file outside the compiled source list is introduced by the
        /// first reload and takes a member from the second, with the caller bound to each in turn.
        /// This is the only shape in which hot reload introduces a type, and the reloads that
        /// follow it can only be judged once it is known to work.
        /// </summary>
        [Test]
        public async Task Run_TypeIntroducedFromAFileTheCompilerDoesNotList_TakesAMemberOnTheNextReload()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunReloadAsync(
                    OwnerRequestedPath,
                    callerPath,
                    CreateIntroducingEdits(callerPath));

                Assert.That(
                    CountFailures(first),
                    Is.EqualTo(0),
                    "Introducing a type from a file the compiler does not list must not fail.\n"
                    + DescribeOutcomes(first));
                AssertIntroducedTypeRow(first, HotReloadIntroducedTypeOutcomeKind.Introduced);
                AssertCallerIsPatched(first);
                Assert.That(readArtifact(), Is.Not.Null, "The reload must retain the artifact it introduced the type into.");
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(PingValue + HostValue),
                    "The patched caller must reach the introduced type.\n" + DescribeOutcomes(first));
                Assert.That(
                    first.Warnings,
                    Is.Empty,
                    "A reload that only introduces the type has no patched body in that file, so "
                    + "it has nothing to say about the baseline it does not have.\n"
                    + string.Join("\n", first.Warnings));

                HotReloadOrchestratorResult second = await RunReloadAsync(
                    OwnerRequestedPath,
                    callerPath,
                    CreateMemberAddedEdits(callerPath));

                Assert.That(
                    CountFailures(second),
                    Is.EqualTo(0),
                    "Adding a member to a type introduced from such a file must not fail.\n"
                    + DescribeOutcomes(second));
                AssertIntroducedTypeRow(second, HotReloadIntroducedTypeOutcomeKind.AlreadyActive);
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "Pong");
                AssertNoWarningContaining(
                    second,
                    "patching all methods",
                    "No compile could give this file a baseline, so the run must not report its "
                    + "absence as something 'uloop compile' would fix.");
                AssertWarningContaining(
                    second,
                    "declares a type hot reload introduced",
                    "The run must explain why a file declaring an introduced type has no baseline.");
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(EditedPingValue + PongValue + HostValue),
                    "The caller must reach the member the second reload added.\n"
                    + DescribeOutcomes(second));
            });
        }

        /// <summary>
        /// What: a later reload that names only the caller re-applies the file declaring the
        /// introduced type on its own, so the body it patches still binds to the member added to
        /// that type. Nothing in the compiled source list names that file, so a reload that did
        /// not pull it back in would leave the caller bound to the previous run's shim.
        /// </summary>
        [Test]
        public async Task Run_CallerReloadedWithoutTheDeclarationFile_ReappliesThatFileWithTheGroup()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await RunReloadAsync(OwnerRequestedPath, callerPath, CreateIntroducingEdits(callerPath));
                await RunReloadAsync(OwnerRequestedPath, callerPath, CreateMemberAddedEdits(callerPath));
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(EditedPingValue + PongValue + HostValue),
                    "Precondition: the second reload must have added the member the caller calls.");

                HotReloadOrchestratorResult third = await RunCallerOnlyReloadAsync(callerPath);

                Assert.That(
                    CountFailures(third),
                    Is.EqualTo(0),
                    "A reload of the caller alone must not lose the declaration it was compiled "
                    + "against, even though the file holding it was not passed.\n"
                    + DescribeOutcomes(third));
                AssertCallerIsPatched(third);
                AssertWarningContaining(
                    third,
                    "Also re-applied 1 unchanged file(s)",
                    "The run must say it pulled the declaration file back in.");
                AssertWarningContaining(
                    third,
                    "UncompiledIntroducedOwner.cs",
                    "The re-applied file must be named, so the reader can tell which one it was.");
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(EditedPingValue + (PongValue * PongFactor) + HostValue),
                    "The body this reload patched must run, which it can only do while the added "
                    + "member it calls is bound to this run's shim.\n"
                    + DescribeOutcomes(third));
            });
        }

        // Why not RunReloadAsync: this reload is the one that does not name the file declaring
        // the type, which is the whole point of it. The declaration's content stays in the
        // override map because the file never exists on disk; a run that pulls it back in as a
        // sibling reads it from there the way it would read the file a user has.
        private static Task<HotReloadOrchestratorResult> RunCallerOnlyReloadAsync(string callerPath)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { callerPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [OwnerRequestedPath] = WriteOwnerWithPong(),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "NewFileIntroducedCallerScaledPong.cs",
                        CallIntroducedType(
                            File.ReadAllText(callerPath),
                            "new " + IntroducedTypeSimpleName + "().Ping() + new "
                            + IntroducedTypeSimpleName + "().Pong() * " + PongFactor.ToString()))
                });
        }

        private static string WriteOwnerWithPong()
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                "NewFileIntroducedOwnerWithPong.cs",
                BuildOwnerSource(EditedPingValue, PongMember));
        }

        private static int CallTheCaller()
        {
            return new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
        }

        private static Dictionary<string, string> CreateIntroducingEdits(string callerPath)
        {
            return new Dictionary<string, string>
            {
                [OwnerRequestedPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "NewFileIntroducedOwner.cs",
                    BuildOwnerSource(PingValue, NoExtraMembers)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "NewFileIntroducedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath), "new " + IntroducedTypeSimpleName + "().Ping()"))
            };
        }

        private static Dictionary<string, string> CreateMemberAddedEdits(string callerPath)
        {
            return new Dictionary<string, string>
            {
                [OwnerRequestedPath] = WriteOwnerWithPong(),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "NewFileIntroducedCallerWithPong.cs",
                    CallIntroducedType(
                        File.ReadAllText(callerPath),
                        "new " + IntroducedTypeSimpleName + "().Ping() + new "
                        + IntroducedTypeSimpleName + "().Pong()"))
            };
        }

        // Why NoInlining: the test reads the values back through a direct call on the patched
        // caller, which an inlined copy at the call site would not observe.
        private static string BuildOwnerSource(int pingValue, string extraMembers)
        {
            return
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Ping()\n"
                + "        {\n"
                + "            return " + pingValue.ToString() + ";\n"
                + "        }\n"
                + extraMembers
                + "    }\n"
                + "}\n";
        }

        private static string CallIntroducedType(string callerSource, string expression)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return " + expression + " + host.Value();",
                StringComparison.Ordinal);
        }

        private static void AssertIntroducedTypeRow(
            HotReloadOrchestratorResult result,
            HotReloadIntroducedTypeOutcomeKind expected)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.MetadataName != IntroducedTypeMetadataName)
                {
                    continue;
                }

                Assert.That(
                    outcome.Kind,
                    Is.EqualTo(expected),
                    "The type row must say what the reload did with the declaration.\n"
                    + DescribeOutcomes(result));
                return;
            }

            Assert.Fail("The reload must report a row for " + IntroducedTypeMetadataName + ".\n"
                + DescribeOutcomes(result));
        }

        private static void AssertCallerIsPatched(HotReloadOrchestratorResult result)
        {
            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, "Call");
        }

        private static void AssertOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind
                    && outcome.Method != null
                    && outcome.Method.Contains(methodNamePart, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected a " + kind + " row for a method named like '" + methodNamePart + "'.\n"
                + DescribeOutcomes(result));
        }

        private static void AssertWarningContaining(
            HotReloadOrchestratorResult result,
            string textPart,
            string because)
        {
            foreach (string warning in result.Warnings)
            {
                if (warning != null && warning.Contains(textPart, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail(because + "\nExpected a warning containing '" + textPart + "'.\n"
                + string.Join("\n", result.Warnings));
        }

        private static void AssertNoWarningContaining(
            HotReloadOrchestratorResult result,
            string textPart,
            string because)
        {
            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning == null || !warning.Contains(textPart, StringComparison.Ordinal),
                    Is.True,
                    because + "\nUnexpected warning: " + warning);
            }
        }
    }
}
