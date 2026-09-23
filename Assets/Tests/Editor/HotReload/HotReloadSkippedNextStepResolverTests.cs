using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how the next step of a skipped row carrying a compiled type is chosen from where the
    /// files declaring that type stand once the run is written.
    /// </summary>
    public class HotReloadSkippedNextStepResolverTests
    {
        private const string Fact = "The added member's body could not be fully bound.";
        private const string KindPath = "Assets/Scripts/Kinds.cs";
        private const string RegistryPath = "Assets/Scripts/Registry.cs";
        private const string ExtraPath = "Assets/Scripts/Registry.Extra.cs";
        private const string KindType = "'Game.Kind'";
        private const string OtherType = "'Game.Shape'";
        private const string CarriedInMethod = "World.StepFalling()";

        /// <summary>
        /// What: a compiled-signature row whose declaring files the run never took in asks for
        /// all of them to be passed.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureWithFilesOutsideTheRun_AsksToPassThem()
        {
            HotReloadMethodOutcome row = CompiledSignatureRow(KindType, RegistryPath, ExtraPath);

            List<HotReloadMethodOutcome> resolved = Resolver(new Dictionary<string, HotReloadCarriedInState>())
                .Resolve(new[] { row });

            Assert.That(
                resolved[0].Reason,
                Is.EqualTo(
                    Fact + " Pass '" + RegistryPath + "' and '" + ExtraPath + "' to this reload as well so both "
                    + "bind to the same type; run 'uloop compile' only if it still does not bind."));
        }

        /// <summary>
        /// What: a compiled-signature row names only the declaring files the run did not take in.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureWithOneFileInTheRun_AsksToPassOnlyTheOther()
        {
            HotReloadMethodOutcome row = CompiledSignatureRow(KindType, RegistryPath, ExtraPath);
            Dictionary<string, HotReloadCarriedInState> states = new Dictionary<string, HotReloadCarriedInState>
            {
                { RegistryPath, HotReloadCarriedInState.NotRecorded }
            };

            List<HotReloadMethodOutcome> resolved = Resolver(states).Resolve(new[] { row });

            Assert.That(resolved[0].Reason, Does.Contain("Pass '" + ExtraPath + "' to this reload"));
            Assert.That(resolved[0].Reason, Does.Not.Contain("'" + RegistryPath + "'"));
        }

        /// <summary>
        /// What: a compiled-signature row whose declaring files are all in the run says passing
        /// them again does not help, without claiming they hold patches.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureWithEveryFileInTheRun_SaysPassingAgainDoesNotHelp()
        {
            HotReloadMethodOutcome row = CompiledSignatureRow(KindType, RegistryPath);
            Dictionary<string, HotReloadCarriedInState> states = new Dictionary<string, HotReloadCarriedInState>
            {
                { RegistryPath, HotReloadCarriedInState.RecordedAtCurrentSource }
            };

            List<HotReloadMethodOutcome> resolved = Resolver(states).Resolve(new[] { row });

            Assert.That(
                resolved[0].Reason,
                Is.EqualTo(
                    Fact + " '" + RegistryPath + "' is already in this reload, so passing it again does not "
                    + "help; run 'uloop compile'."));
        }

        /// <summary>
        /// What: a compiled-signature row with no placed declaring file asks to pass the file the
        /// sentence already describes.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureWithNoPlacedFile_AsksToPassTheDescribedFile()
        {
            HotReloadMethodOutcome row = CompiledSignatureRow(KindType);

            List<HotReloadMethodOutcome> resolved = Resolver(new Dictionary<string, HotReloadCarriedInState>())
                .Resolve(new[] { row });

            Assert.That(
                resolved[0].Reason,
                Does.EndWith(
                    " Pass the file that declares 'Game.Registry' to this reload as well so both bind to the "
                    + "same type; run 'uloop compile' only if it still does not bind."));
        }

        /// <summary>
        /// What: a carried-in row whose type file came back as a sibling asks to undo the edit and
        /// leave the file out.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithReappliedFile_AsksToUndoAndLeaveOut()
        {
            HotReloadMethodOutcome row = CarriedInRow(KindType, KindPath);

            List<HotReloadMethodOutcome> resolved = new HotReloadSkippedNextStepResolver(
                    path => HotReloadCarriedInState.NotRecorded,
                    new[] { KindPath })
                .Resolve(new[] { row });

            Assert.That(resolved[0].Reason, Is.EqualTo(Fact + " " + UndoAndLeaveOut(KindPath)));
        }

        /// <summary>
        /// What: a carried-in row whose type file is recorded at its current source asks to undo
        /// the edit and leave the file out, because leaving it out alone brings it back.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithFileRecordedAtCurrentSource_AsksToUndoAndLeaveOut()
        {
            HotReloadMethodOutcome row = CarriedInRow(KindType, KindPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, HotReloadCarriedInState.RecordedAtCurrentSource))
                .Resolve(new[] { row });

            Assert.That(resolved[0].Reason, Is.EqualTo(Fact + " " + UndoAndLeaveOut(KindPath)));
        }

        /// <summary>
        /// What: a carried-in row whose type file is not recorded at its current source asks only
        /// to leave it out. A record of older bytes reads as not recorded, and undoing the edit
        /// would bring the file back through that record.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithUnrecordedFile_AsksToLeaveOutWithoutUndo()
        {
            HotReloadMethodOutcome row = CarriedInRow(KindType, KindPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, HotReloadCarriedInState.NotRecorded))
                .Resolve(new[] { row });

            Assert.That(
                resolved[0].Reason,
                Is.EqualTo(
                    Fact + " To hot reload it without a compile, leave '" + KindPath + "' out of --files and "
                    + "rerun (the edit in '" + KindPath + "' waits for the next compile); or run 'uloop compile'."));
            Assert.That(resolved[0].Reason, Does.Not.Contain("undo"));
        }

        /// <summary>
        /// What: a carried-in row whose type file applied a change in this run asks for a compile,
        /// since leaving the file out would drop that change.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithFileAppliedInThisRun_AsksForCompile()
        {
            AssertCarriedInRowAsksForCompile(HotReloadCarriedInState.AppliedInThisRun);
        }

        /// <summary>
        /// What: a carried-in row whose type file holds an earlier reload's patches asks for a
        /// compile, since every later reload brings that file back.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithFileActiveFromEarlierRun_AsksForCompile()
        {
            AssertCarriedInRowAsksForCompile(HotReloadCarriedInState.ActiveFromEarlierRun);
        }

        /// <summary>
        /// What: a carried-in row with no declaring file is a worker contract violation and stops
        /// the run instead of guessing a step.
        /// </summary>
        [Test]
        public void Resolve_CarriedInRowWithoutDeclaringFile_Throws()
        {
            HotReloadMethodOutcome row = CarriedInRow(KindType);

            Assert.Throws<InvalidOperationException>(
                () => Resolver(new Dictionary<string, HotReloadCarriedInState>()).Resolve(new[] { row }));
        }

        /// <summary>
        /// What: a compiled-signature row about the type a carried-in row asks to leave out points
        /// to that row instead of naming a file to pass.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureRowForTheLeftOutType_PointsToTheCarriedInRow()
        {
            HotReloadMethodOutcome carriedIn = CarriedInRow(KindType, KindPath);
            HotReloadMethodOutcome compiledSignature = CompiledSignatureRow(KindType, RegistryPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, HotReloadCarriedInState.NotRecorded))
                .Resolve(new[] { compiledSignature, carriedIn });

            Assert.That(
                resolved[0].Reason,
                Is.EqualTo(
                    Fact + " The step on the Skipped row for " + CarriedInMethod + " (leaving '" + KindPath
                    + "' out) resolves this row as well; passing the file that declares the compiled "
                    + "signature rebinds only this member."));
        }

        /// <summary>
        /// What: a compiled-signature row about another type keeps its own step beside a
        /// carried-in row.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureRowForAnotherType_KeepsItsOwnStep()
        {
            HotReloadMethodOutcome carriedIn = CarriedInRow(KindType, KindPath);
            HotReloadMethodOutcome compiledSignature = CompiledSignatureRow(OtherType, RegistryPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, HotReloadCarriedInState.NotRecorded))
                .Resolve(new[] { carriedIn, compiledSignature });

            Assert.That(resolved[1].Reason, Does.Contain("Pass '" + RegistryPath + "' to this reload"));
        }

        /// <summary>
        /// What: a compiled-signature row keeps its own step when the carried-in row for the same
        /// type asks for a compile, since there is no left-out file to point to.
        /// </summary>
        [Test]
        public void Resolve_CompiledSignatureRowBesideCarriedInCompile_KeepsItsOwnStep()
        {
            HotReloadMethodOutcome carriedIn = CarriedInRow(KindType, KindPath);
            HotReloadMethodOutcome compiledSignature = CompiledSignatureRow(KindType, RegistryPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, HotReloadCarriedInState.AppliedInThisRun))
                .Resolve(new[] { carriedIn, compiledSignature });

            Assert.That(resolved[1].Reason, Does.Contain("Pass '" + RegistryPath + "' to this reload"));
        }

        /// <summary>
        /// What: rows without worker facts or with another reason code are returned as they are.
        /// </summary>
        [Test]
        public void Resolve_RowsOutsideTheCarriedInFamily_AreReturnedUnchanged()
        {
            HotReloadMethodOutcome plain = HotReloadMethodOutcome.Skipped("World.Tick()", Fact, "/abs/World.cs");
            HotReloadMethodOutcome otherCode = HotReloadMethodOutcome.Skipped("World.Step()", Fact, "/abs/World.cs")
                .WithWorkerReason(HotReloadWorkerReasonFacts.From(new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.AddedMethodBodyUnbound,
                    args = new[] { "CS0103: missing" }
                }));

            List<HotReloadMethodOutcome> resolved = Resolver(new Dictionary<string, HotReloadCarriedInState>())
                .Resolve(new[] { plain, otherCode });

            Assert.That(resolved[0], Is.SameAs(plain));
            Assert.That(resolved[1], Is.SameAs(otherCode));
        }

        /// <summary>
        /// What: the resolver refuses to be built without the state query or the re-applied paths.
        /// </summary>
        [Test]
        public void Constructor_MissingInputs_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new HotReloadSkippedNextStepResolver(null, Array.Empty<string>()));
            Assert.Throws<ArgumentNullException>(
                () => new HotReloadSkippedNextStepResolver(path => HotReloadCarriedInState.NotInRun, null));
        }

        private static void AssertCarriedInRowAsksForCompile(HotReloadCarriedInState state)
        {
            HotReloadMethodOutcome row = CarriedInRow(KindType, KindPath);

            List<HotReloadMethodOutcome> resolved = Resolver(StateOf(KindPath, state)).Resolve(new[] { row });

            Assert.That(
                resolved[0].Reason,
                Is.EqualTo(
                    Fact + " '" + KindPath + "' also holds patches this or an earlier reload applied, so it "
                    + "cannot be left out; run 'uloop compile'."));
        }

        private static HotReloadSkippedNextStepResolver Resolver(Dictionary<string, HotReloadCarriedInState> states)
        {
            return new HotReloadSkippedNextStepResolver(
                path => states.TryGetValue(path, out HotReloadCarriedInState state)
                    ? state
                    : HotReloadCarriedInState.NotInRun,
                Array.Empty<string>());
        }

        private static Dictionary<string, HotReloadCarriedInState> StateOf(string path, HotReloadCarriedInState state)
        {
            return new Dictionary<string, HotReloadCarriedInState> { { path, state } };
        }

        private static string UndoAndLeaveOut(string path)
        {
            return "To hot reload it without a compile, undo the edit in '" + path + "', leave '" + path
                + "' out of --files, and rerun: '" + path + "' is carried in again while its source matches "
                + "what an earlier reload was given. Or run 'uloop compile'.";
        }

        private static HotReloadMethodOutcome CompiledSignatureRow(string sourceType, params string[] declaringFiles)
        {
            TransformWorkerReasonDto dto = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                args = new[]
                {
                    "CS1503: cannot convert",
                    sourceType,
                    "'Game.Registry.Add(Game.Kind)'",
                    declaringFiles.Length == 0
                        ? "the file that declares 'Game.Registry'"
                        : "'" + string.Join("' and '", declaringFiles) + "'"
                },
                declaringFiles = declaringFiles
            };
            return HotReloadMethodOutcome.Skipped("World.MoveBlock()", Fact, "/abs/World.cs")
                .WithWorkerReason(HotReloadWorkerReasonFacts.From(dto));
        }

        private static HotReloadMethodOutcome CarriedInRow(string boundType, params string[] declaringFiles)
        {
            TransformWorkerReasonDto dto = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                args = new[]
                {
                    "CS0266: cannot convert",
                    "'Game.Behaviours'",
                    boundType,
                    "'" + string.Join("' and '", declaringFiles) + "'"
                },
                declaringFiles = declaringFiles
            };
            return HotReloadMethodOutcome.Skipped(CarriedInMethod, Fact, "/abs/World.cs")
                .WithWorkerReason(HotReloadWorkerReasonFacts.From(dto));
        }
    }
}
