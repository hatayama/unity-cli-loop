using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for matching an edited member to its compiled counterpart when the edited
    /// source renames a type parameter: a rename that keeps every position matches, as metadata
    /// does, for method parameters, return types, fields, properties, indexers, explicit
    /// implementations, overload cover and nested generic types; a swap of positions does not.
    /// </summary>
    public class TransformWorkerTypeParameterRenameTests
    {
        private const string FixtureFileName = "HotReloadTypeParameterRenameWorkerFixture.cs";
        private const string AliasFixtureFileName = "HotReloadTypeParameterRenameAliasFixture.cs";
        private const string FixtureDirectory = "Assets/Tests/Editor/HotReload/";
        private const string AddedGenericReason = "Added generic methods are skipped";
        private const string GenericEditReason = "Generic methods and methods inside generic types cannot be safely patched";
        private const string CallsAddedReason = "which this reload skipped";

        /// <summary>
        /// What: swapping the type parameters of Swap&lt;T, U&gt;(T, U) to Swap&lt;U, T&gt;(T, U)
        /// changes the signature, so the edited method matches no compiled method and is skipped
        /// as an added generic method, although every name it uses is unchanged.
        /// </summary>
        [Test]
        public async Task Run_SwappedTypeParameters_IsAnAddedMethod()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "public int Swap<T, U>(T first, U second)", "public int Swap<U, T>(T first, U second)",
                "return 1100;", "return 1110;");

            AssertSkipped(result, "HotReloadTypeParameterRenameWorkerMethods.Swap", AddedGenericReason);
        }

        /// <summary>
        /// What: renaming the type parameter of T Pick&lt;T&gt;(int), which only the return type
        /// names, matches the compiled method, so Pick gets the edited-generic row the same edit
        /// without the rename gets, not the added-generic one.
        /// </summary>
        [Test]
        public async Task Run_RenamedTypeParameterInReturnTypeOnly_MatchesTheCompiledMethod()
        {
            string[] bodyEdit = { "(index + 1200)", "(index + 1210)" };
            await AssertSameRowsAsControlAsync(
                "HotReloadTypeParameterRenameWorkerMethods.Pick",
                bodyEdit,
                RenameIn("        public T Pick<T>(int index)", "T", "U"));
        }

        /// <summary>
        /// What: when one overload's return type changes, the compiled generic overload the edited
        /// source still declares under a renamed type parameter is covered, so no removed method
        /// signature names it.
        /// </summary>
        [Test]
        public async Task Run_RenamedGenericOverloadBesideARetypedOverload_IsNotReportedRemoved()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "public int Overload(int value)\n        {\n            return 1400;",
                "public long Overload(int value)\n        {\n            return 1410L;",
                "public int Overload<T>(T value)", "public int Overload<U>(U value)");

            foreach (TransformWorkerRemovedMethodSignatureDto removed in result.Output.files[0].removedMethodSignatures)
            {
                Assert.That(removed.genericArity, Is.EqualTo(0), "Removed: " + removed.methodName + "`" + removed.genericArity + "(" + string.Join(",", removed.parameterTypeFullNames) + ")");
            }
        }

        /// <summary>
        /// What: renaming the type parameter of an explicit implementation of a generic interface
        /// method matches the compiled implementation, so it gets the row the same edit without the
        /// rename gets.
        /// </summary>
        [Test]
        public async Task Run_RenamedTypeParameterOfExplicitImplementation_MatchesTheCompiledImplementation()
        {
            string[] bodyEdit = { "return 1500;", "return 1510;" };
            await AssertSameRowsAsControlAsync(
                "HotReloadTypeParameterRenameWorkerMethods.",
                bodyEdit,
                RenameIn("        int IHotReloadTypeParameterRenameEcho.Echo<T>(T value)", "T", "U"));
        }

        /// <summary>
        /// What: renaming a generic host's type parameter matches its field of that type to the
        /// compiled field, so the host gets the rows the same edit without the rename gets.
        /// </summary>
        [Test]
        public async Task Run_RenamedHostTypeParameter_MatchesTheFieldOfThatType()
        {
            await AssertHostRenameMatchesControlAsync("HotReloadTypeParameterRenameFieldHost", "return 1700;", "return 1710;");
        }

        /// <summary>
        /// What: renaming a generic host's type parameter matches its property of that type to the
        /// compiled property, so the host gets the rows the same edit without the rename gets.
        /// </summary>
        [Test]
        public async Task Run_RenamedHostTypeParameter_MatchesThePropertyOfThatType()
        {
            await AssertHostRenameMatchesControlAsync("HotReloadTypeParameterRenamePropertyHost", "marker = 1800;", "marker = 1810;");
        }

        /// <summary>
        /// What: renaming a generic host's type parameter matches its indexer over that type to the
        /// compiled indexer: no added or removed rows and no added-generic reason; the edited getter
        /// gets the edited-generic row, and the untouched setter, whose header now names the renamed
        /// type parameter, is either absent or Skipped with that same reason.
        /// </summary>
        [Test]
        public async Task Run_RenamedHostTypeParameter_MatchesTheIndexerOverThatType()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(Concat(
                RenameIn("    public sealed class HotReloadTypeParameterRenameIndexerHost<T>", "T", "V"),
                new[] { "return 1900;", "return 1910;" }));

            AssertNoReason(result, AddedGenericReason);
            Assert.That(result.Output.files[0].removedMembers, Is.Empty, Describe(result));
            Assert.That(result.Output.files[0].removedMethodSignatures, Is.Empty, Describe(result));
            AssertSkipped(result, "HotReloadTypeParameterRenameIndexerHost`1.get_Item(", GenericEditReason);
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("HotReloadTypeParameterRenameIndexerHost`1.set_Item("))
                {
                    Assert.That(HotReloadWorkerReasonText.Render(skipped.reason), Does.Contain(GenericEditReason), Describe(result));
                }
            }

            Assert.That(FindEntryCount(result, "HotReloadTypeParameterRenameIndexerHost`1", "get_Item"), Is.EqualTo(0), Describe(result));
            Assert.That(FindEntryCount(result, "HotReloadTypeParameterRenameIndexerHost`1", "set_Item"), Is.EqualTo(0), Describe(result));
        }

        /// <summary>
        /// What: in a non-generic type, editing only the generic overload Run&lt;T&gt;(T) leaves
        /// Run(T), whose T is a nested class, without a row, and the baseline stays enabled, so the
        /// two overloads keep distinct snapshot keys.
        /// </summary>
        [Test]
        public async Task Run_EditedGenericOverloadBesideANestedClassNamedT_LeavesTheOtherOverloadUnchanged()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("return 2300;", "return 2310;");

            Assert.That(result.Output.files[0].baselineDisabledByDuplicateKeys, Is.False, Describe(result));
            AssertNoSkippedRow(result, "HotReloadTypeParameterRenameNestedNameHost.Run(");
            Assert.That(FindEntryCount(result, "HotReloadTypeParameterRenameNestedNameHost", "Run"), Is.EqualTo(0), Describe(result));
            AssertSkipped(result, "HotReloadTypeParameterRenameNestedNameHost.Run`1(", GenericEditReason);
        }

        /// <summary>
        /// What: renaming a generic host's type parameter matches its explicit implementation of a
        /// generic interface's property to the compiled one, so the host gets the rows the same
        /// edit without the rename gets.
        /// </summary>
        [Test]
        public async Task Run_RenamedHostTypeParameter_MatchesTheExplicitPropertyImplementation()
        {
            await AssertHostRenameMatchesControlAsync("HotReloadTypeParameterRenameExplicitHost", "marker = 2000;", "marker = 2010;");
        }

        /// <summary>
        /// What: renaming the outer type parameter of Outer&lt;T&gt;.Inner&lt;U&gt; keeps
        /// Read(T, U) matched to the compiled method, so an edited caller of it is an entry and is
        /// not skipped for calling an added method.
        /// </summary>
        [Test]
        public async Task Run_RenamedOuterTypeParameterOfNestedGenericType_CallerIsAnEntry()
        {
            string[] edits = Concat(
                RenameIn("    public sealed class HotReloadTypeParameterRenameOuter<T>", "T", "V"),
                new[] { "return 2100;", "return 2110;", ".Read(1, 2) + 1600;", ".Read(1, 2) + 1610;" });
            TransformWorkerClientResult result = await RunEditedFixtureAsync(edits);

            AssertNoSkippedRow(result, "HotReloadTypeParameterRenameWorkerMethods.CallNested");
            Assert.That(FindEntryCount(result, "HotReloadTypeParameterRenameWorkerMethods", "CallNested"), Is.EqualTo(1), Describe(result));
            AssertNoReason(result, AddedGenericReason);
        }

        /// <summary>
        /// What: swapping the type parameter names of Outer&lt;T&gt;.Inner&lt;U&gt; to
        /// Outer&lt;U&gt;.Inner&lt;T&gt; while Read keeps (T, U) swaps Read's parameter positions, so
        /// Read matches no compiled method and an edited caller of it is skipped for calling an
        /// added method.
        /// </summary>
        [Test]
        public async Task Run_SwappedTypeParameterNamesOfNestedGenericType_ReadIsAnAddedMethod()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "public sealed class HotReloadTypeParameterRenameOuter<T>", "public sealed class HotReloadTypeParameterRenameOuter<U>",
                "public sealed class Inner<U>", "public sealed class Inner<T>",
                "return 2100;", "return 2110;",
                ".Read(1, 2) + 1600;", ".Read(1, 2) + 1610;");

            AssertSkipped(result, "HotReloadTypeParameterRenameWorkerMethods.CallNested", CallsAddedReason);
        }

        /// <summary>
        /// What: renaming the type parameter T of Format&lt;T&gt;(T::String, T) to U leaves the
        /// using alias T on the left of '::' as it is, so the method keeps its snapshot key: no
        /// removed member is reported, and Format gets the row the same edit without the rename gets.
        /// </summary>
        [Test]
        public async Task Run_RenamedTypeParameterBesideAnAliasOfTheSameName_KeepsTheAliasInTheKey()
        {
            string[] bodyEdit = { "return 2400;", "return 2410;" };
            TransformWorkerClientResult control = await RunEditedFileAsync(AliasFixtureFileName, bodyEdit);
            TransformWorkerClientResult renamed = await RunEditedFileAsync(
                AliasFixtureFileName,
                Concat(new[] { "public int Format<T>(T::String text, T value)", "public int Format<U>(T::String text, U value)" }, bodyEdit));

            Assert.That(renamed.Output.files[0].removedMembers, Is.Empty, Describe(renamed));
            Assert.That(
                Summarize(renamed, "HotReloadTypeParameterRenameAliasFixture."),
                Is.EqualTo(Summarize(control, "HotReloadTypeParameterRenameAliasFixture.")),
                "Renamed: " + Describe(renamed) + "\nControl: " + Describe(control));
            AssertNoReason(renamed, AddedGenericReason);
        }

        private static async Task AssertHostRenameMatchesControlAsync(string hostName, string bodyAnchor, string editedBody)
        {
            await AssertSameRowsAsControlAsync(
                hostName,
                new[] { bodyAnchor, editedBody },
                RenameIn("    public sealed class " + hostName + "<T>", "T", "V"));
        }

        // Why compare with a control run: a rename must change nothing but the spelling of the rows'
        // labels, so the reasons, entries and removed members of the type must equal those of the
        // same body edit without the rename, whatever they are for that member kind.
        private static async Task AssertSameRowsAsControlAsync(string typeFragment, string[] bodyEdit, string[] renameEdits)
        {
            TransformWorkerClientResult control = await RunEditedFixtureAsync(bodyEdit);
            TransformWorkerClientResult renamed = await RunEditedFixtureAsync(Concat(renameEdits, bodyEdit));

            Assert.That(Summarize(renamed, typeFragment), Is.EqualTo(Summarize(control, typeFragment)), "Renamed: " + Describe(renamed) + "\nControl: " + Describe(control));
            AssertNoReason(renamed, AddedGenericReason);
            Assert.That(Summarize(control, typeFragment), Is.Not.Empty, "Precondition: the control edit must produce a row. " + Describe(control));
        }

        // Every replacement pair for renaming one identifier, as a whole word, within the block that
        // starts at the given header and ends at the first line closing a top-level type after it.
        private static string[] RenameIn(string header, string from, string to)
        {
            string onDisk = ReadFixture();
            int start = onDisk.IndexOf(header, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Precondition: the fixture must hold " + header);
            int end = onDisk.IndexOf("\n    }\n", start, StringComparison.Ordinal);
            int bodyEnd = header.StartsWith("    public sealed class", StringComparison.Ordinal) ? end : onDisk.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            string block = onDisk.Substring(start, bodyEnd - start);
            string renamed = Regex.Replace(block, @"\b" + from + @"\b", to);
            return new[] { block, renamed };
        }

        private static string[] Concat(string[] first, string[] second)
        {
            string[] all = new string[first.Length + second.Length];
            first.CopyTo(all, 0);
            second.CopyTo(all, first.Length);
            return all;
        }

        private static string ReadFixture()
        {
            return ReadFixtureFile(FixtureFileName);
        }

        private static string ReadFixtureFile(string fileName)
        {
            return File.ReadAllText(Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName))).Replace("\r\n", "\n");
        }

        private static Task<TransformWorkerClientResult> RunEditedFixtureAsync(params string[] replacements)
        {
            return RunEditedFileAsync(FixtureFileName, replacements);
        }

        // Why pairs of strings: the replacements run in order, and each must find its text exactly
        // once, so a typo fails the precondition instead of editing nothing.
        private static async Task<TransformWorkerClientResult> RunEditedFileAsync(string fileName, string[] replacements)
        {
            string onDisk = ReadFixtureFile(fileName);
            string edited = onDisk;
            for (int index = 0; index < replacements.Length; index += 2)
            {
                string anchor = replacements[index];
                Assert.That(
                    edited.IndexOf(anchor, StringComparison.Ordinal),
                    Is.EqualTo(edited.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                    "Precondition: the source must hold the anchor exactly once: " + anchor);
                edited = edited.Replace(anchor, replacements[index + 1]);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("TypeParameterRenameWorker.cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureDirectory + fileName,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        // The rows of members whose label contains the fragment, without the labels themselves,
        // which spell the type parameters as the source names them.
        private static List<string> Summarize(TransformWorkerClientResult result, string typeFragment)
        {
            List<string> rows = new List<string>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(typeFragment))
                {
                    rows.Add("skipped: " + HotReloadWorkerReasonText.Render(skipped.reason));
                }
            }

            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if ((entry.typeMetadataName + "." + entry.methodName).Contains(typeFragment.TrimEnd('.')))
                {
                    rows.Add("entry: " + entry.methodName);
                }
            }

            foreach (TransformWorkerRemovedMemberDto removed in result.Output.files[0].removedMembers)
            {
                rows.Add("removed member: " + removed.name);
            }

            foreach (TransformWorkerRemovedMethodSignatureDto removed in result.Output.files[0].removedMethodSignatures)
            {
                rows.Add("removed signature: " + removed.methodName + "`" + removed.genericArity);
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        private static int FindEntryCount(TransformWorkerClientResult result, string typeName, string methodName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName.EndsWith("." + typeName, StringComparison.Ordinal) && entry.methodName == methodName)
                {
                    count++;
                }
            }

            return count;
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

        private static void AssertNoSkippedRow(TransformWorkerClientResult result, string methodFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(skipped.method ?? string.Empty, Does.Not.Contain(methodFragment), Describe(result));
            }
        }

        private static void AssertNoReason(TransformWorkerClientResult result, string reasonFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(HotReloadWorkerReasonText.Render(skipped.reason), Does.Not.Contain(reasonFragment), Describe(result));
            }
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

            foreach (TransformWorkerRemovedMethodSignatureDto removed in result.Output.files[0].removedMethodSignatures)
            {
                lines.Add("removed signature " + removed.methodName + "`" + removed.genericArity + "(" + string.Join(",", removed.parameterTypeFullNames) + ")");
            }

            return string.Join(" | ", lines);
        }
    }
}
