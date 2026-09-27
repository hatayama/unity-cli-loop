using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of introduced types that reach internal members of the assembly they
    /// are introduced into: they compile against internals-exposed references, run once granted,
    /// and fail without touching the active types when the grant is unavailable or refused.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public sealed class HotReloadIntroducedTypeInternalAccessE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/UncompiledInternalAccessOwner.cs";
        private const string LaterOwnerPath = "Assets/Tests/Editor/HotReload/UncompiledInternalAccessLaterOwner.cs";
        private const string UnexposableOwnerPath = "Assets/Tests/Editor/HotReload/UncompiledUnexposableOwner.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/HotReloadInternalAccessFixtures.cs";
        private const string FixtureConstantDeclaration = "internal const int Constant = 21;";
        private const string CallerBodyAnchor = "return host.Value();";
        private const string CompilationFailurePrefix = "Introduced-type compilation failed: ";
        private const string ExposureFailureReason = "Exposing internal members of the referenced assemblies failed";
        private const string ExposedReferenceDirectory = "InternalsExposedRefs";
        private const string GrantRefusalReason = "refused by the test";
        private const int InternalValue = 21;
        private const int HostValue = 1;

        // Why NoInlining on the bodies a later reload edits: the test reads the edited body back
        // through the patched caller, which an inlined copy at the call site would not observe.
        private const string NoInlining =
            "[System.Runtime.CompilerServices.MethodImpl("
            + "System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

        private const string InternalStaticReader =
            "public sealed class InternalStaticReader { "
            + "public int Read() { return HotReloadInternalAccessFixture.Read(); } }";

        /// <summary>
        /// Verifies that a public introduced type reading an internal static member of the compiled
        /// assembly is introduced without a compile, and that the caller patched against it returns
        /// the value the internal member holds.
        /// </summary>
        [Test]
        public async Task Run_PublicTypeReadingInternalStaticMember_IsIntroducedAndReturnsTheValue()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "InternalStatic",
                    "new InternalStaticReader().Read()",
                    Owner(OwnerPath, InternalStaticReader));

                AssertIntroduced(result, "InternalStaticReader");
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore + 1), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that exposing the internal members of the compiled assembly leaves its private
        /// members private: the declaration fails to compile naming the private member, and the
        /// caller and the active types stay as they were.
        /// </summary>
        /// <remarks>
        /// Why the error code is not pinned: the compiler does not import the private members of a
        /// referenced assembly, so it reports the member as missing (CS0117) where a compilation
        /// importing them would report it inaccessible (CS0122). Both keep it private.
        /// </remarks>
        [Test]
        public async Task Run_PublicTypeReadingPrivateMember_FailsBecauseTheMemberStaysPrivate()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "PrivateMember",
                    "new PrivateMemberReader().Read()",
                    Owner(OwnerPath,
                        "public sealed class PrivateMemberReader { "
                        + "public int Read() { return HotReloadInternalAccessFixture.Hidden(); } }"));

                string reason = FindFailedIntroducedTypeReason(result, CompilationFailurePrefix);
                Assert.That(reason, Is.Not.Null, DescribeOutcomes(result));
                Assert.That(reason, Does.Contain("'Hidden'"), DescribeOutcomes(result));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(HostValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that a public introduced type implementing an internal interface of the compiled
        /// assembly loads, and that a call through the interface dispatches to its method.
        /// </summary>
        [Test]
        public async Task Run_PublicTypeImplementingInternalInterface_IsIntroducedAndDispatches()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "InternalInterface",
                    "((IHotReloadInternalAccessReader)new InternalInterfaceReader()).Read()",
                    Owner(OwnerPath,
                        "public sealed class InternalInterfaceReader : IHotReloadInternalAccessReader { "
                        + "public int Read() { return HotReloadInternalAccessFixture.Read() + 1; } }"));

                AssertIntroduced(result, "InternalInterfaceReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue + 1), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that internal members stay reachable from the methods the compiler generates for
        /// a lambda closure, an async method awaiting a completed task, and a finite iterator.
        /// </summary>
        [TestCase("closure")]
        [TestCase("async")]
        [TestCase("iterator")]
        public async Task Run_InternalAccessInsideLambdaAndAsync_IsIntroducedAndReachable(string shape)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "Generated" + shape,
                    "new GeneratedMethodReader().Read()",
                    Owner(OwnerPath, GeneratedMethodReader(shape)));

                AssertIntroduced(result, "GeneratedMethodReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue + 1), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that a generic method reaching internal members runs for a reference-type and a
        /// value-type argument, directly and through a closure capturing its generic parameter.
        /// </summary>
        [Test]
        public async Task Run_GenericMethodReachingInternals_IsIntroducedForReferenceAndValueTypeArguments()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "GenericMethod",
                    "new GenericReader().Read<string>(\"a\") + new GenericReader().Read<int>(1)"
                    + " + new GenericReader().ReadThroughClosure<string>(\"a\")"
                    + " + new GenericReader().ReadThroughClosure<int>(1)",
                    Owner(OwnerPath,
                        "public sealed class GenericReader { "
                        + "public int Read<T>(T value) { return HotReloadInternalAccessFixture.Read(); } "
                        + "public int ReadThroughClosure<T>(T value) { "
                        + "System.Func<int> read = () => HotReloadInternalAccessFixture.Read() + (value == null ? 0 : 1); "
                        + "return read(); } }"));

                AssertIntroduced(result, "GenericReader");
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(InternalValue * 2 + (InternalValue + 1) * 2),
                    DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that a type a later reload introduces can call an internal member of a type an
        /// earlier reload retained, which the retained assembly only offers to a compilation that
        /// references its internals-exposed copy.
        /// </summary>
        [Test]
        public async Task Run_LaterTypeUsingInternalMemberOfRetainedArtifact_IsIntroduced()
        {
            string retainedHolder =
                "public sealed class RetainedHolder { "
                + "internal static int ReadInternal() { return HotReloadInternalAccessFixture.Read(); } "
                + "public int Read() { return ReadInternal(); } }";
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "RetainedFirst",
                    "new RetainedHolder().Read()",
                    Owner(OwnerPath, retainedHolder));
                AssertIntroduced(first, "RetainedHolder");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue), DescribeOutcomes(first));

                Dictionary<string, string> owners = Owner(OwnerPath, retainedHolder);
                owners[LaterOwnerPath] = TypeSource(
                    "public sealed class LaterReader { public int Read() { return RetainedHolder.ReadInternal() + 1; } }");
                HotReloadOrchestratorResult later = await RunAsync("RetainedLater", "new LaterReader().Read()", owners);

                AssertIntroduced(later, "LaterReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue + 1), DescribeOutcomes(later));
            });
        }

        /// <summary>
        /// Verifies that a body edit of an introduced type that reaches internal members is patched
        /// on the retained assembly, and that the patched caller returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_BodyEditOfIntroducedTypeReachingInternals_IsPatchedAndReturnsTheNewValue()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "BodyEditFirst",
                    "new EditedInternalReader().Read()",
                    Owner(OwnerPath, EditedInternalReader(string.Empty)));
                AssertIntroduced(first, "EditedInternalReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue), DescribeOutcomes(first));

                HotReloadOrchestratorResult edited = await RunAsync(
                    "BodyEditSecond",
                    "new EditedInternalReader().Read()",
                    Owner(OwnerPath, EditedInternalReader(" + 1")));

                Assert.That(CountFailures(edited), Is.Zero, DescribeOutcomes(edited));
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue + 1), DescribeOutcomes(edited));
            });
        }

        /// <summary>
        /// Verifies that an internal constant the edit leaves unchanged can be read by an introduced
        /// type, which a compilation against the raw references rejects as inaccessible.
        /// </summary>
        [Test]
        public async Task Run_UnchangedInternalConst_IsIntroduced()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "InternalConst",
                    "new InternalConstReader().Read()",
                    Owner(OwnerPath,
                        "public sealed class InternalConstReader { "
                        + "public int Read() { return HotReloadInternalAccessFixture.Constant; } }"));

                AssertIntroduced(result, "InternalConstReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// Verifies that without an available grant the artifact compiles against the raw
        /// references, so a declaration reading an internal member fails with CS0122 and the grant
        /// is never asked.
        /// </summary>
        [Test]
        public async Task Run_GrantUnavailable_TypeReadingInternalsFailsWithCS0122()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: false);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                int activeBefore = ActiveTypeCount();

                HotReloadOrchestratorResult result = await RunAsync(
                    "Unavailable",
                    "new InternalStaticReader().Read()",
                    Owner(OwnerPath, InternalStaticReader));

                Assert.That(FindFailedIntroducedTypeReason(result, "CS0122"), Is.Not.Null, DescribeOutcomes(result));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), DescribeOutcomes(result));
                Assert.That(CallTheCaller(), Is.EqualTo(HostValue), DescribeOutcomes(result));
            }, grant);

            Assert.That(grant.GrantCalls, Is.Zero);
        }

        /// <summary>
        /// Verifies that without an available grant a declaration that needs no internal member is
        /// still introduced, from the raw references and without asking the grant.
        /// </summary>
        [Test]
        public async Task Run_GrantUnavailable_PlainTypeIsIntroduced()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: false);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    "UnavailablePlain",
                    "new PlainReader().Read()",
                    Owner(OwnerPath, "public sealed class PlainReader { public int Read() { return 5; } }"));

                AssertIntroduced(result, "PlainReader");
                Assert.That(CallTheCaller(), Is.EqualTo(5), DescribeOutcomes(result));
            }, grant);

            Assert.That(grant.GrantCalls, Is.Zero);
        }

        /// <summary>
        /// Verifies that a refused grant fails the declaration with the grant's reason, activates
        /// nothing, and keeps the caller running what it ran before, including a type an earlier
        /// reload activated.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task Run_GrantRefused_FailsTheArtifactAndLeavesNothingActive(bool withEarlierActiveType)
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: true);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                Dictionary<string, string> owners = new Dictionary<string, string>();
                string callerExpression = "new RefusedReader().Read()";
                int callerValueBefore = HostValue;
                if (withEarlierActiveType)
                {
                    // The earlier type needs no internal member, so the fake's grant, which changes
                    // no method, is all it needs to run.
                    owners[OwnerPath] = TypeSource("public sealed class KeptReader { public int Read() { return 3; } }");
                    HotReloadOrchestratorResult earlier = await RunAsync("RefusalEarlier", "new KeptReader().Read()", owners);
                    AssertIntroduced(earlier, "KeptReader");
                    callerExpression += " + new KeptReader().Read()";
                    callerValueBefore = 3;
                }

                int activeBefore = ActiveTypeCount();
                int grantsBefore = grant.GrantCalls;
                grant.RefusalReason = GrantRefusalReason;
                owners[LaterOwnerPath] = TypeSource("public sealed class RefusedReader { public int Read() { return 4; } }");

                HotReloadOrchestratorResult refused = await RunAsync("Refused", callerExpression, owners);

                string reason = FindFailedIntroducedTypeReason(
                    refused,
                    CompilationFailurePrefix + "Granting internal access to the introduced-type artifact failed: "
                    + GrantRefusalReason);
                Assert.That(reason, Is.Not.Null, DescribeOutcomes(refused));
                Assert.That(grant.GrantCalls, Is.EqualTo(grantsBefore + 1), DescribeOutcomes(refused));
                Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), DescribeOutcomes(refused));
                Assert.That(CallTheCaller(), Is.EqualTo(callerValueBefore), DescribeOutcomes(refused));
            }, grant);
        }

        /// <summary>
        /// Verifies that the references an artifact compiles against never reach the worker runs
        /// or the shim compilation: the preparation leaves its worker input as it was, the transform
        /// run and the gate see only raw paths, and the transform run still gets the artifact this
        /// run prepared.
        /// </summary>
        [Test]
        public async Task Run_ExposedArtifactReferences_DoNotChangeWorkerOrShimReferences()
        {
            List<string> prepareInputsBefore = new List<string>();
            List<string> prepareInputsAfter = new List<string>();
            List<string> preparedDllPaths = new List<string>();
            List<string> transformInputs = new List<string>();
            List<string> gateInputs = new List<string>();
            HotReloadIntroducedTypeStageProbe probe = new HotReloadIntroducedTypeStageProbe
            {
                BeforePrepare = input => prepareInputsBefore.Add(DescribeReferences(input)),
                AfterPrepare = (input, preparation) =>
                {
                    prepareInputsAfter.Add(DescribeReferences(input));
                    preparedDllPaths.Add(preparation.Prepared?.Artifact.DllPath);
                },
                BeforeTransform = input => transformInputs.Add(DescribeReferences(input)),
                BeforeGate = context => gateInputs.Add(DescribeReferences(context.WorkerInput))
            };

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "DtoFirst",
                    "new InternalStaticReader().Read()",
                    Owner(OwnerPath, InternalStaticReader));
                AssertIntroduced(first, "InternalStaticReader");

                // The second run starts with a retained artifact, so its worker input already
                // carries an artifact record the exposure must leave raw as well.
                Dictionary<string, string> owners = Owner(OwnerPath, InternalStaticReader);
                owners[LaterOwnerPath] = TypeSource(
                    "public sealed class SecondReader { public int Read() { return HotReloadInternalAccessFixture.Read() + 1; } }");
                HotReloadOrchestratorResult second = await RunAsync(
                    "DtoSecond",
                    "new InternalStaticReader().Read() + new SecondReader().Read()",
                    owners);
                AssertIntroduced(second, "SecondReader");
                Assert.That(CallTheCaller(), Is.EqualTo(InternalValue * 2 + 1), DescribeOutcomes(second));
            }, probe: probe);

            Assert.That(prepareInputsBefore, Has.Count.EqualTo(2));
            Assert.That(prepareInputsAfter, Is.EqualTo(prepareInputsBefore), "The preparation must not edit its worker input.");
            Assert.That(transformInputs, Has.Count.EqualTo(2));
            Assert.That(gateInputs, Has.Count.EqualTo(2));
            for (int run = 0; run < 2; run++)
            {
                Assert.That(prepareInputsBefore[run], Does.Not.Contain(ExposedReferenceDirectory), prepareInputsBefore[run]);
                Assert.That(transformInputs[run], Does.Not.Contain(ExposedReferenceDirectory), transformInputs[run]);
                Assert.That(gateInputs[run], Does.Not.Contain(ExposedReferenceDirectory), gateInputs[run]);
                Assert.That(preparedDllPaths[run], Is.Not.Null);
                Assert.That(
                    transformInputs[run],
                    Does.Contain("artifact " + preparedDllPaths[run] + " prepared"),
                    transformInputs[run]);
            }
        }

        /// <summary>
        /// Verifies that a run introducing no type neither builds the exposed references nor asks
        /// the grant: an active artifact whose exposed copy cannot be written would otherwise have
        /// failed the preparation.
        /// </summary>
        [Test]
        public async Task Prepare_NoDescriptors_DoesNotBuildReferencesOrGrant()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: true);
            List<HotReloadIntroducedTypePreparationResult> preparations = new List<HotReloadIntroducedTypePreparationResult>();
            HotReloadIntroducedTypeStageProbe probe = new HotReloadIntroducedTypeStageProbe
            {
                AfterPrepare = (_, preparation) => preparations.Add(preparation)
            };

            using (InternalsExposureTestImage unexposable = CreateUnexposableImage())
            {
                await RunInIntroducedTypeDomainAsync(async _ =>
                {
                    ActivateUnexposableArtifact(unexposable);

                    HotReloadOrchestratorResult result = await RunAsync(
                        "NoDescriptors",
                        "2",
                        new Dictionary<string, string>());

                    Assert.That(preparations, Has.Count.EqualTo(1), DescribeOutcomes(result));
                    Assert.That(preparations[0].Success, Is.True, DescribeOutcomes(result));
                    Assert.That(preparations[0].Prepared, Is.Null, DescribeOutcomes(result));
                    Assert.That(preparations[0].Failures, Is.Empty, DescribeOutcomes(result));
                    Assert.That(DescribeOutcomes(result), Does.Not.Contain(ExposureFailureReason));
                }, grant, probe);
            }

            Assert.That(grant.GrantCalls, Is.Zero);
        }

        /// <summary>
        /// Verifies that references that cannot be exposed fail the new declaration with the
        /// reason, compile and grant nothing, and keep what the preparation observed about the rest
        /// of the group: the declaration an earlier reload retains, the notice of a generic
        /// declaration, and the drift of a changed constant.
        /// </summary>
        [Test]
        public async Task Prepare_ReferenceFailure_PreservesObservedContext()
        {
            FakeInternalAccessGrant grant = new FakeInternalAccessGrant(isAvailable: true);
            List<HotReloadIntroducedTypePreparationResult> preparations = new List<HotReloadIntroducedTypePreparationResult>();
            HotReloadIntroducedTypeStageProbe probe = new HotReloadIntroducedTypeStageProbe
            {
                AfterPrepare = (_, preparation) => preparations.Add(preparation)
            };
            const string keptReader = "public sealed class ObservedKept { public int Read() { return 3; } }";
            string fixturePath = FixturePath("HotReloadInternalAccessFixtures.cs");
            string fixtureSource = File.ReadAllText(fixturePath);
            Assert.That(fixtureSource, Does.Contain(FixtureConstantDeclaration), "Precondition: the constant must exist.");

            using (InternalsExposureTestImage unexposable = CreateUnexposableImage())
            {
                await RunInIntroducedTypeDomainAsync(async _ =>
                {
                    HotReloadOrchestratorResult first = await RunAsync(
                        "ObservedFirst",
                        "new ObservedKept().Read()",
                        Owner(OwnerPath, keptReader));
                    AssertIntroduced(first, "ObservedKept");
                    ActivateUnexposableArtifact(unexposable);
                    int activeBefore = ActiveTypeCount();
                    int grantsBefore = grant.GrantCalls;

                    Dictionary<string, string> sources = Owner(
                        OwnerPath,
                        keptReader
                        + " public sealed class ObservedAdded { public int Read() { return 4; } }"
                        + " public sealed class ObservedGeneric<T> { }");
                    sources[fixturePath] = fixtureSource.Replace(
                        FixtureConstantDeclaration,
                        "internal const int Constant = 22;",
                        StringComparison.Ordinal);
                    HotReloadOrchestratorResult failed = await RunAsync(
                        "ObservedFailed",
                        "new ObservedAdded().Read()",
                        sources);

                    string description = DescribeOutcomes(failed);
                    Assert.That(preparations, Has.Count.EqualTo(2), description);
                    HotReloadIntroducedTypePreparationResult preparation = preparations[1];
                    Assert.That(preparation.Success, Is.False, description);
                    Assert.That(
                        FindFailedIntroducedTypeReason(failed, CompilationFailurePrefix + ExposureFailureReason),
                        Is.Not.Null,
                        description);
                    Assert.That(HasAlreadyActive(preparation, "ObservedKept"), Is.True, description);
                    Assert.That(HasNotice(preparation, "Generic introduced type requires a compile"), Is.True, description);
                    Assert.That(
                        preparation.DeclarationDriftWarnings.ContainsKey(FixtureProjectRelativePath),
                        Is.True,
                        description);
                    Assert.That(grant.GrantCalls, Is.EqualTo(grantsBefore), description);
                    Assert.That(ActiveTypeCount(), Is.EqualTo(activeBefore), description);
                    Assert.That(CallTheCaller(), Is.EqualTo(3), description);
                }, grant, probe);
            }
        }

        private static string GeneratedMethodReader(string shape)
        {
            string body;
            switch (shape)
            {
                case "closure":
                    body = "public int Read() { int offset = 1; "
                        + "System.Func<int> read = () => HotReloadInternalAccessFixture.Read() + offset; "
                        + "return read(); }";
                    break;
                case "async":
                    // Why a completed task: the method finishes before Read returns, so reading the
                    // result never waits.
                    body = "public int Read() { System.Threading.Tasks.Task<int> pending = ReadAsync(); "
                        + "return pending.IsCompleted ? pending.Result : -1; } "
                        + "private async System.Threading.Tasks.Task<int> ReadAsync() { "
                        + "await System.Threading.Tasks.Task.CompletedTask; "
                        + "return HotReloadInternalAccessFixture.Read() + 1; }";
                    break;
                case "iterator":
                    body = "public int Read() { int sum = 0; foreach (int value in Values()) { sum += value; } "
                        + "return sum; } "
                        + "private System.Collections.Generic.IEnumerable<int> Values() { "
                        + "yield return HotReloadInternalAccessFixture.Read(); yield return 1; }";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }

            return "public sealed class GeneratedMethodReader { " + body + " }";
        }

        private static string EditedInternalReader(string addend)
        {
            return "public sealed class EditedInternalReader { " + NoInlining
                + "public int Read() { return HotReloadInternalAccessFixture.Read()" + addend + "; } }";
        }

        // An image whose exposed copy cannot be written, because the enum one of its constants is
        // typed with lives in an assembly that is gone.
        private static InternalsExposureTestImage CreateUnexposableImage()
        {
            return InternalsExposureTestImage.CreateWithConstantOfMissingEnum(
                "UnexposableArtifactEnum_" + Guid.NewGuid().ToString("N"));
        }

        // Makes the image an active artifact of the test assembly, so the exposing reference build
        // of any later run has to copy it and fails. Why loaded from its bytes: the image file is
        // deleted when the test ends, which a file an assembly was loaded from blocks on Windows.
        private static void ActivateUnexposableArtifact(InternalsExposureTestImage image)
        {
            Assembly testAssembly = typeof(HotReloadIntroducedTypeInternalAccessE2ETests).Assembly;
            HotReloadIntroducedTypeDescriptor descriptor = new HotReloadIntroducedTypeDescriptor(
                testAssembly.GetName().Name,
                HotReloadSourceSnapshotter.ReadAssemblyMvid(testAssembly.Location),
                "Candidate",
                UnexposableOwnerPath,
                "unexposable-declaration",
                "public class Candidate { }");
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                Assembly.Load(File.ReadAllBytes(image.DllPath)),
                image.DllPath,
                Path.ChangeExtension(image.DllPath, ".pdb"),
                new List<HotReloadIntroducedTypeDescriptor> { descriptor });
            HotReloadIntroducedTypeRegistry registry = HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
        }

        // One line per reference and per artifact record, so two inputs compare as text and a
        // leaked path shows up in the failure message.
        private static string DescribeReferences(TransformWorkerInputDto input)
        {
            StringBuilder description = new StringBuilder();
            foreach (string referencePath in input.referencePaths)
            {
                description.Append("reference ").Append(referencePath).Append('\n');
            }

            foreach (TransformWorkerIntroducedTypeArtifactDto record in input.introducedTypeArtifacts)
            {
                description.Append("artifact ").Append(record.referencePath)
                    .Append(record.preparedByThisRun ? " prepared" : string.Empty)
                    .Append('\n');
            }

            return description.ToString();
        }

        private static bool HasAlreadyActive(HotReloadIntroducedTypePreparationResult preparation, string simpleName)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in preparation.AlreadyActiveTypes)
            {
                if (outcome.MetadataName == Namespace + "." + simpleName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNotice(HotReloadIntroducedTypePreparationResult preparation, string fragment)
        {
            foreach (HotReloadIntroducedTypeNotice notice in preparation.Notices)
            {
                if (notice.Text.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, string> Owner(string ownerPath, string declarations)
        {
            return new Dictionary<string, string> { [ownerPath] = TypeSource(declarations) };
        }

        private static string TypeSource(string declarations)
        {
            return "namespace " + Namespace + "\n{\n    " + declarations + "\n}\n";
        }

        // Only the owners named in the map take part in the run, and the caller body is replaced by
        // the expression, so the value the caller returns is the value the introduced type made.
        private static Task<HotReloadOrchestratorResult> RunAsync(
            string label,
            string callerExpression,
            Dictionary<string, string> sources)
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            List<string> paths = new List<string> { callerPath };
            Dictionary<string, string> edits = new Dictionary<string, string>
            {
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "InternalAccessCaller" + label + ".cs",
                    callerSource.Replace(CallerBodyAnchor, "return " + callerExpression + ";", StringComparison.Ordinal))
            };
            foreach (KeyValuePair<string, string> source in sources)
            {
                paths.Add(source.Key);
                edits[source.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(source.Key) + label + ".cs",
                    source.Value);
            }

            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                paths.ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private static int CallTheCaller()
        {
            return new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
        }

        private static int ActiveTypeCount()
        {
            return HotReloadCompositionRoot.Services.Domain.IntroducedTypes.ActiveTypeCount;
        }

        private static void AssertIntroduced(HotReloadOrchestratorResult result, string simpleName)
        {
            Assert.That(CountFailures(result), Is.Zero, DescribeOutcomes(result));
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Introduced
                    && outcome.MetadataName == Namespace + "." + simpleName)
                {
                    return;
                }
            }

            Assert.Fail(simpleName + " must be reported as introduced.\n" + DescribeOutcomes(result));
        }

        private static string FindFailedIntroducedTypeReason(HotReloadOrchestratorResult result, string fragment)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason != null
                    && outcome.Reason.Contains(fragment, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            return null;
        }
    }
}
