using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that a declaration a retained artifact already serves is taken out of the tree the
    /// transform binds against, only when the artifact record still describes the edited source,
    /// and that removing it does not move the source lines the shim reports.
    /// </summary>
    public sealed class TransformWorkerRetainedDeclarationTests
    {
        private const string EditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // The region opens above the retained declaration and closes inside it, so blanking the
        // declaration takes the closing directive with it and leaves the text unparseable.
        private const string UnbalancedRegionSource =
            "namespace Example\n"
            + "{\n"
            + "#if UNITY_EDITOR\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "#endif\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: the shim reports the line the edited file really has for a method that follows a
        /// removed declaration, so taking the declaration out does not shift the mapping.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_KeepsSourceLineNumbers()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("KeepsSourceLineNumbers", EditedSource);

            TransformWorkerClientResult withoutRecord = await RunAsync(
                fixture,
                Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(FindReadEntry(withRecord).sourceStartLine, Is.EqualTo(FindReadEntry(withoutRecord).sourceStartLine));
            Assert.That(withRecord.Output.shimSource, Does.Contain("Retained.Value + 1"));
        }

        /// <summary>
        /// What: the retained type stops being transformed as a source declaration once its record
        /// matches, so the run no longer reports rows for its members.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_ReportsNoRowsForRetainedType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("ReportsNoRows", EditedSource);

            TransformWorkerClientResult withoutRecord = await RunAsync(
                fixture,
                Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(CountRowsMentioning(withoutRecord, "Twice"), Is.GreaterThan(0));
            Assert.That(CountRowsMentioning(withRecord, "Twice"), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a record whose fingerprint no longer matches the edited source leaves the
        /// declaration in place, because the source is then newer than the artifact.
        /// </summary>
        [Test]
        public async Task Transform_RecordFingerprintDoesNotMatch_KeepsDeclarationInBinding()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("FingerprintMismatch", EditedSource);

            TransformWorkerClientResult tampered = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(new string('0', 64)) });

            Assert.That(tampered.Success, Is.True, tampered.ErrorMessage);
            Assert.That(CountRowsMentioning(tampered, "Twice"), Is.GreaterThan(0));
        }

        /// <summary>
        /// What: an artifact record the worker cannot trust fails the whole run instead of
        /// producing a shim, because the caller advances to revert and compile on success.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactIdentityMismatch_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("IdentityMismatch", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            mismatched.assemblyFullName =
                HotReloadRetainedArtifactFixture.ReadAssemblyFullName(fixture.TargetAssemblyPath);

            TransformWorkerClientResult result = await RunAsync(fixture, new[] { mismatched });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: a preprocessor region that closes inside the retained declaration makes the
        /// blanked text unparseable, and the run fails instead of transforming the file against a
        /// tree the parser had to guess at.
        /// </summary>
        [Test]
        public async Task Transform_BlankedDeclarationLeavesUnbalancedRegion_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("UnbalancedRegion", UnbalancedRegionSource);

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        private static async Task<TransformWorkerClientResult> RunAsync(
            HotReloadRetainedArtifactFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts)
        {
            return await TransformWorkerClient.RunAsync(
                fixture.BuildTransformInput(artifacts),
                CancellationToken.None);
        }

        private static TransformWorkerEntryDto FindReadEntry(TransformWorkerClientResult result)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "Read")
                {
                    return entry;
                }
            }

            Assert.Fail("No shim entry was emitted for the caller method.");
            return null;
        }

        private static int CountRowsMentioning(TransformWorkerClientResult result, string memberName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == memberName)
                {
                    count++;
                }
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(memberName))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
