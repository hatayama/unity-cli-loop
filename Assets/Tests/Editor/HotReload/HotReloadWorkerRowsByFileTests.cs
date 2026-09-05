using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for splitting one group worker output into the rows of each edited file.
    /// </summary>
    public class HotReloadWorkerRowsByFileTests
    {
        private const string FirstPath = "Assets/Scripts/First.cs";
        private const string SecondPath = "Assets/Scripts/Second.cs";

        /// <summary>
        /// What: entries, skips and unchanged methods are split by the file each row came from.
        /// </summary>
        [Test]
        public void Build_RowsOfTwoFiles_SplitsEveryRowKindByItsFile()
        {
            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                CreateOutput(),
                new List<string> { FirstPath, SecondPath });

            Assert.That(rows.EntriesFor(FirstPath).Count, Is.EqualTo(2));
            Assert.That(rows.EntriesFor(FirstPath)[0].methodName, Is.EqualTo("FirstA"));
            Assert.That(rows.EntriesFor(FirstPath)[1].methodName, Is.EqualTo("FirstB"));
            Assert.That(rows.EntriesFor(SecondPath).Count, Is.EqualTo(1));
            Assert.That(rows.EntriesFor(SecondPath)[0].methodName, Is.EqualTo("SecondA"));

            Assert.That(rows.SkippedFor(FirstPath).Count, Is.EqualTo(1));
            Assert.That(rows.SkippedFor(FirstPath)[0].method, Is.EqualTo("First.Skipped"));
            Assert.That(rows.SkippedFor(SecondPath).Count, Is.EqualTo(0));

            Assert.That(rows.UnchangedFor(SecondPath).Count, Is.EqualTo(1));
            Assert.That(rows.UnchangedFor(SecondPath)[0].methodName, Is.EqualTo("SecondUnchanged"));
            Assert.That(rows.UnchangedFor(FirstPath).Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the per-file output of each edited file is reachable by that file's path.
        /// </summary>
        [Test]
        public void Build_FileOutputFor_ReturnsThePerFileRowOfThatFile()
        {
            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                CreateOutput(),
                new List<string> { FirstPath, SecondPath });

            Assert.That(rows.FileOutputFor(FirstPath).sourceContentSha256, Is.EqualTo("aaaa"));
            Assert.That(rows.FileOutputFor(SecondPath).sourceContentSha256, Is.EqualTo("bbbb"));
        }

        /// <summary>
        /// What: a file of the group that produced no rows reads back as empty rather than null,
        /// so callers do not need a null check per row kind.
        /// </summary>
        [Test]
        public void Build_GroupFileWithNoRows_ReturnsEmptyRowLists()
        {
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                shimSource = string.Empty,
                entries = new TransformWorkerEntryDto[0],
                skipped = new TransformWorkerSkippedDto[0],
                unchangedMethods = new TransformWorkerUnchangedMethodDto[0],
                files = new[] { CreateFileOutput(FirstPath, "aaaa") },
                parseErrors = new string[0],
                siblingConstDriftWarnings = new string[0]
            };

            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                output,
                new List<string> { FirstPath });

            Assert.That(rows.EntriesFor(FirstPath), Is.Empty);
            Assert.That(rows.SkippedFor(FirstPath), Is.Empty);
            Assert.That(rows.UnchangedFor(FirstPath), Is.Empty);
        }

        private static TransformWorkerOutputDto CreateOutput()
        {
            return new TransformWorkerOutputDto
            {
                shimSource = "// shim",
                entries = new[]
                {
                    CreateEntry(FirstPath, "FirstA"),
                    CreateEntry(SecondPath, "SecondA"),
                    CreateEntry(FirstPath, "FirstB")
                },
                skipped = new[]
                {
                    new TransformWorkerSkippedDto
                    {
                        sourceProjectRelativePath = FirstPath,
                        method = "First.Skipped",
                        reason = "reason"
                    }
                },
                unchangedMethods = new[]
                {
                    new TransformWorkerUnchangedMethodDto
                    {
                        sourceProjectRelativePath = SecondPath,
                        typeMetadataName = "SecondType",
                        methodName = "SecondUnchanged",
                        parameterTypeFullNames = new string[0]
                    }
                },
                files = new[]
                {
                    CreateFileOutput(FirstPath, "aaaa"),
                    CreateFileOutput(SecondPath, "bbbb")
                },
                parseErrors = new string[0],
                siblingConstDriftWarnings = new string[0]
            };
        }

        private static TransformWorkerEntryDto CreateEntry(string projectRelativePath, string methodName)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = projectRelativePath,
                typeMetadataName = "SomeType",
                methodName = methodName,
                parameterTypeFullNames = new string[0]
            };
        }

        private static TransformWorkerFileOutputDto CreateFileOutput(
            string projectRelativePath,
            string sourceContentSha256)
        {
            return new TransformWorkerFileOutputDto
            {
                projectRelativePath = projectRelativePath,
                sourceContentSha256 = sourceContentSha256,
                parseErrors = new string[0],
                declarationDriftWarnings = new string[0],
                removedMembers = new TransformWorkerRemovedMemberDto[0],
                removedMethodSignatures = new TransformWorkerRemovedMethodSignatureDto[0],
                addedFieldNames = new string[0],
                addedConstNames = new string[0]
            };
        }
    }
}
