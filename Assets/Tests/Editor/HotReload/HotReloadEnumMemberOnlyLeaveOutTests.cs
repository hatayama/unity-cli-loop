using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers which files a default-selection group leaves out after its first transform run, and
    /// the worker input of the rerun without them.
    /// </summary>
    public sealed class HotReloadEnumMemberOnlyLeaveOutTests
    {
        private const string EnumPath = "Assets/Scripts/Kind.cs";
        private const string OtherEnumPath = "Assets/Scripts/Mode.cs";
        private const string CallerPath = "Assets/Scripts/Caller.cs";
        private const string ApiPath = "Assets/Scripts/Registry.cs";
        private const string EnumMemberName = "Game.Kind.Third";

        /// <summary>
        /// The one change that keeps a file from being an enum-only file, for the case that
        /// breaks exactly one condition.
        /// </summary>
        public enum OtherChange
        {
            Entry,
            OwnSkippedRow,
            AddedField,
            AddedConst,
            RemovedMember,
            RemovedMethodSignature,
            ParseError,
            NoSourceHash,
            NoEnumMember,
            NewType
        }

        private readonly HotReloadEnumMemberOnlyLeaveOut _leaveOut = new HotReloadEnumMemberOnlyLeaveOut();

        /// <summary>
        /// What: an enum-only file that a carried-in row names as the source of the type it splits
        /// is left out.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_EnumOnlyFileNamedByACarriedInRow_FindsIt()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, CarriedInRow(CallerPath, EnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.EqualTo(new[] { EnumPath }));
        }

        /// <summary>
        /// What: an enum-only file that a compiled-signature row names as the source of the type it
        /// splits is left out, even though that row's declaring files name the compiled API.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_CompiledSignatureRowNamingTheFile_FindsIt()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, CompiledSignatureRow(CallerPath, EnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.EqualTo(new[] { EnumPath }));
        }

        /// <summary>
        /// What: a compiled-signature row that names no source file of the run leaves nothing out,
        /// because only its declaring files, which name the compiled API, are known.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_CompiledSignatureRowWithoutSplitSourceFiles_FindsNone()
        {
            TransformWorkerSkippedDto row = CompiledSignatureRow(CallerPath, EnumPath);
            row.reason.splitSourceFiles = null;

            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, row),
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: a skipped row of another code leaves nothing out, even when its declaring files
        /// happen to name the enum-only file.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_SplitRowOfAnotherCode_FindsNone()
        {
            TransformWorkerSkippedDto row = CarriedInRow(CallerPath, EnumPath);
            row.reason.code = HotReloadWorkerReasonCode.AddedMethodBodyUnbound;

            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, row),
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: an enum-only file is kept when the split row names another file as the source of
        /// its type, since leaving it out would not resolve that row.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_SplitRowNamingAnotherFile_FindsNone()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(OtherEnumPath), DefaultFile(CallerPath) },
                Output(
                    new[] { EnumOnly(EnumPath), Caller(OtherEnumPath), Caller(CallerPath) },
                    CarriedInRow(CallerPath, OtherEnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: of two enum-only files, only the one a split row names is left out.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_TwoEnumOnlyFilesOneNamed_FindsOnlyTheNamedFile()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(OtherEnumPath), DefaultFile(CallerPath) },
                Output(
                    new[] { EnumOnly(EnumPath), EnumOnly(OtherEnumPath), Caller(CallerPath) },
                    CarriedInRow(CallerPath, OtherEnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.EqualTo(new[] { OtherEnumPath }));
        }

        /// <summary>
        /// What: two enum-only files named by split rows are both left out, in the order of the files.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_TwoEnumOnlyFilesBothNamed_FindsBothInFileOrder()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(CallerPath), DefaultFile(OtherEnumPath), DefaultFile(EnumPath) },
                Output(
                    new[] { Caller(CallerPath), EnumOnly(OtherEnumPath), EnumOnly(EnumPath) },
                    CarriedInRow(CallerPath, EnumPath),
                    CompiledSignatureRow(CallerPath, OtherEnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.EqualTo(new[] { OtherEnumPath, EnumPath }));
        }

        /// <summary>
        /// What: an enum-only file with no split row naming it stays in the run.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_NoSplitRow_FindsNone()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }),
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: a file the caller passed, or one the run brought back as a sibling, is never left
        /// out, because only a default selection chose files the caller did not name.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_FileNotDefaultSelected_FindsNone()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { new HotReloadLeaveOutFile(EnumPath, false, false), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, CarriedInRow(CallerPath, EnumPath)),
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: a file that holds changes of its own in the domain stays in the run, because the
        /// sibling plan would bring it back and its step is a compile, not leaving it out.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_ActiveFile_FindsNone()
        {
            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                Output(new[] { EnumOnly(EnumPath), Caller(CallerPath) }, CarriedInRow(CallerPath, EnumPath)),
                new HashSet<string>(StringComparer.Ordinal) { EnumPath });

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: unchanged-method rows of the file do not keep it in the run, since they apply nothing.
        /// </summary>
        [Test]
        public void FindLeftOutPaths_UnchangedRowsOfTheFile_StillFindsIt()
        {
            TransformWorkerOutputDto output = Output(
                new[] { EnumOnly(EnumPath), Caller(CallerPath) },
                CarriedInRow(CallerPath, EnumPath));
            output.unchangedMethods = new[]
            {
                new TransformWorkerUnchangedMethodDto
                {
                    sourceProjectRelativePath = EnumPath,
                    typeMetadataName = "Game.KindNames",
                    methodName = "Describe",
                    parameterTypeFullNames = Array.Empty<string>()
                }
            };

            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { DefaultFile(EnumPath), DefaultFile(CallerPath) },
                output,
                NoActivePaths());

            Assert.That(leftOut, Is.EqualTo(new[] { EnumPath }));
        }

        /// <summary>
        /// What: a file with any change besides added enum members stays in the run, one condition
        /// at a time, because leaving it out would drop that change.
        /// </summary>
        [TestCase(OtherChange.Entry)]
        [TestCase(OtherChange.OwnSkippedRow)]
        [TestCase(OtherChange.AddedField)]
        [TestCase(OtherChange.AddedConst)]
        [TestCase(OtherChange.RemovedMember)]
        [TestCase(OtherChange.RemovedMethodSignature)]
        [TestCase(OtherChange.ParseError)]
        [TestCase(OtherChange.NoSourceHash)]
        [TestCase(OtherChange.NoEnumMember)]
        [TestCase(OtherChange.NewType)]
        public void FindLeftOutPaths_FileWithAnotherChange_FindsNone(OtherChange change)
        {
            TransformWorkerFileOutputDto enumOutput = EnumOnly(EnumPath);
            List<TransformWorkerSkippedDto> skipped = new List<TransformWorkerSkippedDto>
            {
                CarriedInRow(CallerPath, EnumPath)
            };
            List<TransformWorkerEntryDto> entries = new List<TransformWorkerEntryDto>();
            bool declaresNewType = false;
            switch (change)
            {
                case OtherChange.Entry:
                    entries.Add(new TransformWorkerEntryDto { sourceProjectRelativePath = EnumPath, methodName = "Describe" });
                    break;
                case OtherChange.OwnSkippedRow:
                    skipped.Add(new TransformWorkerSkippedDto
                    {
                        sourceProjectRelativePath = EnumPath,
                        method = "Game.KindNames.Describe()",
                        reason = new TransformWorkerReasonDto { code = HotReloadWorkerReasonCode.AddedMethodBodyUnbound }
                    });
                    break;
                case OtherChange.AddedField:
                    enumOutput.addedFieldNames = new[] { "Game.KindNames.count" };
                    break;
                case OtherChange.AddedConst:
                    enumOutput.addedConstNames = new[] { "Game.KindNames.Limit" };
                    break;
                case OtherChange.RemovedMember:
                    enumOutput.removedMembers = new[] { new TransformWorkerRemovedMemberDto() };
                    break;
                case OtherChange.RemovedMethodSignature:
                    enumOutput.removedMethodSignatures = new[] { new TransformWorkerRemovedMethodSignatureDto() };
                    break;
                case OtherChange.ParseError:
                    enumOutput.parseErrors = new[] { "CS1002: ; expected" };
                    break;
                case OtherChange.NoSourceHash:
                    enumOutput.sourceContentSha256 = string.Empty;
                    break;
                case OtherChange.NoEnumMember:
                    enumOutput.addedEnumMemberNames = Array.Empty<string>();
                    break;
                case OtherChange.NewType:
                    declaresNewType = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change), change, null);
            }

            TransformWorkerOutputDto output = Output(new[] { enumOutput, Caller(CallerPath) }, skipped.ToArray());
            output.entries = entries.ToArray();

            IReadOnlyList<string> leftOut = _leaveOut.FindLeftOutPaths(
                new[] { new HotReloadLeaveOutFile(EnumPath, true, declaresNewType), DefaultFile(CallerPath) },
                output,
                NoActivePaths());

            Assert.That(leftOut, Is.Empty);
        }

        /// <summary>
        /// What: the rerun's input drops only the left-out sources, keeps the others in order, and
        /// carries every other field of the first input over unchanged, so the rerun binds the same
        /// artifacts, siblings and labels as the first run.
        /// </summary>
        [Test]
        public void BuildRetryInput_LeavesOutOnlyTheNamedSourcesAndCopiesTheRest()
        {
            TransformWorkerSourceDto caller = Source(CallerPath);
            TransformWorkerSourceDto enumSource = Source(EnumPath);
            TransformWorkerSourceDto other = Source(OtherEnumPath);
            TransformWorkerInputDto first = new TransformWorkerInputDto
            {
                // A prepare operation is never sent with a transform input; it is set here only to
                // show the rerun is a transform whatever the first input carries.
                operation = "prepareIntroducedTypes",
                sources = new[] { caller, enumSource, other },
                defines = new[] { "UNITY_EDITOR" },
                referencePaths = new[] { "/refs/UnityEngine.dll" },
                targetTypesAssemblyPath = "/Library/ScriptAssemblies/Game.dll",
                targetAssemblyName = "Game",
                targetAssemblyMvid = "mvid",
                excludedMethodKeys = new[] { "excluded" },
                excludedAddedMethodKeys = new[] { "excludedAdded" },
                assemblySourcePaths = new[] { "/project/" + EnumPath },
                changedSiblingSourcePaths = new[] { "/project/Assets/Scripts/Sibling.cs" },
                introducedTypeArtifacts = new[] { new TransformWorkerIntroducedTypeArtifactDto() },
                activeMethodLabels = new[] { "Game.Caller.Run()" }
            };

            TransformWorkerInputDto retry = _leaveOut.BuildRetryInput(first, new[] { EnumPath });

            Assert.That(retry, Is.Not.SameAs(first));
            Assert.That(retry.sources, Is.EqualTo(new[] { caller, other }));
            Assert.That(retry.operation, Is.Null);
            Assert.That(retry.defines, Is.SameAs(first.defines));
            Assert.That(retry.referencePaths, Is.SameAs(first.referencePaths));
            Assert.That(retry.targetTypesAssemblyPath, Is.EqualTo(first.targetTypesAssemblyPath));
            Assert.That(retry.targetAssemblyName, Is.EqualTo(first.targetAssemblyName));
            Assert.That(retry.targetAssemblyMvid, Is.EqualTo(first.targetAssemblyMvid));
            Assert.That(retry.excludedMethodKeys, Is.SameAs(first.excludedMethodKeys));
            Assert.That(retry.excludedAddedMethodKeys, Is.SameAs(first.excludedAddedMethodKeys));
            Assert.That(retry.assemblySourcePaths, Is.SameAs(first.assemblySourcePaths));
            Assert.That(retry.changedSiblingSourcePaths, Is.SameAs(first.changedSiblingSourcePaths));
            Assert.That(retry.introducedTypeArtifacts, Is.SameAs(first.introducedTypeArtifacts));
            Assert.That(retry.activeMethodLabels, Is.SameAs(first.activeMethodLabels));
        }

        /// <summary>
        /// What: a left-out path that is not among the first run's sources stops the rerun, since
        /// the rerun would otherwise report a different set of files than the group holds.
        /// </summary>
        [Test]
        public void BuildRetryInput_PathNotAmongTheSources_Throws()
        {
            TransformWorkerInputDto first = new TransformWorkerInputDto
            {
                sources = new[] { Source(CallerPath), Source(EnumPath) }
            };

            Assert.Throws<InvalidOperationException>(() => _leaveOut.BuildRetryInput(first, new[] { OtherEnumPath }));
        }

        /// <summary>
        /// What: leaving out every source stops the rerun, since a group result needs a file the
        /// rerun reports on.
        /// </summary>
        [Test]
        public void BuildRetryInput_LeavingOutEverySource_Throws()
        {
            TransformWorkerInputDto first = new TransformWorkerInputDto
            {
                sources = new[] { Source(EnumPath) }
            };

            Assert.Throws<InvalidOperationException>(() => _leaveOut.BuildRetryInput(first, new[] { EnumPath }));
        }

        /// <summary>
        /// What: the left-out warning names the file, its added enum members, and the compile
        /// that adds them.
        /// </summary>
        [Test]
        public void FormatLeftOutWarning_NamesTheFileItsMembersAndTheCompile()
        {
            string warning = _leaveOut.FormatLeftOutWarning(EnumPath, new[] { EnumMemberName, "Game.Kind.Fourth" });

            Assert.That(warning, Does.StartWith("Left '" + EnumPath + "' out of this reload"));
            Assert.That(warning, Does.Contain(EnumMemberName + ", Game.Kind.Fourth"));
            Assert.That(warning, Does.Contain("'uloop compile'"));
        }

        private static HotReloadLeaveOutFile DefaultFile(string path)
        {
            return new HotReloadLeaveOutFile(path, true, false);
        }

        private static HashSet<string> NoActivePaths()
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        private static TransformWorkerSourceDto Source(string path)
        {
            return new TransformWorkerSourceDto { sourcePath = "/project/" + path, projectRelativePath = path };
        }

        private static TransformWorkerFileOutputDto EnumOnly(string path)
        {
            TransformWorkerFileOutputDto output = Caller(path);
            output.addedEnumMemberNames = new[] { EnumMemberName };
            return output;
        }

        private static TransformWorkerFileOutputDto Caller(string path)
        {
            return new TransformWorkerFileOutputDto
            {
                projectRelativePath = path,
                sourceContentSha256 = "hash-of-" + path,
                parseErrors = Array.Empty<string>(),
                declarationDriftWarnings = Array.Empty<string>(),
                removedMembers = Array.Empty<TransformWorkerRemovedMemberDto>(),
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>(),
                addedFieldNames = Array.Empty<string>(),
                addedConstNames = Array.Empty<string>(),
                addedEnumMemberNames = Array.Empty<string>()
            };
        }

        private static TransformWorkerSkippedDto CarriedInRow(string callerPath, string declaringFile)
        {
            return new TransformWorkerSkippedDto
            {
                sourceProjectRelativePath = callerPath,
                method = "Game.Caller.TakeKind()",
                reason = new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                    declaringFiles = new[] { declaringFile }
                }
            };
        }

        private static TransformWorkerSkippedDto CompiledSignatureRow(string callerPath, string splitSourceFile)
        {
            return new TransformWorkerSkippedDto
            {
                sourceProjectRelativePath = callerPath,
                method = "Game.Caller.AcceptKind()",
                reason = new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                    declaringFiles = new[] { ApiPath },
                    splitSourceFiles = new[] { splitSourceFile }
                }
            };
        }

        private static TransformWorkerOutputDto Output(
            TransformWorkerFileOutputDto[] files,
            params TransformWorkerSkippedDto[] skipped)
        {
            return new TransformWorkerOutputDto
            {
                files = files,
                entries = Array.Empty<TransformWorkerEntryDto>(),
                skipped = skipped,
                unchangedMethods = Array.Empty<TransformWorkerUnchangedMethodDto>()
            };
        }
    }
}
