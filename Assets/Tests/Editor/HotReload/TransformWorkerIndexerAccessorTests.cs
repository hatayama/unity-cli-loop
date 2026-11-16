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
    /// Worker coverage for edited bodies of compiled indexer accessors: each edited accessor becomes
    /// an entry named and typed like the compiled accessor, an unchanged accessor of the same indexer
    /// gets no row, and the shapes refused for ordinary methods stay refused.
    /// </summary>
    public class TransformWorkerIndexerAccessorTests
    {
        private const string FixtureFileName = "HotReloadIndexerAccessorFixture.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string Int32 = "System.Int32";
        private const string BaseMemberReason = "Uses base. for something other than a direct call to a base method";
        private const string ExplicitInterfaceReason = "Explicit interface implementations are skipped.";

        /// <summary>
        /// What: an edited getter of a one-parameter indexer is an entry for get_Item(Int32) whose
        /// shim takes the instance and the index, and the unchanged setter gets no row.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerGetter_IsAnEntryAndTheUnchangedSetterHasNoRow()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("Stored + i + 1100", "Stored + i + 1110");

            AssertSingleEntry(result, "HotReloadIndexerSingle", "get_Item", Int32);
            Assert.That(result.Output.shimSource, Does.Match(@"static int get_Item\w*\(\s*[\w.:]*HotReloadIndexerSingle __uloopInstance,\s*int i\)"));
            AssertNoRow(result, "HotReloadIndexerSingle", "set_Item");
        }

        /// <summary>
        /// What: an edited setter of a one-parameter indexer is an entry for set_Item(Int32, Int32)
        /// whose shim takes the instance, the index, and the value, and the unchanged getter gets no
        /// row.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerSetter_IsAnEntryAndTheUnchangedGetterHasNoRow()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("value + i + 1150", "value + i + 1160");

            AssertSingleEntry(result, "HotReloadIndexerSingle", "set_Item", Int32, Int32);
            Assert.That(result.Output.shimSource, Does.Match(@"static void set_Item\w*\(\s*[\w.:]*HotReloadIndexerSingle __uloopInstance,\s*int i,\s*int value\)"));
            AssertNoRow(result, "HotReloadIndexerSingle", "get_Item");
        }

        /// <summary>
        /// What: editing the string overload's getter makes an entry for get_Item(String) only; the
        /// int overload and the string setter get no row.
        /// </summary>
        [Test]
        public async Task Run_EditedOverloadGetter_IsAnEntryForThatOverloadOnly()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("s.Length + 1250", "s.Length + 1260");

            AssertSingleEntry(result, "HotReloadIndexerOverloads", "get_Item", "System.String");
            AssertNoSkippedRow(result, "HotReloadIndexerOverloads");
        }

        /// <summary>
        /// What: an indexer renamed with [IndexerName] is an entry under that name (get_Cell).
        /// </summary>
        [Test]
        public async Task Run_EditedNamedIndexerGetter_IsAnEntryUnderTheIndexerName()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("i + 1300", "i + 1310");

            AssertSingleEntry(result, "HotReloadIndexerNamed", "get_Cell", Int32);
        }

        /// <summary>
        /// What: a two-parameter indexer's edited getter is an entry with both index types in order.
        /// </summary>
        [Test]
        public async Task Run_EditedTwoParameterIndexerGetter_IsAnEntryWithBothIndexTypes()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("x + y + 1400", "x + y + 1410");

            AssertSingleEntry(result, "HotReloadIndexerTwoParameters", "get_Item", Int32, Int32);
        }

        /// <summary>
        /// What: an indexer whose index parameter types changed since the last compile has no
        /// matching compiled accessor, so both of its accessors stay Skipped with the accessor reason
        /// and neither is an entry.
        /// </summary>
        [Test]
        public async Task Run_IndexerWithChangedIndexTypes_IsSkippedWithTheAccessorReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("this[int x, int y]", "this[short x, short y]");

            const string NoMatchingAccessor = "with no matching accessor on the compiled type";
            AssertSkipped(result, "HotReloadIndexerTwoParameters.get_Item(", NoMatchingAccessor);
            AssertSkipped(result, "HotReloadIndexerTwoParameters.set_Item(", NoMatchingAccessor);
            Assert.That(FindEntries(result, "HotReloadIndexerTwoParameters", null).Count, Is.EqualTo(0), Describe(result));
        }

        /// <summary>
        /// What: an expression-bodied get-only indexer's edited body is an entry for its getter.
        /// </summary>
        [Test]
        public async Task Run_EditedExpressionBodiedIndexer_IsAnEntryForItsGetter()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("i + 1500", "i + 1510");

            AssertSingleEntry(result, "HotReloadIndexerExpressionBodied", "get_Item", Int32);
            Assert.That(result.Output.shimSource, Does.Contain("#line "), "The arrow body must map back to the edited source.");
        }

        /// <summary>
        /// What: an indexer getter that reads base[i] stays Skipped with the base-member reason, the
        /// same as an ordinary method using a base indexer, and is not an entry.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerGetterReadingBaseIndexer_IsSkippedWithTheBaseMemberReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("base[i] + 1600", "base[i] + 1610");

            AssertSkipped(result, "HotReloadIndexerBaseCaller.get_Item(", BaseMemberReason);
            Assert.That(FindEntries(result, "HotReloadIndexerBaseCaller", "get_Item").Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: an explicit interface indexer's edited getter stays Skipped with the explicit
        /// interface reason and is not an entry.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitInterfaceIndexerGetter_IsSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("i + 1700", "i + 1710");

            AssertSkipped(result, "HotReloadIndexerExplicitInterface.", ExplicitInterfaceReason);
            Assert.That(FindEntries(result, "HotReloadIndexerExplicitInterface", null).Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a patched indexer body edit raises no "edits outside method bodies" warning, because
        /// the edit is applied.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerBody_RaisesNoDeclarationDriftWarning()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("Stored + i + 1100", "Stored + i + 1110");

            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (string warning in file.declarationDriftWarnings ?? Array.Empty<string>())
                {
                    Assert.That(
                        warning,
                        Does.Not.Contain("Edits outside method bodies"),
                        "A patched indexer body must not be reported as outside-body drift.");
                }
            }
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string anchor, string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(
                onDisk.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(onDisk.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            string edited = onDisk.Replace(anchor, replacement, StringComparison.Ordinal);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("IndexerAccessorWorker.cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static List<TransformWorkerEntryDto> FindEntries(
            TransformWorkerClientResult result,
            string typeName,
            string methodNameOrNull)
        {
            List<TransformWorkerEntryDto> found = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName.EndsWith("." + typeName, StringComparison.Ordinal)
                    && (methodNameOrNull == null || entry.methodName == methodNameOrNull))
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        // Exactly one entry for the type: the named accessor with exactly these parameter types.
        private static void AssertSingleEntry(
            TransformWorkerClientResult result,
            string typeName,
            string methodName,
            params string[] parameterTypeFullNames)
        {
            List<TransformWorkerEntryDto> entries = FindEntries(result, typeName, null);
            Assert.That(entries.Count, Is.EqualTo(1), "Expected one entry for " + typeName + ". " + Describe(result));
            Assert.That(entries[0].methodName, Is.EqualTo(methodName));
            Assert.That(entries[0].parameterTypeFullNames, Is.EqualTo(parameterTypeFullNames));
            AssertNoSkippedRow(result, typeName);
        }

        private static void AssertNoRow(TransformWorkerClientResult result, string typeName, string methodName)
        {
            Assert.That(FindEntries(result, typeName, methodName).Count, Is.EqualTo(0), Describe(result));
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(
                    skipped.method ?? string.Empty,
                    Does.Not.Contain(typeName + "." + methodName + "("),
                    Describe(result));
            }
        }

        private static void AssertNoSkippedRow(TransformWorkerClientResult result, string typeName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(skipped.method ?? string.Empty, Does.Not.Contain(typeName + "."), Describe(result));
            }
        }

        private static void AssertSkipped(TransformWorkerClientResult result, string methodFragment, string reasonFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(methodFragment)
                    && HotReloadWorkerReasonText.Render(skipped.reason).Contains(reasonFragment))
                {
                    return;
                }
            }

            Assert.Fail("Expected Skipped " + methodFragment + " with '" + reasonFragment + "'. " + Describe(result));
        }

        private static string Describe(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                lines.Add("entry " + entry.typeMetadataName + "." + entry.methodName + "(" + string.Join(",", entry.parameterTypeFullNames ?? Array.Empty<string>()) + ")");
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add("skipped " + skipped.method + " => " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return string.Join(" | ", lines);
        }
    }
}
