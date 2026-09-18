using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

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
    public class HotReloadIntroducedTypeBodyEditE2ETests : HotReloadIntroducedTypeE2ETestBase
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
        private const string IntroducedStructSimpleName = "HotReloadBodyEditIntroducedStruct";
        private const string IntroducedStructMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload." + IntroducedStructSimpleName;
        private const string IntroducedStructReturn = "5";
        private const string EditedStructReturn = "10";
        private const int IntroducedStructValue = 5;
        private const string StructHostSkipReason =
            "Struct (value type) methods are skipped; byref instance transplant is unverified.";

        // A constructor the introduced type declares, which hot reload cannot patch and therefore
        // reports. Kept out of the other reloads' declaration so only this test's fixture carries
        // it, and the fingerprints the existing tests compare stay what they were.
        private const string DeclaredConstructor =
            "        public " + IntroducedTypeSimpleName + "()\n"
            + "        {\n"
            + "        }\n"
            + "\n";

        // The member the removal test introduces first and drops afterwards.
        private const string RemovableMember =
            "        public int Twice()\n"
            + "        {\n"
            + "            return _seed * 2;\n"
            + "        }\n"
            + "\n";

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
        /// Verifies that a body edit of an already introduced type whose declaration has a
        /// constructor reports nothing about that constructor. The row used to appear on every
        /// reload of such a file and told the reader to compile a member they had not touched.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeWithConstructorBodyEdited_ReportsNoConstructorRow()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateIntroducingEditsWithConstructor(hostPath, callerPath));
                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult second = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateBodyEditedEditsWithConstructor(hostPath, callerPath));

                Assert.That(
                    CountPatchedIntroducedMethods(second),
                    Is.EqualTo(1),
                    "Precondition: the edited body must still be patched.\n"
                    + DescribeOutcomes(second));
                Assert.That(
                    FindSkippedConstructorMethods(second),
                    Is.Empty,
                    "The constructor of a type the domain already runs from an artifact was never "
                    + "edited, so no row may ask the reader to compile it.\n"
                    + DescribeOutcomes(second));
            });
        }

        /// <summary>
        /// Verifies that removing a member from an already introduced type asks for a compile: the
        /// type row fails with the changed-declaration reason, which names the type and lists the
        /// removal that made the reload refuse it. The retained assembly still holds the member, so
        /// a caller compiled against it would keep finding a definition the source no longer has.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeMemberRemoved_FailsAndAsksForACompile()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateIntroducingEditsWithTwoMembers(hostPath, callerPath));
                AssertComputedValue(
                    readArtifact(),
                    IntroducedSeed,
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult changed = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateMemberRemovedEdits(hostPath, callerPath));

                HotReloadIntroducedTypeOutcome outcome = FindIntroducedTypeOutcome(changed);
                Assert.That(
                    outcome.Kind,
                    Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed),
                    "Removing a member from an introduced type changes its declaration, which needs "
                    + "a compile.\n"
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
                    Does.Contain("removed:"),
                    "The differences must name the removed member.\n"
                    + DescribeOutcomes(changed));
            });
        }

        /// <summary>
        /// Verifies that editing a method body of an already introduced struct is skipped with the
        /// struct-host reason instead of being patched: struct methods are never transplanted, even
        /// on an introduced type, so the retained assembly keeps running the body the introducing
        /// reload compiled.
        /// </summary>
        [Test]
        public async Task Run_IntroducedStructMethodBodyEdited_ReportsTheStructHostSkipRow()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateStructEdits(
                    hostPath,
                    callerPath,
                    IntroducedStructReturn,
                    "IntroducedStructHost.cs",
                    "IntroducedStructCaller.cs"));
                Assert.That(
                    ReadStructValue(readArtifact()),
                    Is.EqualTo(IntroducedStructValue),
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult edited = await RunReloadAsync(hostPath, callerPath, CreateStructEdits(
                    hostPath,
                    callerPath,
                    EditedStructReturn,
                    "IntroducedStructEditedHost.cs",
                    "IntroducedStructEditedCaller.cs"));

                HotReloadMethodOutcome structRow = FindStructComputeOutcome(edited);
                Assert.That(
                    structRow.Kind,
                    Is.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                    "A method body of an introduced struct must be skipped, not patched.\n"
                    + DescribeOutcomes(edited));
                Assert.That(
                    structRow.Reason,
                    Does.Contain(StructHostSkipReason),
                    "The skip must name the struct-host limit so the reader knows to compile.\n"
                    + DescribeOutcomes(edited));
                Assert.That(
                    ReadStructValue(readArtifact()),
                    Is.EqualTo(IntroducedStructValue),
                    "A skipped struct body must leave the retained assembly running the original body.");
            });
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

        private static List<string> FindSkippedConstructorMethods(HotReloadOrchestratorResult result)
        {
            List<string> found = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(IntroducedTypeSimpleName + "..ctor", StringComparison.Ordinal))
                {
                    found.Add(outcome.Method);
                }
            }

            return found;
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

        private static HotReloadMethodOutcome FindStructComputeOutcome(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null
                    && outcome.Method.Contains(IntroducedStructSimpleName + ".Compute()", StringComparison.Ordinal))
                {
                    return outcome;
                }
            }

            Assert.Fail("The reload must report a row for " + IntroducedStructSimpleName + ".Compute().\n"
                + DescribeOutcomes(result));
            return null;
        }

        private static int ReadStructValue(HotReloadIntroducedTypeArtifact artifact)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to introduce the struct before this check.");
            Type introducedType = artifact.Assembly.GetType(IntroducedStructMetadataName, throwOnError: false);
            Assert.That(introducedType, Is.Not.Null, "The artifact must hold " + IntroducedStructMetadataName + ".");

            MethodInfo compute = introducedType.GetMethod("Compute", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(compute, Is.Not.Null, "The introduced struct must declare Compute().");

            return (int)compute.Invoke(Activator.CreateInstance(introducedType), Array.Empty<object>());
        }

        // Why the struct has no field: a struct field initializer needs a C# version Unity's
        // compiler does not accept, and the edit only has to change a body.
        private static Dictionary<string, string> CreateStructEdits(
            string hostPath,
            string callerPath,
            string returnedValue,
            string hostFileName,
            string callerFileName)
        {
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public struct " + IntroducedStructSimpleName + "\n"
                + "    {\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + returnedValue + ";\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    hostFileName,
                    hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    callerFileName,
                    CallType(File.ReadAllText(callerPath), IntroducedStructSimpleName))
            };
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

        // The introduced type declared with a constructor, so the reload that follows can be asked
        // what it reports about a member the retained assembly already runs.
        private static Dictionary<string, string> CreateIntroducingEditsWithConstructor(
            string hostPath,
            string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeCtorHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, DeclaredConstructor)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeCtorCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The same declaration with only the body of Compute() edited, which leaves the constructor
        // byte-identical to the one the introducing reload compiled.
        private static Dictionary<string, string> CreateBodyEditedEditsWithConstructor(
            string hostPath,
            string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeCtorEditedHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), EditedExpression, DeclaredConstructor)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeCtorEditedCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The declaration the removal is measured against: the introduced type with a second
        // member, so the reload that drops it has something the retained assembly still holds.
        private static Dictionary<string, string> CreateIntroducingEditsWithTwoMembers(
            string hostPath,
            string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTwoMemberHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, RemovableMember)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTwoMemberCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The same declaration with the second member gone, which is the change a reload cannot
        // apply without a compile: the member stays in the assembly the domain runs the type from.
        private static Dictionary<string, string> CreateMemberRemovedEdits(
            string hostPath,
            string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeMemberRemovedHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), SeedExpression, NoExtraMembers)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeMemberRemovedCaller.cs",
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
            return CallType(callerSource, IntroducedTypeSimpleName);
        }

        private static string CallType(string callerSource, string typeSimpleName)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new " + typeSimpleName + "().Compute() + host.Value();",
                StringComparison.Ordinal);
        }

    }
}
