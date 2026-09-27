using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of introduced declarations that are internal or carry no access
    /// modifier: they are introduced as public artifact types, keep their source fingerprint, and
    /// follow the rules of compiled internal types when their bodies are edited later.
    /// </summary>
    public sealed class HotReloadIntroducedTypeInternalDeclarationE2ETests : HotReloadIntroducedTypeCallerE2ETestBase
    {
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/UncompiledInternalDeclarationOwner.cs";
        private const string BodyEditAnchor = "return 1;";
        private const int InternalValue = 21;

        // Why NoInlining on the bodies a later reload edits: the test reads the edited body back
        // through the patched caller, which an inlined copy at the call site would not observe.
        private const string NoInlining =
            "[System.Runtime.CompilerServices.MethodImpl("
            + "System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

        /// <summary>
        /// Verifies that an internal declaration reading an internal member of the compiled assembly
        /// is introduced without a compile, and that the caller patched against it returns the value.
        /// </summary>
        [Test]
        public async Task Run_InternalTypeReadingTargetInternals_IsIntroducedAndReachable()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "InternalReader",
                    "new InternalReader().Read()",
                    Owner(OwnerPath,
                        "internal sealed class InternalReader { "
                        + "public int Read() { return HotReloadInternalAccessFixture.Read(); } }"));

                AssertIntroduced(result, "InternalReader");
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore + 1), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that a declaration without an access modifier can derive from an internal base
        /// of the compiled assembly, and that the base's compiled member dispatches to its override.
        /// </summary>
        [Test]
        public async Task Run_ModifierlessTypeDerivingFromInternalBase_OverridesThroughVirtualDispatch()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "ModifierlessDerived",
                    "new ModifierlessReader().ReadThroughBase()",
                    Owner(OwnerPath,
                        "sealed class ModifierlessReader : HotReloadInternalAccessBase { "
                        + "public override int Read() { return 11; } }"));

                AssertIntroduced(result, "ModifierlessReader");
                Assert.That(CallTheCaller(), Is.EqualTo(111), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that an enum declared without an access modifier is introduced and that the
        /// caller can name both the type and its member.
        /// </summary>
        [Test]
        public async Task Run_ModifierlessEnum_IsIntroducedAndNamedByCaller()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "ModifierlessEnum",
                    "typeof(ModifierlessKind).Name.Length + (int)ModifierlessKind.Second",
                    Owner(OwnerPath, "enum ModifierlessKind { First = 3, Second = 4 }"));

                AssertIntroduced(result, "ModifierlessKind");
                Assert.That(CallTheCaller(), Is.EqualTo("ModifierlessKind".Length + 4), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that rerunning an unchanged internal declaration binds it from the artifact the
        /// first run retained instead of introducing it again.
        /// </summary>
        [Test]
        public async Task Run_UnchangedInternalType_IsAlreadyActive()
        {
            const string declaration =
                "internal sealed class KeptInternal { public int Read() { return 6; } }";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "KeptFirst", "new KeptInternal().Read()", Owner(OwnerPath, declaration));
                AssertIntroduced(first, "KeptInternal");
                int activeAfterFirst = ActiveTypeCount();

                HotReloadOrchestratorResult second = await RunAsync(
                    "KeptSecond", "new KeptInternal().Read()", Owner(OwnerPath, declaration));

                Assert.That(CountFailures(second), Is.Zero, DescribeOutcomes(second));
                Assert.That(
                    HasIntroducedTypeRow(second, HotReloadIntroducedTypeOutcomeKind.AlreadyActive, "KeptInternal"),
                    Is.True,
                    DescribeOutcomes(second));
                Assert.That(
                    HasIntroducedTypeRow(second, HotReloadIntroducedTypeOutcomeKind.Introduced, "KeptInternal"),
                    Is.False,
                    DescribeOutcomes(second));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeAfterFirst), DescribeOutcomes(second));
                Assert.That(CallTheCaller(), Is.EqualTo(6), DescribeOutcomes(second));
            });
        }

        /// <summary>
        /// Verifies that making an introduced internal declaration public is a change of its
        /// declaration, which requires a compile and keeps the type the first run introduced.
        /// </summary>
        [Test]
        public async Task Run_InternalTypeChangedToPublic_RequiresCompile()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "FlippedFirst",
                    "new Flipped().Read()",
                    Owner(OwnerPath, "internal sealed class Flipped { public int Read() { return 6; } }"));
                AssertIntroduced(first, "Flipped");
                int activeAfterFirst = ActiveTypeCount();

                HotReloadOrchestratorResult flipped = await RunAsync(
                    "FlippedSecond",
                    "new Flipped().Read()",
                    Owner(OwnerPath, "public sealed class Flipped { public int Read() { return 6; } }"));

                string reason = FindFailedIntroducedTypeReason(flipped, "Changed introduced type requires a compile");
                Assert.That(reason, Is.Not.Null, DescribeOutcomes(flipped));
                Assert.That(reason, Does.Contain(Namespace + ".Flipped"), DescribeOutcomes(flipped));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeAfterFirst), DescribeOutcomes(flipped));
                Assert.That(CallTheCaller(), Is.EqualTo(6), DescribeOutcomes(flipped));
            });
        }

        /// <summary>
        /// Verifies that a top-level declaration with an access modifier C# forbids there is not
        /// made public for the artifact: the artifact compilation rejects it and nothing activates.
        /// </summary>
        [TestCase("private")]
        [TestCase("protected")]
        public async Task Run_InvalidTopLevelAccessibility_IsNotRepaired(string access)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "Invalid" + access,
                    "new InvalidAccess().Read()",
                    Owner(OwnerPath, access + " sealed class InvalidAccess { public int Read() { return 6; } }"));

                string reason = FindFailedIntroducedTypeReason(result, CompilationFailurePrefix);
                Assert.That(reason, Is.Not.Null, DescribeOutcomes(result));
                Assert.That(reason, Does.Contain("CS1527"), DescribeOutcomes(result));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(HostValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that editing the body of an introduced internal type ends like the same edit of
        /// a compiled internal type: a plain body is patched on both, and a closure reaching an
        /// internal member is skipped on both for the same reason.
        /// </summary>
        [TestCase("plain")]
        [TestCase("closure")]
        public async Task Run_InternalIntroducedBodyEdit_MatchesCompiledInternalType(string shape)
        {
            string editedBody = shape == "plain"
                ? "return 2;"
                : "System.Func<int> read = () => HotReloadInternalAccessFixture.Read(); return read();";
            const string callerExpression =
                "new IntroducedBodyEdit().Read() * 10 + new HotReloadInternalBodyEditFixture().Read()";
            string fixturePath = FixturePath("HotReloadInternalAccessFixtures.cs");
            string fixtureSource = File.ReadAllText(fixturePath);
            Assert.That(
                fixtureSource.IndexOf(BodyEditAnchor, StringComparison.Ordinal),
                Is.EqualTo(fixtureSource.LastIndexOf(BodyEditAnchor, StringComparison.Ordinal)),
                "Precondition: only the compiled fixture's body may hold the anchor.");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "BodyEditFirst" + shape,
                    callerExpression,
                    Owner(OwnerPath, IntroducedBodyEdit(BodyEditAnchor)));
                AssertIntroduced(first, "IntroducedBodyEdit");
                Assert.That(CallTheCaller(), Is.EqualTo(11), DescribeOutcomes(first));

                Dictionary<string, string> sources = Owner(OwnerPath, IntroducedBodyEdit(editedBody));
                sources[fixturePath] = fixtureSource.Replace(BodyEditAnchor, editedBody, StringComparison.Ordinal);
                HotReloadOrchestratorResult edited = await RunAsync("BodyEditSecond" + shape, callerExpression, sources);

                string description = DescribeOutcomes(edited);
                HotReloadMethodOutcome introducedRow = FindMethodRow(edited, Namespace + ".IntroducedBodyEdit.Read");
                HotReloadMethodOutcome compiledRow = FindMethodRow(edited, Namespace + ".HotReloadInternalBodyEditFixture.Read");
                Assert.That(introducedRow, Is.Not.Null, description);
                Assert.That(compiledRow, Is.Not.Null, description);
                Assert.That(introducedRow.Kind, Is.EqualTo(compiledRow.Kind), description);
                if (shape == "plain")
                {
                    Assert.That(compiledRow.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), description);
                    Assert.That(CallTheCaller(), Is.EqualTo(22), description);
                    return;
                }

                Assert.That(compiledRow.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), description);
                Assert.That(compiledRow.Reason, Does.Contain("Lambda, local-function, or query-expression bodies"), description);
                Assert.That(introducedRow.Reason, Is.EqualTo(compiledRow.Reason), description);
                Assert.That(CallTheCaller(), Is.EqualTo(11), description);
            });
        }

        /// <summary>
        /// Verifies that without an available grant an internal declaration that needs no internal
        /// member is still introduced, from the raw references and without asking the grant.
        /// </summary>
        [Test]
        public async Task Run_GrantUnavailable_InternalPlainTypeIsIntroduced()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: false);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "UnavailableInternalPlain",
                    "new InternalPlain().Read()",
                    Owner(OwnerPath, "internal sealed class InternalPlain { public int Read() { return 8; } }"));

                AssertIntroduced(result, "InternalPlain");
                Assert.That(CallTheCaller(), Is.EqualTo(8), DescribeOutcomes(result));
            }, grant);

            Assert.That(grant.GrantCalls, Is.Zero);
        }

        /// <summary>
        /// Verifies that without an available grant an internal declaration reading an internal
        /// member fails with the compiler's diagnostic from the raw references, and nothing activates.
        /// </summary>
        [Test]
        public async Task Run_GrantUnavailable_InternalTypeReadingInternalsFails()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: false);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "UnavailableInternalReader",
                    "new UnavailableInternalReader().Read()",
                    Owner(OwnerPath,
                        "internal sealed class UnavailableInternalReader { "
                        + "public int Read() { return HotReloadInternalAccessFixture.Read(); } }"));

                string reason = FindFailedIntroducedTypeReason(result, CompilationFailurePrefix);
                Assert.That(reason, Is.Not.Null, DescribeOutcomes(result));
                Assert.That(reason, Does.Contain("CS0122"), DescribeOutcomes(result));
                Assert.That(reason, Does.Contain("HotReloadInternalAccessFixture"), DescribeOutcomes(result));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(HostValue), DescribeOutcomes(result));
            }, grant);

            Assert.That(grant.GrantCalls, Is.Zero);
        }

        private static string IntroducedBodyEdit(string body)
        {
            return "internal sealed class IntroducedBodyEdit { " + NoInlining + "public int Read() { " + body + " } }";
        }

        private static bool HasIntroducedTypeRow(
            HotReloadOrchestratorResult result,
            HotReloadIntroducedTypeOutcomeKind kind,
            string simpleName)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == kind && outcome.MetadataName == Namespace + "." + simpleName)
                {
                    return true;
                }
            }

            return false;
        }

        private static HotReloadMethodOutcome FindMethodRow(HotReloadOrchestratorResult result, string methodPrefix)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.StartsWith(methodPrefix, StringComparison.Ordinal))
                {
                    return outcome;
                }
            }

            return null;
        }
    }
}
