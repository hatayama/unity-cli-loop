using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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

        // The one owner path a test writes to disk, because an omitted --files run only selects a
        // file that exists. The test deletes it before Unity could import it.
        private const string RevertedOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledRevertedIntroducedOwner.cs";

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
        private const string CallerDeclarationAnchor =
            "        [MethodImpl(MethodImplOptions.NoInlining)]\n        public int Call(";
        private const int UnwiredRead = -100;
        private const int ArrayWeight = 10;
        private const int ListWeight = 100;

        // The fields the wiring test adds to the caller: the introduced type itself, an array of
        // it, and a generic argument of it. No field is typed by a type nested in it, because a
        // type that declares a nested one is refused introduction outright.
        private static readonly string IntroducedTypedFieldMembers =
            "        public " + IntroducedTypeSimpleName + " AddedIntroduced;\n"
            + "        public " + IntroducedTypeSimpleName + "[] AddedIntroducedArray;\n"
            + "        public System.Collections.Generic.List<" + IntroducedTypeSimpleName + "> AddedIntroducedList;\n\n";

        private static readonly string IntroducedTypedFieldsReadExpression =
            "(AddedIntroduced == null ? " + UnwiredRead.ToString() + " : AddedIntroduced.Ping())"
            + " + (AddedIntroducedArray == null ? 0 : AddedIntroducedArray.Length * " + ArrayWeight.ToString() + ")"
            + " + (AddedIntroducedList == null ? 0 : AddedIntroducedList.Count * " + ListWeight.ToString() + ")";

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
                AssertWarningContaining(
                    third,
                    "1 re-applied sibling file(s) declare a type hot reload introduced",
                    "The re-applied file's missing baseline must be reported in the sibling summary line.");
                AssertNoWarningContaining(
                    third,
                    "UncompiledIntroducedOwner.cs declares a type hot reload introduced",
                    "A file the reload only pulled back in must not get a baseline line of its own, "
                    + "because it would repeat on every run until the next compile.");
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(EditedPingValue + (PongValue * PongFactor) + HostValue),
                    "The body this reload patched must run, which it can only do while the added "
                    + "member it calls is bound to this run's shim.\n"
                    + DescribeOutcomes(third));
            });
        }

        /// <summary>
        /// What: added fields typed by an introduced type (plain, array element, and generic
        /// argument) accept a wired value and the patched caller reads it, on the reload
        /// that introduces the type, on a reload that retains it unchanged, and on a reload that
        /// edits a body of the retained type. The last one keeps the declaration in the worker's
        /// binding tree, so the type is bound from source there and has to be named by the
        /// artifact that serves it; an instance made before that edit must still be accepted.
        /// </summary>
        [Test]
        public async Task Run_AddedFieldsTypedByAnIntroducedType_AreWiredAndReadAcrossRetainingReloads()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunIntroducedTypedFieldsReloadAsync(
                    callerPath, PingValue, "Introducing");
                AssertIntroducedTypeRow(introducing, HotReloadIntroducedTypeOutcomeKind.Introduced);
                object firstGenerationValue = AssertIntroducedTypedFieldsAreWiredAndRead(
                    readArtifact, null, PingValue, "the introducing reload");

                HotReloadOrchestratorResult retaining = await RunIntroducedTypedFieldsReloadAsync(
                    callerPath, PingValue, "Retaining");
                AssertIntroducedTypeRow(retaining, HotReloadIntroducedTypeOutcomeKind.AlreadyActive);
                AssertIntroducedTypedFieldsAreWiredAndRead(readArtifact, null, PingValue, "the retaining reload");

                HotReloadOrchestratorResult bodyEdit = await RunIntroducedTypedFieldsReloadAsync(
                    callerPath, EditedPingValue, "BodyEdit");
                AssertIntroducedTypeRow(bodyEdit, HotReloadIntroducedTypeOutcomeKind.AlreadyActive);
                AssertOutcome(bodyEdit, HotReloadMethodOutcomeKind.Patched, "Ping");
                AssertIntroducedTypedFieldsAreWiredAndRead(
                    readArtifact, firstGenerationValue, EditedPingValue, "the body-edit reload");
            });
        }

        /// <summary>
        /// What: after --revert-all, an omitted --files run selects the owner file of the type the
        /// revert left loaded again, so the caller that uses a member a later reload added to that
        /// type is patched instead of failing with CS1061. The revert drops that added member while
        /// the type stays loaded, and the file was never compiled, so without the reselection
        /// nothing would bring the member back.
        /// </summary>
        [Test]
        public async Task RevertAllThenOmittedFiles_SelectsTheOwnerFileAgainAndTheCallerReachesItsAddedMember()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string callerWithPong = HotReloadTestSourceWriter.WriteEditedSource(
                "RevertedOwnerCallerWithPong.cs",
                CallIntroducedType(
                    File.ReadAllText(callerPath),
                    "new " + IntroducedTypeSimpleName + "().Ping() + new "
                    + IntroducedTypeSimpleName + "().Pong() * " + PongFactor.ToString()));
            string ownerAbsolutePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", RevertedOwnerPath.Replace('/', Path.DirectorySeparatorChar)));
            HotReloadPlayModeEntryDropLedgerSessionScope ledgerScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
            try
            {
                File.WriteAllText(ownerAbsolutePath, BuildOwnerSource(EditedPingValue, PongMember));
                await RunInIntroducedTypeDomainAsync(async _ =>
                {
                    await RunReloadAsync(
                        RevertedOwnerPath,
                        callerPath,
                        new Dictionary<string, string>
                        {
                            [RevertedOwnerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                                "RevertedOwnerIntroducing.cs",
                                BuildOwnerSource(PingValue, NoExtraMembers)),
                            [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                                "RevertedOwnerCaller.cs",
                                CallIntroducedType(File.ReadAllText(callerPath), "new " + IntroducedTypeSimpleName + "().Ping()"))
                        });
                    await RunReloadAsync(
                        RevertedOwnerPath,
                        callerPath,
                        new Dictionary<string, string> { [callerPath] = callerWithPong });
                    Assert.That(
                        CallTheCaller(),
                        Is.EqualTo(EditedPingValue + (PongValue * PongFactor) + HostValue),
                        "Precondition: the second reload must have added the member the caller calls.");

                    HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();
                    Assert.That(CallTheCaller(), Is.EqualTo(HostValue), "Precondition: the revert must restore the compiled caller.");

                    HotReloadDefaultFileSelection selection = HotReloadDefaultFileSelector.Resolve(
                        null,
                        () => new HotReloadChangedFileAggregationResult(
                            hasBaseline: true,
                            changedProjectRelativePaths: new List<string> { callerPath },
                            scanLimitWarnings: new List<string>()),
                        HotReloadDroppedIntroducedSourceFiles.ListExistingOnDisk(),
                        path => HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                            HotReloadCompositionRoot.Services.PackageRootCapture,
                            path));

                    Assert.That(
                        selection.Files,
                        Is.EqualTo(new[] { callerPath, RevertedOwnerPath }),
                        "The owner file of the type the revert left loaded must be selected after the changed file.");
                    Assert.That(
                        selection.SelectionMessage,
                        Does.Contain("entering Play Mode or 'uloop hot-reload --revert-all' dropped what earlier reloads had applied from them: " + RevertedOwnerPath + "."));

                    HotReloadOrchestratorResult reselected = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                        selection.Files,
                        contentPathOverride: null,
                        CancellationToken.None,
                        new Dictionary<string, string> { [callerPath] = callerWithPong });

                    Assert.That(CountFailures(reselected), Is.EqualTo(0), DescribeOutcomes(reselected));
                    AssertIntroducedTypeRow(reselected, HotReloadIntroducedTypeOutcomeKind.AlreadyActive);
                    AssertOutcome(reselected, HotReloadMethodOutcomeKind.Added, "Pong");
                    Assert.That(
                        CallTheCaller(),
                        Is.EqualTo(EditedPingValue + (PongValue * PongFactor) + HostValue),
                        "The caller must reach the member the reselected owner file adds again.\n"
                        + DescribeOutcomes(reselected));
                });
            }
            finally
            {
                File.Delete(ownerAbsolutePath);
                ledgerScope.Restore();
            }
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

        // Every reload of the wiring test declares the same fields, so each one either introduces,
        // retains, or body-edits the type without also changing what the caller adds.
        private static Task<HotReloadOrchestratorResult> RunIntroducedTypedFieldsReloadAsync(
            string callerPath,
            int pingValue,
            string label)
        {
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerDeclarationAnchor), "Precondition: caller declaration anchor must exist.");
            string withFields = callerSource.Replace(
                CallerDeclarationAnchor,
                IntroducedTypedFieldMembers + CallerDeclarationAnchor,
                StringComparison.Ordinal);
            return RunReloadAsync(
                OwnerRequestedPath,
                callerPath,
                new Dictionary<string, string>
                {
                    [OwnerRequestedPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "IntroducedTypedFieldsOwner" + label + ".cs",
                        BuildOwnerSource(pingValue, NoExtraMembers)),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "IntroducedTypedFieldsCaller" + label + ".cs",
                        CallIntroducedType(withFields, IntroducedTypedFieldsReadExpression))
                });
        }

        // Wires all three fields and returns the value wired into the plain one, so a later reload
        // can hand an instance made before it back in. A null instance means "make a new one".
        private static object AssertIntroducedTypedFieldsAreWiredAndRead(
            Func<HotReloadIntroducedTypeArtifact> readArtifact,
            object instance,
            int expectedPing,
            string reload)
        {
            Type introducedType = readArtifact().Assembly.GetType(IntroducedTypeMetadataName, true);
            object value = instance ?? Activator.CreateInstance(introducedType);
            Array array = Array.CreateInstance(introducedType, 2);
            System.Collections.IList list = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(introducedType));
            list.Add(value);
            list.Add(value);
            list.Add(value);
            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();
            HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();
            Assert.That(caller.Call(host), Is.EqualTo(UnwiredRead + HostValue), "Precondition: nothing is wired yet after " + reload + ".");

            HotReloadAddedFieldWiring.SetInstanceField(caller, "AddedIntroduced", value);
            HotReloadAddedFieldWiring.SetInstanceField(caller, "AddedIntroducedArray", array);
            HotReloadAddedFieldWiring.SetInstanceField(caller, "AddedIntroducedList", list);

            Assert.That(
                caller.Call(host),
                Is.EqualTo(expectedPing + (2 * ArrayWeight) + (3 * ListWeight) + HostValue),
                "The patched caller must read every value wired after " + reload + ".");
            return value;
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
