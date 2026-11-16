using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for edited bodies of compiled init accessors: an init accessor whose compiled
    /// peer is also init becomes a set_X entry, a set/init change since the compile keeps the
    /// accessor row, and an init body that assigns a readonly field or an init-only property is
    /// refused with its own reason.
    /// </summary>
    public class TransformWorkerInitAccessorTests
    {
        private const string FixtureFileName = "HotReloadInitAccessorFixture.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string InitOnlyMemberReason =
            "Init accessors that assign a readonly field or an init-only property are skipped";
        private const string NoMatchingAccessorReason = "with no matching accessor on the compiled type";

        /// <summary>
        /// What: an edited init accessor of a class property is an entry for set_X(Int32).
        /// </summary>
        [Test]
        public async Task Run_EditedClassInitAccessor_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 1700", "value + 1710");

            AssertEntry(result, "HotReloadInitClass", "set_X");
        }

        /// <summary>
        /// What: an edited init accessor of a record property is an entry for set_B(Int32).
        /// </summary>
        [Test]
        public async Task Run_EditedRecordInitAccessor_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 1800", "value + 1810");

            AssertEntry(result, "HotReloadInitRecord", "set_B");
        }

        /// <summary>
        /// What: a set accessor that became init since the last compile keeps the accessor row and
        /// is not an entry, because the compiled set_X is not init-only.
        /// </summary>
        [Test]
        public async Task Run_CompiledSetNowInitInSource_IsSkippedWithTheAccessorReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "set { XField = value + 1750; }",
                "init { XField = value + 1760; }");

            AssertSkippedWithoutEntry(result, "HotReloadInitClass", "set_SetNowInit", NoMatchingAccessorReason);
        }

        /// <summary>
        /// What: an init body that assigns a readonly field is refused with the init-only-member
        /// reason and is not an entry.
        /// </summary>
        [Test]
        public async Task Run_InitAssigningReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2100", "value + 2110");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_AssignReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that compound-assigns a readonly field is refused with the
        /// init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitCompoundAssigningReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2200", "value + 2210");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_CompoundReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that increments a readonly field is refused with the init-only-member
        /// reason.
        /// </summary>
        [Test]
        public async Task Run_InitIncrementingReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2300", "value + 2310");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_IncrementReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that deconstructs into a readonly field is refused with the
        /// init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitDeconstructingIntoReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("(value, 2400)", "(value, 2410)");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_DeconstructReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that passes a readonly field as an out argument is refused with the
        /// init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitPassingReadonlyFieldAsOut_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("\"2500\"", "\"2510\"");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_OutReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that assigns another init-only property is refused with the
        /// init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitAssigningInitOnlyProperty_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2600", "value + 2610");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_AssignInitOnly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body in the same type that assigns only an ordinary field is an entry, so
        /// the refusal is about the assigned member, not the type.
        /// </summary>
        [Test]
        public async Task Run_InitAssigningOrdinaryField_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2700", "value + 2710");

            AssertEntry(result, "HotReloadInitWrites", "set_AssignPlain");
        }

        /// <summary>
        /// What: an init body that sets another object's init-only property inside an object or with
        /// initializer is an entry, because an initializer may set init-only members from anywhere.
        /// </summary>
        [Test]
        public async Task Run_InitSettingInitOnlyPropertyInsideInitializers_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 2850", "value + 2860");

            AssertEntry(result, "HotReloadInitWrites", "set_BuildChild");
        }

        /// <summary>
        /// What: an edited init accessor of an indexer stays Skipped with the accessor reason and is
        /// not an entry, even though the compiled accessor is init too.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerInitAccessor_IsSkippedWithTheAccessorReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + i + 2900", "value + i + 2910");

            AssertSkippedWithoutEntry(result, "HotReloadInitIndexer", "set_Item", "and explicit interface or indexer init accessors, are skipped");
        }

        /// <summary>
        /// What: an init body that passes a readonly field as a ref argument is refused with the init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitRefArgumentOfReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3400", "value + 3401");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_RefArgumentReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that pre-increments a readonly field is refused with the init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitPreIncrementOfReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3410", "value + 3411");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_PreIncrementReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that pre-decrements a readonly field is refused with the init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitPreDecrementOfReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3420", "value + 3421");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_PreDecrementReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that post-decrements a readonly field is refused with the init-only-member reason.
        /// </summary>
        [Test]
        public async Task Run_InitPostDecrementOfReadonlyField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3430", "value + 3431");

            AssertSkippedWithoutEntry(result, "HotReloadInitWrites", "set_PostDecrementReadonly", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that assigns a member of a public readonly struct field is refused with the init-only-member reason, and an init
        /// edit of another type in the same file still becomes an entry.
        /// </summary>
        [Test]
        public async Task Run_InitAssigningMemberOfReadonlyStructField_IsRefusedAndTheRestOfTheFileApplies()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "value + 3500",
                "value + 3501",
                ("value + 1700", "value + 1710"));

            AssertSkippedWithoutEntry(result, "HotReloadInitPublicWrites", "set_MemberOfReadonlyStruct", InitOnlyMemberReason);
            AssertEntry(result, "HotReloadInitClass", "set_X");
        }

        /// <summary>
        /// What: an init body that increments a member of a public readonly struct field is refused with the init-only-member reason, and an init
        /// edit of another type in the same file still becomes an entry.
        /// </summary>
        [Test]
        public async Task Run_InitIncrementingMemberOfReadonlyStructField_IsRefusedAndTheRestOfTheFileApplies()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "value + 3510",
                "value + 3511",
                ("value + 1700", "value + 1710"));

            AssertSkippedWithoutEntry(result, "HotReloadInitPublicWrites", "set_IncrementMemberOfReadonlyStruct", InitOnlyMemberReason);
            AssertEntry(result, "HotReloadInitClass", "set_X");
        }

        /// <summary>
        /// What: an init body that takes a ref to a public readonly field is refused with the init-only-member reason, and an init
        /// edit of another type in the same file still becomes an entry.
        /// </summary>
        [Test]
        public async Task Run_InitTakingRefToReadonlyField_IsRefusedAndTheRestOfTheFileApplies()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "value + 3520",
                "value + 3521",
                ("value + 1700", "value + 1710"));

            AssertSkippedWithoutEntry(result, "HotReloadInitPublicWrites", "set_RefToReadonly", InitOnlyMemberReason);
            AssertEntry(result, "HotReloadInitClass", "set_X");
        }

        /// <summary>
        /// What: an init body that writes a member of a parenthesized readonly struct field is refused
        /// with the init-only-member reason, since parentheses do not change the write target.
        /// </summary>
        [Test]
        public async Task Run_InitAssigningMemberOfParenthesizedReadonlyStructField_IsRefused()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3540", "value + 3541");

            AssertSkippedWithoutEntry(result, "HotReloadInitPublicWrites", "set_MemberOfParenthesizedReadonlyStruct", InitOnlyMemberReason);
        }

        /// <summary>
        /// What: an init body that writes through a class reference held by a readonly struct field is
        /// an entry, because the write changes the referenced object, not the readonly field.
        /// </summary>
        [Test]
        public async Task Run_InitWritingThroughReferenceInReadonlyStructField_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3530", "value + 3531");

            AssertEntry(result, "HotReloadInitPublicWrites", "set_ThroughReferenceInReadonlyStruct");
        }

        /// <summary>
        /// What: an edited explicit interface init accessor renders the accessor reason that names
        /// explicit interface init accessors, not only the reason code.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitInterfaceInitAccessor_RendersTheExplicitInterfaceInitText()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 3600", "value + 3601");

            AssertSkippedWithoutEntry(
                result,
                "HotReloadInitExplicitInterface",
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.IHotReloadInitContract.set_Value",
                "and explicit interface or indexer init accessors, are skipped");
        }

        /// <summary>
        /// What: a patched init body edit raises no "edits outside method bodies" warning, because
        /// the edit is applied.
        /// </summary>
        [Test]
        public async Task Run_EditedInitBody_RaisesNoDeclarationDriftWarning()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + 1700", "value + 1710");

            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (string warning in file.declarationDriftWarnings ?? Array.Empty<string>())
                {
                    Assert.That(
                        warning,
                        Does.Not.Contain("Edits outside method bodies"),
                        "A patched init body must not be reported as outside-body drift.");
                }
            }
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(
            string anchor,
            string replacement,
            params (string Anchor, string Replacement)[] alsoReplace)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(
                onDisk.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(onDisk.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            string edited = onDisk.Replace(anchor, replacement, StringComparison.Ordinal);
            foreach ((string Anchor, string Replacement) extra in alsoReplace)
            {
                Assert.That(edited, Does.Contain(extra.Anchor), "Precondition: the fixture must hold " + extra.Anchor);
                edited = edited.Replace(extra.Anchor, extra.Replacement, StringComparison.Ordinal);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("InitAccessorWorker.cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        // Exactly one entry for the type: the named accessor taking the property's Int32 value; no
        // skipped row for the type.
        private static void AssertEntry(TransformWorkerClientResult result, string typeName, string methodName)
        {
            List<TransformWorkerEntryDto> entries = FindEntries(result, typeName);
            Assert.That(entries.Count, Is.EqualTo(1), "Expected one entry for " + typeName + ". " + Describe(result));
            Assert.That(entries[0].methodName, Is.EqualTo(methodName));
            Assert.That(entries[0].parameterTypeFullNames, Is.EqualTo(new[] { "System.Int32" }));
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(skipped.method ?? string.Empty, Does.Not.Contain(typeName + "."), Describe(result));
            }
        }

        private static void AssertSkippedWithoutEntry(
            TransformWorkerClientResult result,
            string typeName,
            string methodName,
            string reasonFragment)
        {
            Assert.That(FindEntries(result, typeName).Count, Is.EqualTo(0), Describe(result));
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(typeName + "." + methodName + "(")
                    && HotReloadWorkerReasonText.Render(skipped.reason).Contains(reasonFragment))
                {
                    return;
                }
            }

            Assert.Fail("Expected Skipped " + methodName + " with '" + reasonFragment + "'. " + Describe(result));
        }

        private static List<TransformWorkerEntryDto> FindEntries(TransformWorkerClientResult result, string typeName)
        {
            List<TransformWorkerEntryDto> found = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName.EndsWith("." + typeName, StringComparison.Ordinal))
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        private static string Describe(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                lines.Add("entry " + entry.typeMetadataName + "." + entry.methodName);
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add("skipped " + skipped.method + " => " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return string.Join(" | ", lines);
        }
    }
}
