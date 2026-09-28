using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of code that names an internal or modifier-less introduced type while a
    /// later reload edits a body of that type. That reload keeps the declaration in the worker to
    /// transform it, and whatever names the type must be judged against the public artifact the
    /// domain runs, exactly as on a reload that leaves the declaration alone.
    /// </summary>
    public sealed class HotReloadIntroducedTypeRetainedInternalReferrerE2ETests : HotReloadIntroducedTypeCallerE2ETestBase
    {
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/UncompiledRetainedInternalReferrerOwner.cs";
        private const string HostValueAnchor = "        public int Value()";
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";
        private const string AddedFieldName = "AddedRetained";

        // What a reload that binds the retained artifact through its public surface reports for a
        // body naming one of its internal members.
        private const string UnboundBodyReason = "could not be fully bound";

        // Why NoInlining on the bodies a later reload edits: the test reads the edited body back
        // through a direct call, which an inlined copy at the call site would not observe.
        private const string NoInlining =
            "[System.Runtime.CompilerServices.MethodImpl("
            + "System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

        // One added method whose body names the retained type and one whose return type is it.
        private static readonly string AddedMethodsNamingTheReferent =
            "        public int AddedReadsRetained()\n"
            + "        {\n"
            + "            return new RetainedReferent().Read() * 10;\n"
            + "        }\n"
            + "\n"
            + "        public RetainedReferent AddedMakesRetained()\n"
            + "        {\n"
            + "            return new RetainedReferent();\n"
            + "        }\n"
            + "\n";

        private static readonly string AddedFieldOfTheReferent =
            "        public RetainedFieldValue " + AddedFieldName + ";\n"
            + "\n"
            + "        public int AddedReadsRetainedField()\n"
            + "        {\n"
            + "            return " + AddedFieldName + " == null ? -100 : " + AddedFieldName + ".Read() * 10;\n"
            + "        }\n"
            + "\n";

        // The members the second reload adds to the retained type itself: a method reading an
        // added private field, an auto-property, and a bodied property reading the auto-property.
        // Both properties arrive as added accessor methods.
        private static readonly string GrownMembers =
            "\n"
            + "        private int _extra = 3;\n"
            + "\n"
            + "        public int Extra() { return _extra; }\n"
            + "\n"
            + "        public int Count { get; set; }\n"
            + "\n"
            + "        public int Doubled { get { return Count * 2; } }\n";

        // An added method of a compiled type whose body calls an internal method of the retained type.
        private static readonly string AddedMethodCallingTheSecret =
            "        public int AddedUsesSecret()\n"
            + "        {\n"
            + "            return new RetainedHelper().Secret();\n"
            + "        }\n"
            + "\n";

        // Added methods of a compiled type that each call one accessor of a retained type's
        // properties, where every property keeps one accessor non-public.
        private static readonly string AddedMethodsCallingAccessors =
            "        public int AddedWritesGuardedSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            accessors.GuardedSetter = 5;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedReadsGuardedGetter()\n"
            + "        {\n"
            + "            return new RetainedAccessors().GuardedGetter;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedCompoundsGuardedGetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            accessors.GuardedGetter += 5;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedDeconstructsIntoGuardedSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            int other;\n"
            + "            (accessors.GuardedSetter, other) = (5, 6);\n"
            + "            return other;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedFillsGuardedGetterItems()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors { GuardedItems = { 7 } };\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedIncrementsGuardedSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            accessors.GuardedSetter++;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedWritesParenthesizedGuardedSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            (accessors.GuardedSetter) = 5;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedIncrementsParenthesizedGuardedSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            (accessors.GuardedSetter)++;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedWritesGuardedRef()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            accessors.GuardedRef = 5;\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedNamesGuardedGetter()\n"
            + "        {\n"
            + "            return nameof(RetainedAccessors.GuardedGetter).Length;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedReadsPublicGetter()\n"
            + "        {\n"
            + "            return new RetainedAccessors().GuardedSetter + 40;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedWritesPublicSetter()\n"
            + "        {\n"
            + "            RetainedAccessors accessors = new RetainedAccessors();\n"
            + "            accessors.GuardedGetter = 5;\n"
            + "            return 2;\n"
            + "        }\n"
            + "\n";

        private static readonly string[] MethodsCallingNonPublicAccessors =
        {
            "AddedWritesGuardedSetter",
            "AddedReadsGuardedGetter",
            "AddedCompoundsGuardedGetter",
            "AddedDeconstructsIntoGuardedSetter",
            "AddedFillsGuardedGetterItems",
            "AddedIncrementsGuardedSetter",
            "AddedWritesParenthesizedGuardedSetter",
            "AddedIncrementsParenthesizedGuardedSetter",
            "AddedWritesGuardedRef"
        };

        // Why nameof counts here: it calls no accessor and binds wherever the property is kept,
        // which the artifact import does while one accessor is public.
        private static readonly string[] MethodsCallingPublicAccessors =
        {
            "AddedReadsPublicGetter",
            "AddedWritesPublicSetter",
            "AddedNamesGuardedGetter"
        };

        // A compiled type's added auto-property and the added method that writes and reads it.
        private static readonly string AddedPropertyOnTheHost =
            "        public int AddedCount { get; set; }\n"
            + "\n"
            + "        public int AddedUsesCount()\n"
            + "        {\n"
            + "            AddedCount = 4;\n"
            + "            return AddedCount;\n"
            + "        }\n"
            + "\n";

        private static readonly string AddedMethodsNamingEachDeclaration =
            "        public int AddedReadsFirst()\n"
            + "        {\n"
            + "            return new RetainedFirstEdited().Read() * 10;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedReadsUnchanged()\n"
            + "        {\n"
            + "            return new RetainedUnchanged().Read() * 100;\n"
            + "        }\n"
            + "\n"
            + "        public int AddedReadsSecond()\n"
            + "        {\n"
            + "            return new RetainedSecondEdited().Read() * 1000;\n"
            + "        }\n"
            + "\n";

        /// <summary>
        /// Verifies that editing a body of a retained internal or modifier-less introduced type
        /// keeps the methods a compiled type added around it applied: the one whose body names the
        /// type and the one that returns it stay added, and both run the edited body.
        /// </summary>
        [TestCase("internal")]
        [TestCase("modifierless")]
        public async Task Run_RetainedTypeBodyEdit_KeepsAddedMethodsNamingItApplied(string access)
        {
            const string callerExpression = "host.AddedReadsRetained() + host.AddedMakesRetained().Read()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "AddedMethodsFirst" + access,
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedReferent(access, 1)), AddedMethodsNamingTheReferent));
                AssertIntroduced(first, "RetainedReferent");
                AssertOutcome(first, HotReloadMethodOutcomeKind.Added, "AddedReadsRetained");
                AssertOutcome(first, HotReloadMethodOutcomeKind.Added, "AddedMakesRetained");
                Assert.That(CallTheCaller(), Is.EqualTo(11), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "AddedMethodsSecond" + access,
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedReferent(access, 2)), AddedMethodsNamingTheReferent));

                AssertAppliedWithoutSkips(edited);
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedReferent.Read");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Added, "AddedReadsRetained");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Added, "AddedMakesRetained");
                Assert.That(CallTheCaller(), Is.EqualTo(22), DescribeOutcomes(edited));
            });
        }

        /// <summary>
        /// Verifies that editing a body of a retained internal introduced type keeps a field a
        /// compiled type added with that type registered: the value wired before the edit is still
        /// stored, and the added method reading it runs the edited body.
        /// </summary>
        [Test]
        public async Task Run_RetainedTypeBodyEdit_KeepsAnAddedFieldOfItRegistered()
        {
            const string callerExpression = "host.AddedReadsRetainedField()";
            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "AddedFieldFirst",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedFieldValue(1)), AddedFieldOfTheReferent));
                AssertIntroduced(first, "RetainedFieldValue");
                HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();
                object wired = Activator.CreateInstance(
                    readArtifact().Assembly.GetType(Namespace + ".RetainedFieldValue", true));
                HotReloadAddedFieldWiring.SetInstanceField(host, AddedFieldName, wired);
                Assert.That(new HotReloadCrossFileAddedMemberCaller().Call(host), Is.EqualTo(10), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "AddedFieldSecond",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedFieldValue(2)), AddedFieldOfTheReferent));

                AssertAppliedWithoutSkips(edited);
                string description = DescribeOutcomes(edited);
                Assert.That(edited.AddedFields, Has.Length.EqualTo(1), description);
                Assert.That(edited.AddedFields[0], Does.Contain(AddedFieldName), description);
                Assert.That(
                    HotReloadAddedFieldWiring.TryReadInstanceField(host, AddedFieldName, out object stored),
                    Is.True,
                    description);
                Assert.That(stored, Is.SameAs(wired), description);
                Assert.That(new HotReloadCrossFileAddedMemberCaller().Call(host), Is.EqualTo(20), description);
            });
        }

        /// <summary>
        /// Verifies that editing the body of a retained introduced type that raises its own
        /// field-like event is patched the same way whether the type was written internal or
        /// public, and the subscriber receives the argument of the edited body.
        /// </summary>
        [TestCase("internal")]
        [TestCase("public")]
        public async Task Run_RetainedTypeRaisingItsEvent_IsPatchedLikeAPublicType(string access)
        {
            const string callerExpression = "new RetainedPublisherProbe().Run()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "EventFirst" + access,
                    callerExpression,
                    Owner(OwnerPath, RetainedPublisher(access, 1)));
                AssertIntroduced(first, "RetainedPublisher");
                Assert.That(CallTheCaller(), Is.EqualTo(1), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "EventSecond" + access,
                    callerExpression,
                    Owner(OwnerPath, RetainedPublisher(access, 2)));

                AssertAppliedWithoutSkips(edited);
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedPublisher.Fire");
                Assert.That(CallTheCaller(), Is.EqualTo(2), DescribeOutcomes(edited));
            });
        }

        /// <summary>
        /// Verifies that adding a method, a field, an auto-property and a bodied property to a
        /// retained introduced type, with an edited body that goes through all of them, ends the
        /// same whether the type was written internal or public: every addition is added and the
        /// body runs them.
        /// </summary>
        [TestCase("internal")]
        [TestCase("public")]
        public async Task Run_MembersAddedToRetainedType_AreAppliedLikeOnAPublicType(string access)
        {
            const string callerExpression = "new RetainedGrowing().Read()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "GrowingFirst" + access,
                    callerExpression,
                    Owner(OwnerPath, RetainedGrowing(access, "return 1;", string.Empty)));
                AssertIntroduced(first, "RetainedGrowing");
                Assert.That(CallTheCaller(), Is.EqualTo(1), DescribeOutcomes(first));

                HotReloadOrchestratorResult grown = await RunAsync(
                    "GrowingSecond" + access,
                    callerExpression,
                    Owner(OwnerPath, RetainedGrowing(access, "Count = Extra(); return Doubled * 5;", GrownMembers)));

                AssertAppliedWithoutSkips(grown);
                AssertOutcome(grown, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedGrowing.Read");
                AssertOutcome(grown, HotReloadMethodOutcomeKind.Added, "Extra");
                AssertOutcome(grown, HotReloadMethodOutcomeKind.Added, "get_Count");
                AssertOutcome(grown, HotReloadMethodOutcomeKind.Added, "set_Count");
                AssertOutcome(grown, HotReloadMethodOutcomeKind.Added, "get_Doubled");
                Assert.That(CallTheCaller(), Is.EqualTo(30), DescribeOutcomes(grown));
            });
        }

        /// <summary>
        /// Verifies that one file holding two edited internal or modifier-less declarations and an
        /// unchanged one keeps every added method naming them applied, so the declarations the
        /// worker keeps and the one it drops are each bound as the public type the domain runs.
        /// </summary>
        [Test]
        public async Task Run_EditedAndUnchangedDeclarationsInOneFile_KeepEveryReferrerApplied()
        {
            const string callerExpression =
                "host.AddedReadsFirst() + host.AddedReadsUnchanged() + host.AddedReadsSecond()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "MixedFirst",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, MixedDeclarations(1, 4)), AddedMethodsNamingEachDeclaration));
                AssertIntroduced(first, "RetainedFirstEdited");
                AssertIntroduced(first, "RetainedUnchanged");
                AssertIntroduced(first, "RetainedSecondEdited");
                Assert.That(CallTheCaller(), Is.EqualTo(4310), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "MixedSecond",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, MixedDeclarations(2, 5)), AddedMethodsNamingEachDeclaration));

                AssertAppliedWithoutSkips(edited);
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedFirstEdited.Read");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedSecondEdited.Read");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Added, "AddedReadsFirst");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Added, "AddedReadsUnchanged");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Added, "AddedReadsSecond");
                Assert.That(CallTheCaller(), Is.EqualTo(5320), DescribeOutcomes(edited));
            });
        }

        /// <summary>
        /// Verifies that a compiled type sharing a file with a retained internal introduced type
        /// gets its added auto-property applied, whether the reload leaves that declaration
        /// unchanged or edits a body of it: the worker binds the file from a rewritten copy in
        /// both cases, and the property accessors must still be emitted from that copy.
        /// </summary>
        [TestCase("unchanged")]
        [TestCase("bodyEdit")]
        public async Task Run_CompiledNeighbourOfRetainedType_GetsItsAddedPropertyApplied(string edit)
        {
            int readValue = edit == "bodyEdit" ? 2 : 1;
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "NeighbourFirst" + edit,
                    "new RetainedNeighbour().Read()",
                    HostWithNeighbour(1, string.Empty));
                AssertIntroduced(first, "RetainedNeighbour");
                Assert.That(CallTheCaller(), Is.EqualTo(1), DescribeOutcomes(first));

                HotReloadOrchestratorResult second = await RunAsync(
                    "NeighbourSecond" + edit,
                    "new RetainedNeighbour().Read() + host.AddedUsesCount()",
                    HostWithNeighbour(readValue, AddedPropertyOnTheHost));

                AssertAppliedWithoutSkips(second);
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "get_AddedCount");
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "set_AddedCount");
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "AddedUsesCount");
                Assert.That(CallTheCaller(), Is.EqualTo(readValue + 4), DescribeOutcomes(second));
            });
        }

        /// <summary>
        /// Verifies that a method a compiled type adds is never applied while its body calls an
        /// internal method of a retained introduced type: it is skipped when the type is introduced
        /// in the same reload, when a later reload edits a body of the type, and when a reload
        /// leaves the type's file out and binds the artifact, so no unrelated reload drops a method
        /// an earlier one applied.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodCallingInternalMemberOfRetainedType_IsSkippedOnEveryReload()
        {
            const string callerExpression = "new RetainedHelper().Read()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    "SecretFirst",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedHelper(1)), AddedMethodCallingTheSecret));
                AssertIntroduced(introducing, "RetainedHelper");
                AssertOutcome(introducing, HotReloadMethodOutcomeKind.Skipped, "AddedUsesSecret");
                AssertReasonContains(introducing, "AddedUsesSecret", UnboundBodyReason);

                HotReloadOrchestratorResult edited = await RunAsync(
                    "SecretSecond",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedHelper(2)), AddedMethodCallingTheSecret));
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedHelper.Read");
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Skipped, "AddedUsesSecret");
                AssertReasonContains(edited, "AddedUsesSecret", "a non-public member of a type hot reload introduced");
                Assert.That(CallTheCaller(), Is.EqualTo(2), DescribeOutcomes(edited));

                HotReloadOrchestratorResult hostOnly = await RunAsync(
                    "SecretThird",
                    callerExpression,
                    WithHostAdditions(new Dictionary<string, string>(), AddedMethodCallingTheSecret));
                AssertOutcome(hostOnly, HotReloadMethodOutcomeKind.Skipped, "AddedUsesSecret");
                AssertReasonContains(hostOnly, "AddedUsesSecret", UnboundBodyReason);
            });
        }

        /// <summary>
        /// Verifies that methods a compiled type adds around properties of a retained introduced
        /// type that keep one accessor non-public are judged by the accessors they call, the same
        /// way on every reload: a write, a parenthesized write, an increment, a parenthesized
        /// increment, or a deconstruction through an internal setter, a read, a compound
        /// assignment, or a nested collection initializer through an internal getter, and a write
        /// through an internal ref-returning property are skipped each time, while a read or a
        /// write that calls only the public accessor, and a nameof of a property with an internal
        /// getter, are applied and run each time.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodsUsingAccessorsOfRetainedType_AreJudgedByTheAccessorsTheyCall()
        {
            const string callerExpression =
                "host.AddedReadsPublicGetter() + host.AddedWritesPublicSetter() + host.AddedNamesGuardedGetter()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    "AccessorsFirst",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedAccessors(1)), AddedMethodsCallingAccessors));
                AssertIntroduced(introducing, "RetainedAccessors");
                AssertAccessorCallers(introducing, UnboundBodyReason);

                HotReloadOrchestratorResult edited = await RunAsync(
                    "AccessorsSecond",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, RetainedAccessors(2)), AddedMethodsCallingAccessors));
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedAccessors.Read");
                AssertAccessorCallers(edited, "a non-public member of a type hot reload introduced");

                HotReloadOrchestratorResult hostOnly = await RunAsync(
                    "AccessorsThird",
                    callerExpression,
                    WithHostAdditions(new Dictionary<string, string>(), AddedMethodsCallingAccessors));
                AssertAccessorCallers(hostOnly, UnboundBodyReason);
            });
        }

        /// <summary>
        /// Verifies that a retained internal introduced type with a public method whose signature
        /// uses an internal type of the compiled assembly still has a body edit patched: binding it
        /// as public exposes that signature, and nothing about the edit depends on it.
        /// </summary>
        [Test]
        public async Task Run_RetainedTypeWithInternalTypeInASignature_BodyEditIsPatched()
        {
            const string callerExpression = "new RetainedInternalSignature().Read()";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "InternalSignatureFirst",
                    callerExpression,
                    Owner(OwnerPath, RetainedInternalSignature(1)));
                AssertIntroduced(first, "RetainedInternalSignature");
                Assert.That(CallTheCaller(), Is.EqualTo(1), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "InternalSignatureSecond",
                    callerExpression,
                    Owner(OwnerPath, RetainedInternalSignature(2)));

                AssertAppliedWithoutSkips(edited);
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".RetainedInternalSignature.Read");
                Assert.That(CallTheCaller(), Is.EqualTo(2), DescribeOutcomes(edited));
            });
        }

        // The modifier-less shape puts a doc comment and an attribute above the header, so the
        // header the worker rewrites is not the first token of the declaration. Why not
        // [Serializable]: a serializable type is never introduced at all.
        private static string RetainedReferent(string access, int value)
        {
            string header = access == "internal"
                ? "internal sealed class RetainedReferent"
                : "/// <summary>Read by the methods a compiled type adds.</summary>\n"
                    + "    [System.Diagnostics.DebuggerDisplay(\"Retained\")]\n"
                    + "    sealed class RetainedReferent";
            return header + "\n    {\n        " + NoInlining + "public int Read() { return " + value.ToString() + "; }\n    }";
        }

        // The declaration the first reload introduces holds only Read, and the second one appends
        // the added members after it.
        private static string RetainedGrowing(string access, string readBody, string addedMembers)
        {
            return access + " sealed class RetainedGrowing\n"
                + "    {\n"
                + "        " + NoInlining + "public int Read() { " + readBody + " }\n"
                + addedMembers
                + "    }";
        }

        private static string RetainedFieldValue(int value)
        {
            return "internal sealed class RetainedFieldValue { "
                + NoInlining + "public int Read() { return " + value.ToString() + "; } }";
        }

        // Why the probe subscribes a method rather than a lambda: the probe must stay unchanged
        // between the reloads, and only the publisher's raising body is edited.
        private static string RetainedPublisher(string access, int argument)
        {
            return access + " sealed class RetainedPublisher\n"
                + "    {\n"
                + "        public event System.Action<int> Raised;\n"
                + "\n"
                + "        " + NoInlining + "public void Fire()\n"
                + "        {\n"
                + "            System.Action<int> raised = Raised;\n"
                + "            if (raised != null) { raised(" + argument.ToString() + "); }\n"
                + "        }\n"
                + "    }\n"
                + "\n"
                + "    " + access + " sealed class RetainedPublisherProbe\n"
                + "    {\n"
                + "        private int _received;\n"
                + "\n"
                + "        private void Record(int value) { _received = value; }\n"
                + "\n"
                + "        public int Run()\n"
                + "        {\n"
                + "            RetainedPublisher publisher = new RetainedPublisher();\n"
                + "            publisher.Raised += Record;\n"
                + "            publisher.Fire();\n"
                + "            return _received;\n"
                + "        }\n"
                + "    }";
        }

        // Why this order: the header that grows (an access modifier inserted) comes first and the
        // one that shrinks (internal made public) last, with the dropped declaration between them,
        // so a rewrite that shifted a later position by an earlier change would break the file.
        private static string MixedDeclarations(int firstValue, int secondValue)
        {
            return "sealed class RetainedFirstEdited { "
                + NoInlining + "public int Read() { return " + firstValue.ToString() + "; } }\n"
                + "\n"
                + "    internal sealed class RetainedUnchanged { public int Read() { return 3; } }\n"
                + "\n"
                + "    internal sealed class RetainedSecondEdited { "
                + NoInlining + "public int Read() { return " + secondValue.ToString() + "; } }";
        }

        private static string RetainedHelper(int value)
        {
            return "internal sealed class RetainedHelper\n"
                + "    {\n"
                + "        internal int Secret() { return 5; }\n"
                + "\n"
                + "        " + NoInlining + "public int Read() { return " + value.ToString() + "; }\n"
                + "    }";
        }

        private static string RetainedAccessors(int value)
        {
            return "internal sealed class RetainedAccessors\n"
                + "    {\n"
                + "        public int GuardedSetter { get; internal set; }\n"
                + "\n"
                + "        public int GuardedGetter { internal get; set; }\n"
                + "\n"
                + "        public System.Collections.Generic.List<int> GuardedItems { internal get; set; }\n"
                + "\n"
                + "        private int _refTarget;\n"
                + "\n"
                + "        internal ref int GuardedRef => ref _refTarget;\n"
                + "\n"
                + "        " + NoInlining + "public int Read() { return " + value.ToString() + "; }\n"
                + "    }";
        }

        private static string RetainedInternalSignature(int value)
        {
            return "internal sealed class RetainedInternalSignature\n"
                + "    {\n"
                + "        " + NoInlining + "public int Read() { return " + value.ToString() + "; }\n"
                + "\n"
                + "        public int Take(HotReloadInternalAccessBase reader) { return 0; }\n"
                + "    }";
        }

        // The compiled host file with an internal introduced type declared above the host class,
        // and the given members added to the host.
        private static Dictionary<string, string> HostWithNeighbour(int readValue, string hostMembers)
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            string neighbour = "    internal sealed class RetainedNeighbour { "
                + NoInlining + "public int Read() { return " + readValue.ToString() + "; } }\n\n";
            return new Dictionary<string, string>
            {
                [hostPath] = hostSource
                    .Replace(HostTypeAnchor, neighbour + HostTypeAnchor, StringComparison.Ordinal)
                    .Replace(HostValueAnchor, hostMembers + HostValueAnchor, StringComparison.Ordinal)
            };
        }

        // Adds the members to a copy of the compiled public host, which every run of a test names
        // again so the additions are judged on each of them.
        private static Dictionary<string, string> WithHostAdditions(Dictionary<string, string> sources, string members)
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            sources[hostPath] = hostSource.Replace(HostValueAnchor, members + HostValueAnchor, StringComparison.Ordinal);
            return sources;
        }

        private static void AssertAppliedWithoutSkips(HotReloadOrchestratorResult result)
        {
            string description = DescribeOutcomes(result);
            Assert.That(CountFailures(result), Is.Zero, description);
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(outcome.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Skipped), description);
            }
        }

        private static void AssertOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind
                    && outcome.Method != null
                    && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail("No " + kind + " row mentions " + methodFragment + ".\n" + DescribeOutcomes(result));
        }

        // Each method calling a non-public accessor is skipped for the given reason, and the methods
        // calling only public accessors are added and return what the caller sums.
        private static void AssertAccessorCallers(HotReloadOrchestratorResult result, string skipReasonFragment)
        {
            foreach (string method in MethodsCallingNonPublicAccessors)
            {
                AssertOutcome(result, HotReloadMethodOutcomeKind.Skipped, method);
                AssertReasonContains(result, method, skipReasonFragment);
            }

            foreach (string method in MethodsCallingPublicAccessors)
            {
                AssertOutcome(result, HotReloadMethodOutcomeKind.Added, method);
            }

            Assert.That(CallTheCaller(), Is.EqualTo(40 + 2 + "GuardedGetter".Length), DescribeOutcomes(result));
        }

        private static void AssertReasonContains(
            HotReloadOrchestratorResult result,
            string methodFragment,
            string reasonFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    Assert.That(outcome.Reason, Does.Contain(reasonFragment), DescribeOutcomes(result));
                    return;
                }
            }

            Assert.Fail("No row mentions " + methodFragment + ".\n" + DescribeOutcomes(result));
        }
    }
}
