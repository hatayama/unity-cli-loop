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
    /// Worker coverage for edits to methods of partial types: the parts of the type in files the run
    /// was not given complete the binding, and a method is skipped with a specific reason only when
    /// such a part cannot be trusted or its body names something no visible part declares.
    /// </summary>
    public class TransformWorkerPartialTypeTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureFileName = "HotReloadPartialTypeFixture.cs";
        private const string OtherPartFileName = "HotReloadPartialTypeFixture.Other.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string OtherPartProjectRelativePath = "Assets/Tests/Editor/HotReload/" + OtherPartFileName;
        private const string UnrelatedBrokenProjectRelativePath = "Assets/Tests/Editor/HotReload/PartialUnrelatedBrokenRunFile.cs";
        private const string UnreadableProjectRelativePath = "Assets/Tests/Editor/HotReload/PartialUnreadableRunFile.cs";
        private const string OtherPartOwnMethodBody = "return PartialTuning - 1;";
        private const string OtherPartOwnMethodBodyWithSyntaxError = "return PartialTuning - ;";

        // A part of the fixture whose "partial" is misspelled. Nothing else in it says "partial",
        // so only the type name in its text tells that the file was meant to hold a part.
        private const string MisspelledPartialPart =
            "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
            + "{\n"
            + "    public partal class HotReloadPartialTypeFixture\n"
            + "    {\n"
            + "        private int MisspelledPartValue()\n"
            + "        {\n"
            + "            return 3;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // A broken file that names none of the fixture's partial types.
        private const string UnrelatedBrokenFile =
            "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
            + "{\n"
            + "    internal static class HotReloadUnrelatedBrokenHelper\n"
            + "    {\n"
            + "        internal static int Value()\n"
            + "        {\n"
            + "            return 1 + ;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string OwnOnlyDeclaration =
            "        public int OwnOnly()\n        {\n            return 1;\n        }";
        private const string OwnOnlyEdited =
            "        public int OwnOnly()\n        {\n            return 2;\n        }";
        private const string OwnOnlyWithAttribute =
            "[MethodImpl(MethodImplOptions.NoInlining)]\n        public int OwnOnly()";
        private const string OtherPartPropertyGetter = "get { return OtherPartProperty; }";
        private const string OtherPartPropertyGetterEdited = "get { return OtherPartProperty + 100; }";

        // Fixtures of the internal-visibility repro tests. None of them declares the types whose
        // non-public members the edited bodies use, so the worker sees those types as compiled.
        private const string FixtureDirectoryProjectRelativePath = "Assets/Tests/Editor/HotReload/";
        private const string CallerFileName = "HotReloadInternalMemberCaller.cs";
        private const string PlainDerivedFileName = "HotReloadPlainDerivedFixture.cs";
        private const string PartialDerivedFileName = "HotReloadPartialDerivedFixture.cs";
        private const string CallerInternalCallBody = "return HotReloadInternalMemberHost.InternalStaticValue();";
        private const string CallerInternalCallBodyEdited = "return HotReloadInternalMemberHost.InternalStaticValue() + 100;";
        private const string CallerPlainValueBody = "return 1;";
        private const string PlainDerivedValueBody = "return 10;";
        private const string PartialDerivedValueBody = "return 9;";
        private const string InternalMemberOfATypeOfAnotherAssembly =
            "global::io.github.hatayama.UnityCliLoop.FirstPartyTools.PausePointCapturedVariable.FromSnapshot(null).Name.Length";

        /// <summary>
        /// What: a body that reads a private field declared in another part of the type is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyEdit_ReadingOtherPartField_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialReadsOtherPartField.cs",
                "return _otherSeed;",
                "return _otherSeed + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "ReadsOtherPartField");
        }

        /// <summary>
        /// What: a body that uses only members of its own part is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyEdit_UsingOwnMembersOnly_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialOwnOnly.cs",
                OwnOnlyDeclaration,
                OwnOnlyEdited);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "OwnOnly");
        }

        /// <summary>
        /// What: a body that calls a private method declared in another part of the type is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyEdit_CallingOtherPartPrivateMethod_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialCallsOtherPartMethod.cs",
                "return OtherPartValue();",
                "return OtherPartValue() + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "CallsOtherPartMethod");
        }

        /// <summary>
        /// What: a method whose parameter is a type nested in another part of the type keeps matching
        /// the compiled method, so its body edit is a normal edit and not an added method.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeMethodTakingOtherPartNestedType_IsNotClassifiedAsAdded()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialTakesOtherPartNested.cs",
                "return value.Number;",
                "return value.Number + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto entry = AssertEmitted(result, "TakesOtherPartNested");
            Assert.That(entry.patchKind, Is.Not.EqualTo(HotReloadConstants.PatchKindAddedMethod));
            Assert.That(entry.replacesCompiledMethod, Is.False);
        }

        /// <summary>
        /// What: in a partial type nested in a partial type, a body that reads a field declared in the
        /// nested type's other part is emitted.
        /// </summary>
        [Test]
        public async Task Run_NestedPartialTypeBodyEdit_ReadingOtherPartField_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialNestedReadsOtherPartField.cs",
                "return _nestedSeed;",
                "return _nestedSeed + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "ReadsOtherPartNestedField");
        }

        /// <summary>
        /// What: an existing property getter that reads a property declared in another part of the
        /// type is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeGetterEdit_ReadingOtherPartProperty_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialGetterReadsOtherPartProperty.cs",
                OtherPartPropertyGetter,
                OtherPartPropertyGetterEdited);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "get_ReadsOtherPartProperty");
        }

        /// <summary>
        /// What: when another part of the type changed since the last compile and is not in the run,
        /// the edited method is skipped with a reason that names that file.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAnotherPartChangedSinceTheLastCompile_NamesThatFile()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialOtherPartChanged.cs",
                OwnOnlyDeclaration,
                OwnOnlyEdited,
                changedSiblingSourcePaths: new[] { ResolveFixturePath(OtherPartFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "OwnOnly"), Is.Null, "OwnOnly must not be applied.");
            string reason = FindSkipReason(result, "OwnOnly");
            Assert.That(reason, Does.Contain(OtherPartFileName), FormatSkipped(result));
            Assert.That(reason, Does.Contain("changed since the last compile"), FormatSkipped(result));
        }

        /// <summary>
        /// What: when the list of changed files may be incomplete, no other part of the type is
        /// trusted, so the edited method is skipped with a reason that says the parts were not checked.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenTheSiblingScanIsIncomplete_SaysTheOtherPartsWereNotChecked()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialSiblingScanIncomplete.cs",
                OwnOnlyDeclaration,
                OwnOnlyEdited,
                changedSiblingScanComplete: false);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "OwnOnly"), Is.Null, "OwnOnly must not be applied.");
            string reason = FindSkipReason(result, "OwnOnly");
            Assert.That(reason, Does.Contain("could not be checked against the last compile"), FormatSkipped(result));
        }

        /// <summary>
        /// What: when a part of the type is in no source file the run can see (the stand-in for a part
        /// generated at compile time), only the body that names that part's member is skipped, with
        /// the unresolved-name diagnostic, and the other edited method of the file is still emitted.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenTheOtherPartIsNotAmongTheAssemblySources_SkipsOnlyTheUnboundBody()
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(FixtureFileName));
            string edited = ReplaceOnce(onDisk, OwnOnlyDeclaration, OwnOnlyEdited);
            edited = ReplaceOnce(edited, "return _otherSeed;", "return _otherSeed + 100;");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PartialOtherPartNotInSources.cs", edited),
                FixtureProjectRelativePath,
                onDisk,
                assemblySourcePathsOverride: BuildAssemblySourcePathsWithout(OtherPartFileName));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "OwnOnly");
            Assert.That(FindEntry(result, "ReadsOtherPartField"), Is.Null, "ReadsOtherPartField must not be applied.");
            string reason = FindSkipReason(result, "ReadsOtherPartField");
            Assert.That(reason, Does.Contain("CS0103"), FormatSkipped(result));
            Assert.That(reason, Does.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: when a part of the type is in no source file the run can see, an existing getter
        /// that names that part's property is skipped with the unresolved-name diagnostic.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeGetterEdit_WhenTheOtherPartIsNotAmongTheAssemblySources_SkipsTheGetter()
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(FixtureFileName));
            string edited = ReplaceOnce(onDisk, OtherPartPropertyGetter, OtherPartPropertyGetterEdited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PartialGetterOtherPartNotInSources.cs", edited),
                FixtureProjectRelativePath,
                onDisk,
                assemblySourcePathsOverride: BuildAssemblySourcePathsWithout(OtherPartFileName));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_ReadsOtherPartProperty"), Is.Null, "The getter must not be applied.");
            Assert.That(FindSkipReason(result, "get_ReadsOtherPartProperty"), Does.Contain("CS0103"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a body that passes the instance to a compiled API is emitted even though the worker's
        /// binding reports a conversion error for it, because the shim compile settles that error.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyEdit_PassingItselfToACompiledApi_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialPassesThisToCompiledApi.cs",
                "return HotReloadPartialTypeFixtureConsumer.Describe(this);",
                "return HotReloadPartialTypeFixtureConsumer.Describe(this) + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "PassesThisToCompiledApi");
        }

        /// <summary>
        /// What: a non-partial type nested in a partial type can read a member declared in the outer
        /// type's other part.
        /// </summary>
        [Test]
        public async Task Run_PlainTypeNestedInAPartialType_ReadingTheOuterOtherPartMember_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialNestedPlainReadsOuterOtherPart.cs",
                "return _otherStaticSeed;",
                "return _otherStaticSeed + 100;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "ReadsOuterOtherPartStatic");
        }

        /// <summary>
        /// What: with two declarations of one partial type in the edited file, each edited method is
        /// emitted exactly once.
        /// </summary>
        [Test]
        public async Task Run_TwoDeclarationsOfOnePartialTypeInTheEditedFile_EmitEachMethodOnce()
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(FixtureFileName));
            string edited = ReplaceOnce(onDisk, OwnOnlyDeclaration, OwnOnlyEdited);
            edited = ReplaceOnce(edited, "return 21;", "return 22;");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PartialTwoDeclarations.cs", edited),
                FixtureProjectRelativePath,
                onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(CountEntries(result, "OwnOnly"), Is.EqualTo(1), FormatSkipped(result));
            Assert.That(CountEntries(result, "SecondBlockMethod"), Is.EqualTo(1), FormatSkipped(result));
        }

        /// <summary>
        /// What: a method of a partial struct is skipped for being on a struct, not for being partial.
        /// </summary>
        [Test]
        public async Task Skip_PartialStructMethodEdit_ReportsTheStructReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialStructValue.cs",
                "        public int StructValue()\n        {\n            return 1;\n        }",
                "        public int StructValue()\n        {\n            return 2;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "StructValue");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for StructValue.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformStructHost));
        }

        /// <summary>
        /// What: a method added to a partial type that calls a method of another part is added, not
        /// skipped.
        /// </summary>
        [Test]
        public async Task Run_MethodAddedToAPartialType_CallingAnOtherPartMethod_IsAddedNotSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialAddedCallsOtherPart.cs",
                OwnOnlyWithAttribute,
                "public int AddedCallsOtherPart()\n        {\n            return OtherPartValue() + 1;\n        }\n\n        "
                + OwnOnlyWithAttribute);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, "AddedCallsOtherPart"), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            TransformWorkerEntryDto entry = FindEntry(result, "AddedCallsOtherPart");
            Assert.That(entry, Is.Not.Null, "Missing entry for AddedCallsOtherPart.\n" + FormatSkipped(result));
            Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
        }

        /// <summary>
        /// What: a method added to a partial type that passes the instance to a compiled API is skipped
        /// with the same split reason a non-partial type gets, and the run itself does not fail.
        /// </summary>
        [Test]
        public async Task Skip_MethodAddedToAPartialType_PassingItselfToACompiledApi_NamesTheSplitWithoutFailingTheRun()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialAddedPassesThis.cs",
                OwnOnlyWithAttribute,
                "public int AddedPassesThis()\n        {\n            return HotReloadPartialTypeFixtureConsumer.Describe(this);\n        }\n\n        "
                + OwnOnlyWithAttribute);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "AddedPassesThis"), Is.Null, "AddedPassesThis must not be applied.");
            TransformWorkerSkippedDto skipped = FindSkipped(result, "AddedPassesThis");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for AddedPassesThis.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature));
        }

        /// <summary>
        /// What: when another part of the type has a syntax error, the edited method is skipped with a
        /// reason that names that file, even though the file is not listed as changed.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAnotherPartHasASyntaxError_NamesThatFile()
        {
            string brokenSibling = WriteEdited(
                "PartialSiblingWithSyntaxError.cs",
                ReplaceOnce(
                    File.ReadAllText(ResolveFixturePath(OtherPartFileName)),
                    OtherPartOwnMethodBody,
                    OtherPartOwnMethodBodyWithSyntaxError));
            List<string> assemblySourcePaths = new List<string>(BuildAssemblySourcePathsWithout(OtherPartFileName))
            {
                brokenSibling
            };

            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(
                new[] { BuildOwnOnlyEditSource("PartialSiblingWithSyntaxErrorEdited.cs") },
                assemblySourcePathsOverride: assemblySourcePaths.ToArray());

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedAsOtherPartChanged(result, "OwnOnly", "PartialSiblingWithSyntaxError.cs");
        }

        /// <summary>
        /// What: when the run is also given another part of the type and that part has a syntax error,
        /// the edited method is skipped with a reason that names the broken file, and the broken file
        /// reports its parse errors on its own row.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAPassedOtherPartHasASyntaxError_NamesThatFile()
        {
            string brokenOtherPart = ReplaceOnce(
                File.ReadAllText(ResolveFixturePath(OtherPartFileName)),
                OtherPartOwnMethodBody,
                OtherPartOwnMethodBodyWithSyntaxError);

            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildOwnOnlyEditSource("PartialPassedPartWithSyntaxErrorEdited.cs"),
                BuildOtherPartSource("PartialPassedPartWithSyntaxError.cs", brokenOtherPart)
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedAsOtherPartChanged(result, "OwnOnly", OtherPartProjectRelativePath);
            AssertFileHasParseErrors(result, OtherPartProjectRelativePath);
        }

        /// <summary>
        /// What: when another passed part misspells "partial", so its tree no longer declares a part of
        /// the type, the edited method is still skipped with a reason that names that file.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAPassedOtherPartMisspellsPartial_NamesThatFile()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildOwnOnlyEditSource("PartialPassedPartMisspelledEdited.cs"),
                BuildOtherPartSource("PartialPassedPartMisspelled.cs", MisspelledPartialPart)
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedAsOtherPartChanged(result, "OwnOnly", OtherPartProjectRelativePath);
            AssertFileHasParseErrors(result, OtherPartProjectRelativePath);
        }

        /// <summary>
        /// What: a broken file in the same run that never names the partial type does not keep the
        /// type's edited method from being emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyEdit_WithABrokenRunFileThatNeverNamesTheType_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildOwnOnlyEditSource("PartialUnrelatedBrokenRunFileEdited.cs"),
                new TransformWorkerSourceDto
                {
                    sourcePath = WriteEdited("PartialUnrelatedBrokenRunFile.cs", UnrelatedBrokenFile),
                    projectRelativePath = UnrelatedBrokenProjectRelativePath,
                    snapshotSource = string.Empty
                }
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "OwnOnly");
            AssertFileHasParseErrors(result, UnrelatedBrokenProjectRelativePath);
        }

        /// <summary>
        /// What: when a file listed as changed since the last compile misspells "partial", the type
        /// name in its text still marks it as a part of the type, and the edited method is skipped
        /// with a reason that names it.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAChangedFileMisspellsPartial_NamesThatFile()
        {
            string misspelledSibling = WriteEdited("PartialChangedSiblingMisspelled.cs", MisspelledPartialPart);
            List<string> assemblySourcePaths =
                new List<string>(BuildAbsoluteAssemblySourcePaths(FindCompilationAssembly().sourceFiles))
                {
                    misspelledSibling
                };

            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(
                new[] { BuildOwnOnlyEditSource("PartialChangedSiblingMisspelledEdited.cs") },
                assemblySourcePathsOverride: assemblySourcePaths.ToArray(),
                changedSiblingSourcePaths: new[] { misspelledSibling });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedAsOtherPartChanged(result, "OwnOnly", "PartialChangedSiblingMisspelled.cs");
        }

        /// <summary>
        /// What: when another file of the run cannot be read, nothing tells which types it holds parts
        /// of, so the edited method of the partial type is skipped with a reason that names that file.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyEdit_WhenAnotherRunFileCannotBeRead_NamesThatFile()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string missingPath = Path.Combine(
                projectRoot,
                HotReloadConstants.TestSourcesRelativeDirectory,
                "PartialUnreadableRunFile.cs");
            Assert.That(File.Exists(missingPath), Is.False, "The unreadable run file must not exist: " + missingPath);

            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildOwnOnlyEditSource("PartialUnreadableRunFileEdited.cs"),
                new TransformWorkerSourceDto
                {
                    sourcePath = missingPath,
                    projectRelativePath = UnreadableProjectRelativePath,
                    snapshotSource = string.Empty
                }
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertSkippedAsOtherPartChanged(result, "OwnOnly", UnreadableProjectRelativePath);
            AssertFileHasParseErrors(result, UnreadableProjectRelativePath);
        }

        /// <summary>
        /// What: a body of a partial type that calls an internal method of a plain type the run was
        /// not given binds, as the same call from a plain type does (the control run of the same test).
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyCallingInternalMethodOfUnpassedPlainType_Binds()
        {
            const string call = "HotReloadInternalMemberHost.InternalStaticValue()";
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildCallerPlainValueEdit("ReproAControl.cs", call)
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproAPartial.cs", call)
            });

            AssertControlAndReproEmitted(control, "PlainValue", repro, "OwnOnly");
        }

        /// <summary>
        /// What: a plain type's body that calls an internal method of an unpassed type binds when an
        /// edit of a partial type is in the same run, as it does when the plain file is passed alone
        /// (the control run of the same test).
        /// </summary>
        [Test]
        public async Task Run_PlainFilePassedNextToPartialTypeEdit_CallingInternalMethodOfUnpassedType_Binds()
        {
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildCallerInternalCallEdit("ReproB1Control.cs")
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproB1Partial.cs", "2"),
                BuildCallerInternalCallEdit("ReproB1Caller.cs")
            });

            AssertControlAndReproEmitted(control, "CallsInternal", repro, "CallsInternal");
        }

        /// <summary>
        /// What: a plain file that comes back as a sibling to re-bind its active patches while an edit
        /// of a partial type is passed: its body that calls an internal method of an unpassed type
        /// binds, as it does when the passed edit is in a plain type (the control run of the same test).
        /// </summary>
        [Test]
        public async Task Run_PlainSiblingBroughtBackNextToPartialTypeEdit_CallingInternalMethodOfUnpassedType_Binds()
        {
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildEditedFixtureSource(PlainDerivedFileName, "ReproB2ControlPassed.cs", PlainDerivedValueBody, "return 11;"),
                AsReappliedSibling(BuildCallerInternalCallEdit("ReproB2ControlSibling.cs"))
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproB2Partial.cs", "2"),
                AsReappliedSibling(BuildCallerInternalCallEdit("ReproB2Sibling.cs"))
            });

            AssertControlAndReproEmitted(control, "CallsInternal", repro, "CallsInternal");
        }

        /// <summary>
        /// What: a body of a partial type that calls an internal method another partial type declares
        /// in a separate file the run was not given binds, as the same call from a plain type does
        /// (the control run of the same test).
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyCallingInternalMethodOfAnotherUnpassedPartialType_Binds()
        {
            const string call = "new HotReloadPartialInternalPeer().PeerInternalValue()";
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildCallerPlainValueEdit("ReproC1Control.cs", call)
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproC1Partial.cs", call)
            });

            AssertControlAndReproEmitted(control, "PlainValue", repro, "OwnOnly");
        }

        /// <summary>
        /// What: the same call, with the other partial type's file wrapped whole in a
        /// conditional-compilation block whose symbol the assembly defines, binds as the same call
        /// from a plain type does (the control run of the same test).
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyCallingInternalMethodOfAnotherUnpassedPartialTypeInAConditionalFile_Binds()
        {
            const string call = "new HotReloadPartialInternalGuardedPeer().GuardedPeerInternalValue()";
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildCallerPlainValueEdit("ReproC2Control.cs", call)
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproC2Partial.cs", call)
            });

            AssertControlAndReproEmitted(control, "PlainValue", repro, "OwnOnly");
        }

        /// <summary>
        /// What: a body of a partial type that uses a non-public member of a type the run was not
        /// given binds, whatever the kind of member, as the same use from a plain type does (the
        /// control run of the same test).
        /// </summary>
        [TestCase("InternalInstanceMethod", "new HotReloadInternalMemberHost().InternalInstanceValue()")]
        [TestCase("InternalField", "new HotReloadInternalMemberHost().InternalField")]
        [TestCase("InternalProperty", "new HotReloadInternalMemberHost().InternalProperty")]
        [TestCase("InternalType", "HotReloadInternalOnlyType.Value()")]
        [TestCase("InternalMethodOfAnInternalResult", "HotReloadInternalMemberHost.InternalSelf().InternalInstanceValue()")]
        [TestCase("ProtectedInternalMethod", "new HotReloadInternalMemberHost().ProtectedInternalValue()")]
        [TestCase(
            "PublicMemberOfInternalTypeOfAnotherAssemblyThroughInternalsVisibleTo",
            "global::io.github.hatayama.UnityCliLoop.FirstPartyTools.HotReloadConstants.TestSourcesRelativeDirectory.Length")]
        public async Task Run_PartialTypeBodyUsingNonPublicMemberOfUnpassedType_Binds(string memberKind, string expression)
        {
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildCallerPlainValueEdit("ReproKindControl" + memberKind + ".cs", expression)
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("ReproKindPartial" + memberKind + ".cs", expression)
            });

            AssertControlAndReproEmitted(control, "PlainValue", repro, "OwnOnly");
        }

        /// <summary>
        /// What: a body of a partial type that uses a member a compiled base type grants its derived
        /// types binds, as the same use from a plain derived type does (the control run of the same
        /// test).
        /// </summary>
        [TestCase("Protected", "ProtectedValue()")]
        [TestCase("PrivateProtected", "PrivateProtectedValue()")]
        [TestCase("InheritedInternalThroughThis", "this.InternalInstanceValue()")]
        public async Task Run_DerivedPartialTypeBodyUsingBaseMemberOfUnpassedType_Binds(string memberKind, string expression)
        {
            TransformWorkerClientResult control = await RunWorkerOnSourcesAsync(new[]
            {
                BuildEditedFixtureSource(
                    PlainDerivedFileName,
                    "ReproBaseControl" + memberKind + ".cs",
                    PlainDerivedValueBody,
                    "return " + expression + ";")
            });
            TransformWorkerClientResult repro = await RunWorkerOnSourcesAsync(new[]
            {
                BuildEditedFixtureSource(
                    PartialDerivedFileName,
                    "ReproBasePartial" + memberKind + ".cs",
                    PartialDerivedValueBody,
                    "return " + expression + ";")
            });

            AssertControlAndReproEmitted(control, "DerivedValue", repro, "DerivedValue");
        }

        /// <summary>
        /// What: a body of a partial type that uses an internal member of a type the run was not given
        /// where the patched method cannot run it in place (inside a lambda, a local function or a
        /// query, in an iterator or async method, as a method passed as a delegate, by a bare name, or
        /// next to a lambda, local function or query that works with the member's result) is skipped
        /// with a reason that says the member is internal, not that a part is missing.
        /// </summary>
        [TestCase("Lambda", "OwnOnly", null, "System.Func<int> read = () => HotReloadInternalMemberHost.InternalStaticValue(); return read();")]
        [TestCase("LocalFunction", "OwnOnly", null, "int Read() { return HotReloadInternalMemberHost.InternalStaticValue(); } return Read();")]
        [TestCase("Query", "OwnOnly", null, "return (from value in new[] { 1 } select value + HotReloadInternalMemberHost.InternalStaticValue()).First();")]
        [TestCase("LambdaUsingTheResult", "OwnOnly", null, "var host = HotReloadInternalMemberHost.InternalSelf(); System.Func<int> read = () => host.InternalInstanceValue(); return read();")]
        [TestCase("LambdaParameterFromTheResult", "OwnOnly", null, "return System.Array.Exists(HotReloadInternalMemberHost.InternalHosts(), host => host.InternalField > 0) ? 1 : 0;")]
        [TestCase("LocalFunctionUsingTheResult", "OwnOnly", null, "var host = HotReloadInternalMemberHost.InternalSelf(); int Read() { return host.InternalInstanceValue(); } return Read();")]
        [TestCase("QueryOverTheResult", "OwnOnly", null, "var hosts = HotReloadInternalMemberHost.InternalHosts(); return (from host in hosts select host.InternalField).First();")]
        [TestCase("MethodPassedAsDelegate", "OwnOnly", null, "System.Func<int> read = HotReloadInternalMemberHost.InternalStaticValue; return read();")]
        [TestCase("Iterator", "IteratorValues", "yield return _seed;", "yield return HotReloadInternalMemberHost.InternalStaticValue();")]
        [TestCase("Async", "AsyncValue", "return 50;", "return HotReloadInternalMemberHost.InternalStaticValue();")]
        [TestCase("BareName", "DerivedValue", PartialDerivedValueBody, "return InternalInstanceValue();")]
        public async Task Skip_PartialTypeBodyUsingInternalMemberWhereItCannotBePatchedInPlace_SaysWhy(
            string form,
            string methodName,
            string partialDerivedFragment,
            string replacement)
        {
            string editedFileName = "InternalOutOfReach" + form + ".cs";
            TransformWorkerSourceDto edit = partialDerivedFragment == null
                ? BuildFixtureOwnOnlyBodyEdit(editedFileName, replacement)
                : BuildEditedFixtureSource(PartialDerivedFileName, editedFileName, partialDerivedFragment, replacement);

            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[] { edit });

            string reason = AssertSkipped(result, methodName);
            Assert.That(reason, Does.Contain("is internal to 'HotReloadInternalMemberHost'"), FormatSkipped(result));
            Assert.That(reason, Does.Not.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a getter of a partial type whose lambda reads a private member, so the whole getter
        /// runs through a delegating shim, is skipped with a reason that says the internal member it
        /// uses outside the lambda is internal.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeGetterWithALambdaReadingAPrivateMember_UsingInternalMemberDirectly_SaysWhy()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildEditedFixtureSource(
                    PartialDerivedFileName,
                    "InternalOutOfReachDelegatingGetter.cs",
                    "return 40;",
                    "System.Func<int> read = () => this._seed; return read() + HotReloadInternalMemberHost.InternalStaticValue();")
            });

            Assert.That(AssertSkipped(result, "get_DerivedProperty"), Does.Contain("is internal to"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a getter of a partial type whose lambda works with the result of an internal member
        /// of a type the run was not given is skipped with the internal-member reason, as a method
        /// with the same body is.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeGetterWithALambdaUsingTheResultOfAnInternalMember_SaysWhy()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildEditedFixtureSource(
                    PartialDerivedFileName,
                    "InternalOutOfReachGetterLambdaUsingTheResult.cs",
                    "return 40;",
                    "var host = HotReloadInternalMemberHost.InternalSelf(); System.Func<int> read = () => host.InternalInstanceValue(); return read();")
            });

            Assert.That(AssertSkipped(result, "get_DerivedProperty"), Does.Contain("is internal to"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a body of a partial type that uses a private member of a type the run was not given
        /// keeps the missing-name reason.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyUsingPrivateMemberOfUnpassedType_KeepsTheMissingNameReason()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("UnpassedPrivateMember.cs", "HotReloadInternalMemberHost.PrivateStaticValue()")
            });

            Assert.That(AssertSkipped(result, "OwnOnly"), Does.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a body of a partial type that uses an internal member of a type in another assembly
        /// keeps the missing-name reason.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyUsingInternalMemberOfATypeOfAnotherAssembly_KeepsTheMissingNameReason()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("AnotherAssemblyInternalMember.cs", InternalMemberOfATypeOfAnotherAssembly)
            });

            Assert.That(AssertSkipped(result, "OwnOnly"), Does.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a body of a partial type that uses an internal member of an unpassed type next to a
        /// name nothing declares is skipped with a reason that names the undeclared name.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyUsingInternalMemberNextToAnUnresolvedName_NamesTheUnresolvedName()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit(
                    "InternalMemberNextToUnresolvedName.cs",
                    "HotReloadInternalMemberHost.InternalStaticValue() + NoSuchName")
            });

            string reason = AssertSkipped(result, "OwnOnly");
            Assert.That(reason, Does.Contain("NoSuchName"), FormatSkipped(result));
            Assert.That(reason, Does.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: when a part of the type is in no source file the run can see, a body that calls that
        /// part's internal method through 'this' keeps the missing-name reason, although the compiled
        /// type declares the method.
        /// </summary>
        [Test]
        public async Task Skip_PartialTypeBodyUsingThisInternalMemberOfAPartNotAmongTheAssemblySources_KeepsTheMissingNameReason()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(
                new[] { BuildFixtureOwnOnlyEdit("PartialThisInternalOfPartNotInSources.cs", "this.OtherPartInternalValue()") },
                assemblySourcePathsOverride: BuildAssemblySourcePathsWithout(OtherPartFileName));

            Assert.That(AssertSkipped(result, "OwnOnly"), Does.Contain("generated at compile time"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a body of a partial type that calls an internal method of a type nested in a type the
        /// run was not given is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyUsingInternalMemberOfANestedUnpassedType_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("NestedTypeInternalMember.cs", "HotReloadInternalMemberHost.Nested.NestedInternalValue()")
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "OwnOnly");
        }

        /// <summary>
        /// What: an existing getter of a partial type that calls an internal method of a type the run
        /// was not given is emitted.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeGetterUsingInternalMemberOfUnpassedType_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "PartialGetterUsesUnpassedInternal.cs",
                OtherPartPropertyGetter,
                "get { return HotReloadInternalMemberHost.InternalStaticValue(); }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "get_ReadsOtherPartProperty");
        }

        /// <summary>
        /// What: a getter of a file brought back to re-bind its active patches that calls an internal
        /// method of a type the run was not given is emitted.
        /// </summary>
        [Test]
        public async Task Run_SiblingBroughtBack_GetterUsingInternalMemberOfUnpassedType_EmitsEntry()
        {
            TransformWorkerClientResult result = await RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit("SiblingGetterPassed.cs", "2"),
                AsReappliedSibling(BuildEditedFixtureSource(
                    CallerFileName,
                    "SiblingGetterCaller.cs",
                    "get { return 30; }",
                    "get { return HotReloadInternalMemberHost.InternalStaticValue(); }"))
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertEmitted(result, "get_CallerProperty");
        }

        /// <summary>
        /// What: a method of a file brought back to re-bind its active patches whose body uses an
        /// internal member of an unpassed type inside a lambda keeps the sibling's skip reason.
        /// </summary>
        [Test]
        public async Task Skip_SiblingBroughtBack_UsingInternalMemberInsideALambda_KeepsTheSiblingReason()
        {
            TransformWorkerClientResult result = await RunWithSiblingPlainValueAsync(
                "SiblingInternalInLambda",
                "System.Func<int> read = () => HotReloadInternalMemberHost.InternalStaticValue(); return read();");

            Assert.That(AssertSkipped(result, "PlainValue"), Does.Contain("brought back to re-bind"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a method of a file brought back to re-bind its active patches whose lambda works with
        /// the result of an internal member of an unpassed type keeps the sibling's skip reason.
        /// </summary>
        [Test]
        public async Task Skip_SiblingBroughtBack_WithALambdaUsingTheResultOfAnInternalMember_KeepsTheSiblingReason()
        {
            TransformWorkerClientResult result = await RunWithSiblingPlainValueAsync(
                "SiblingLambdaUsingTheResult",
                "var host = HotReloadInternalMemberHost.InternalSelf(); System.Func<int> read = () => host.InternalInstanceValue(); return read();");

            Assert.That(AssertSkipped(result, "PlainValue"), Does.Contain("brought back to re-bind"), FormatSkipped(result));
        }

        /// <summary>
        /// What: a method of a file brought back to re-bind its active patches whose body uses an
        /// internal member of an unpassed type next to a name nothing declares keeps the sibling's skip
        /// reason.
        /// </summary>
        [Test]
        public async Task Skip_SiblingBroughtBack_UsingInternalMemberNextToAnUnresolvedName_KeepsTheSiblingReason()
        {
            TransformWorkerClientResult result = await RunWithSiblingPlainValueAsync(
                "SiblingInternalNextToUnresolvedName",
                "return HotReloadInternalMemberHost.InternalStaticValue() + NoSuchName;");

            Assert.That(AssertSkipped(result, "PlainValue"), Does.Contain("brought back to re-bind"), FormatSkipped(result));
        }

        // A fixture file next to these tests, edited once and described as a passed run source whose
        // snapshot is the file on disk.
        private static TransformWorkerSourceDto BuildEditedFixtureSource(
            string fileName,
            string editedFileName,
            string fragment,
            string replacement)
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(fileName));
            return new TransformWorkerSourceDto
            {
                sourcePath = WriteEdited(editedFileName, ReplaceOnce(onDisk, fragment, replacement)),
                projectRelativePath = FixtureDirectoryProjectRelativePath + fileName,
                snapshotSource = onDisk
            };
        }

        // The edited main part of the partial fixture, with OwnOnly returning the given expression.
        private static TransformWorkerSourceDto BuildFixtureOwnOnlyEdit(string editedFileName, string returnedExpression)
        {
            string editedOwnOnly =
                "        public int OwnOnly()\n        {\n            return " + returnedExpression + ";\n        }";
            return BuildEditedFixtureSource(FixtureFileName, editedFileName, OwnOnlyDeclaration, editedOwnOnly);
        }

        // The edited main part of the partial fixture, with OwnOnly's body replaced by the given line,
        // which may hold several statements.
        private static TransformWorkerSourceDto BuildFixtureOwnOnlyBodyEdit(string editedFileName, string bodyLine)
        {
            string editedOwnOnly =
                "        public int OwnOnly()\n        {\n            " + bodyLine + "\n        }";
            return BuildEditedFixtureSource(FixtureFileName, editedFileName, OwnOnlyDeclaration, editedOwnOnly);
        }

        // An edit of the partial fixture passed next to the plain caller, which comes back as a sibling
        // whose PlainValue body is the given line.
        private static Task<TransformWorkerClientResult> RunWithSiblingPlainValueAsync(string label, string plainValueBody)
        {
            return RunWorkerOnSourcesAsync(new[]
            {
                BuildFixtureOwnOnlyEdit(label + "Passed.cs", "2"),
                AsReappliedSibling(BuildEditedFixtureSource(CallerFileName, label + "Sibling.cs", CallerPlainValueBody, plainValueBody))
            });
        }

        // The plain caller with PlainValue returning the given expression.
        private static TransformWorkerSourceDto BuildCallerPlainValueEdit(string editedFileName, string returnedExpression)
        {
            return BuildEditedFixtureSource(
                CallerFileName,
                editedFileName,
                CallerPlainValueBody,
                "return " + returnedExpression + ";");
        }

        // The plain caller with an edit inside the body that calls the internal method.
        private static TransformWorkerSourceDto BuildCallerInternalCallEdit(string editedFileName)
        {
            return BuildEditedFixtureSource(
                CallerFileName,
                editedFileName,
                CallerInternalCallBody,
                CallerInternalCallBodyEdited);
        }

        private static TransformWorkerSourceDto AsReappliedSibling(TransformWorkerSourceDto source)
        {
            source.reappliedSibling = true;
            return source;
        }

        // Why one assertion over both runs: the control and the repro fail for different reasons, and
        // the reader needs both runs' rows even when the control already failed.
        private static void AssertControlAndReproEmitted(
            TransformWorkerClientResult control,
            string controlMethodName,
            TransformWorkerClientResult repro,
            string reproMethodName)
        {
            List<string> failures = new List<string>();
            CollectMissingEntry("control", control, controlMethodName, failures);
            CollectMissingEntry("repro", repro, reproMethodName, failures);
            Assert.That(failures, Is.Empty, string.Join("\n\n", failures));
        }

        private static void CollectMissingEntry(
            string runLabel,
            TransformWorkerClientResult result,
            string methodName,
            List<string> failures)
        {
            if (!result.Success)
            {
                failures.Add(runLabel + " run failed: " + result.ErrorMessage);
                return;
            }

            if (FindEntry(result, methodName) == null)
            {
                failures.Add(
                    runLabel + ": missing entry for " + methodName + ".\n"
                    + FormatSkipped(result) + "\n" + FormatFileErrors(result));
            }
        }

        private static string FormatFileErrors(TransformWorkerClientResult result)
        {
            List<string> rows = new List<string>();
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (string parseError in file.parseErrors ?? Array.Empty<string>())
                {
                    rows.Add(file.projectRelativePath + " :: " + parseError);
                }
            }

            return rows.Count == 0 ? "FileErrors=(none)" : "FileErrors=\n" + string.Join("\n", rows);
        }

        // The edited main part. OwnOnly reads only its own part, so it binds whether or not the other
        // part is visible, and only an untrusted other part can keep it from being emitted.
        private static TransformWorkerSourceDto BuildOwnOnlyEditSource(string editedFileName)
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(FixtureFileName));
            return new TransformWorkerSourceDto
            {
                sourcePath = WriteEdited(editedFileName, ReplaceOnce(onDisk, OwnOnlyDeclaration, OwnOnlyEdited)),
                projectRelativePath = FixtureProjectRelativePath,
                snapshotSource = onDisk
            };
        }

        // A run file that stands for the fixture's other part, with the given text.
        private static TransformWorkerSourceDto BuildOtherPartSource(string editedFileName, string contents)
        {
            return new TransformWorkerSourceDto
            {
                sourcePath = WriteEdited(editedFileName, contents),
                projectRelativePath = OtherPartProjectRelativePath,
                snapshotSource = File.ReadAllText(ResolveFixturePath(OtherPartFileName))
            };
        }

        private static void AssertSkippedAsOtherPartChanged(
            TransformWorkerClientResult result,
            string methodName,
            string namedPath)
        {
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be applied.\n" + FormatSkipped(result));
            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName);
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for " + methodName + ".\n" + FormatSkipped(result));
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformPartialOtherPartChanged),
                FormatSkipped(result));
            Assert.That(HotReloadWorkerReasonText.Render(skipped.reason), Does.Contain(namedPath), FormatSkipped(result));
        }

        private static void AssertFileHasParseErrors(TransformWorkerClientResult result, string projectRelativePath)
        {
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                if (file.projectRelativePath == projectRelativePath)
                {
                    Assert.That(file.parseErrors, Is.Not.Empty, "Expected parse errors on " + projectRelativePath);
                    return;
                }
            }

            Assert.Fail("Missing file row for " + projectRelativePath);
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(
            string editedFileName,
            string fragment,
            string replacement,
            string[] changedSiblingSourcePaths = null,
            bool changedSiblingScanComplete = true)
        {
            string onDisk = File.ReadAllText(ResolveFixturePath(FixtureFileName));
            string edited = ReplaceOnce(onDisk, fragment, replacement);
            return await RunWorkerOnSourceAsync(
                WriteEdited(editedFileName, edited),
                FixtureProjectRelativePath,
                onDisk,
                changedSiblingSourcePaths: changedSiblingSourcePaths,
                changedSiblingScanComplete: changedSiblingScanComplete);
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not look at, and the assert would pass or fail for the wrong reason.
        private static string ReplaceOnce(string source, string fragment, string replacement)
        {
            int first = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Fragment missing from the fixture: " + fragment);
            Assert.That(
                source.LastIndexOf(fragment, StringComparison.Ordinal),
                Is.EqualTo(first),
                "Fragment occurs more than once in the fixture: " + fragment);
            return source.Substring(0, first) + replacement + source.Substring(first + fragment.Length);
        }

        // The rendered reason of the method's skipped row, once the run succeeded without an entry for
        // the method.
        private static string AssertSkipped(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be applied.\n" + FormatSkipped(result));
            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName);
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for " + methodName + ".\n" + FormatSkipped(result));
            return HotReloadWorkerReasonText.Render(skipped.reason);
        }

        private static TransformWorkerEntryDto AssertEmitted(TransformWorkerClientResult result, string methodName)
        {
            TransformWorkerEntryDto entry = FindEntry(result, methodName);
            Assert.That(entry, Is.Not.Null, "Missing entry for " + methodName + ".\n" + FormatSkipped(result));
            Assert.That(FindSkipReason(result, methodName), Is.Null, FormatSkipped(result));
            return entry;
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static int CountEntries(TransformWorkerClientResult result, string methodName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    count++;
                }
            }

            return count;
        }

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return skipped;
                }
            }

            return null;
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodNameFragment))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            if (result.Output == null || result.Output.skipped == null || result.Output.skipped.Length == 0)
            {
                return "Skipped=(none)";
            }

            List<string> rows = new List<string>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                rows.Add(
                    skipped.method + " :: "
                    + (skipped.reason == null ? "(no reason)" : HotReloadWorkerReasonText.Render(skipped.reason)));
            }

            return "Skipped=\n" + string.Join("\n", rows);
        }

        private static string WriteEdited(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string ResolveFixturePath(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName);
            Assert.That(File.Exists(path), Is.True, "Partial type fixture missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string[] BuildAssemblySourcePathsWithout(string fileName)
        {
            List<string> paths = new List<string>();
            foreach (string path in BuildAbsoluteAssemblySourcePaths(FindCompilationAssembly().sourceFiles))
            {
                if (!string.Equals(Path.GetFileName(path), fileName, StringComparison.Ordinal))
                {
                    paths.Add(path);
                }
            }

            return paths.ToArray();
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource,
            string[] assemblySourcePathsOverride = null,
            string[] changedSiblingSourcePaths = null,
            bool changedSiblingScanComplete = true)
        {
            TransformWorkerSourceDto source = new TransformWorkerSourceDto
            {
                sourcePath = sourcePath,
                projectRelativePath = projectRelativePath,
                snapshotSource = snapshotSource
            };
            return await RunWorkerOnSourcesAsync(
                new[] { source },
                assemblySourcePathsOverride,
                changedSiblingSourcePaths,
                changedSiblingScanComplete);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourcesAsync(
            TransformWorkerSourceDto[] sources,
            string[] assemblySourcePathsOverride = null,
            string[] changedSiblingSourcePaths = null,
            bool changedSiblingScanComplete = true)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            string[] referencePaths = BuildAbsoluteReferencePaths(
                compilationAssembly.allReferences,
                targetDllPath);
            string[] assemblySourcePaths = assemblySourcePathsOverride
                ?? BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles);

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = sources,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths,
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = assemblySourcePaths,
                changedSiblingSourcePaths = changedSiblingSourcePaths ?? Array.Empty<string>(),
                changedSiblingScanComplete = changedSiblingScanComplete,
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("CompilationPipeline assembly not found: " + TestAssemblyName);
            return null;
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                    {
                        continue;
                    }

                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            bool hasTarget = false;
            foreach (string path in paths)
            {
                if (string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    hasTarget = true;
                    break;
                }
            }

            if (!hasTarget)
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                string absoluteSourcePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absoluteSourcePath);
            }

            return paths;
        }
    }
}
