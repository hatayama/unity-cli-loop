using System;
using System.Globalization;
using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the per-assembly referenced-method index: which methods of other assemblies a
    /// dll's MemberRef table names, and when the dll is read again. A test that changes a dll works
    /// on a copy in a private temp directory so that ScriptAssemblies is never touched.
    /// </summary>
    public class HotReloadReferencedMethodIndexTests
    {
        private const string TestScriptProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadReferencedMethodIndexTests.cs";

        private const string CrossAssemblyCallerAssemblyName =
            "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerFixture";

        private const string CrossAssemblyTargetTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerCrossAssemblyTarget";

        private const string CrossAssemblyGenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossAssemblyGenericHost`1";

        private string _temporaryDirectory;
        private string _persistenceDirectory;

        [SetUp]
        public void SetUp()
        {
            // Why a directory per test: a set persisted by an earlier test or domain would let a new
            // index answer without reading the dll and break every LoadCount expectation.
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-referenced-method-index-" + Guid.NewGuid().ToString("N"));
            _persistenceDirectory = Path.Combine(_temporaryDirectory, "persisted");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: a dll that calls a method of another assembly names that method.
        /// </summary>
        [Test]
        public void MentionsAny_MethodOfAnotherAssemblyTheDllCalls_IsTrue()
        {
            HotReloadReferencedMethodIndex index = NewIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") });

            Assert.That(mentioned, Is.True);
        }

        /// <summary>
        /// What: a method no other assembly calls is not named by the referencing dll.
        /// </summary>
        [Test]
        public void MentionsAny_MethodNobodyCalls_IsFalse()
        {
            HotReloadReferencedMethodIndex index = NewIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), FixtureTypeMetadataName, "NeverCalled") });

            Assert.That(mentioned, Is.False);
        }

        /// <summary>
        /// What: a call through a constructed generic type is filed under the open type's name.
        /// </summary>
        [Test]
        public void MentionsAny_CallThroughConstructedGenericType_IsFiledUnderTheOpenType()
        {
            HotReloadReferencedMethodIndex index = NewIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyGenericHostTypeMetadataName, "Target") });

            Assert.That(mentioned, Is.True);
        }

        /// <summary>
        /// What: a method of a nested type is filed under the metadata name with '/', not the
        /// reflection or source spelling.
        /// </summary>
        [Test]
        public void MentionsAny_NestedTypeKey_UsesTheMetadataSeparator()
        {
            HotReloadReferencedMethodIndex index = NewIndex();
            const string methodName = "CalledFromOtherAssembly";

            bool metadataSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + "/Nested", methodName) });
            bool reflectionSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + "+Nested", methodName) });
            bool sourceSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + ".Nested", methodName) });

            Assert.That(metadataSpelling, Is.True);
            Assert.That(reflectionSpelling, Is.False);
            Assert.That(sourceSpelling, Is.False);
        }

        /// <summary>
        /// What: a method of the dll's own module that the dll calls is not named, because that call
        /// is a MethodDef operand with no MemberRef row.
        /// </summary>
        [Test]
        public void MentionsAny_OwnMethodDefCallee_IsFalse()
        {
            HotReloadReferencedMethodIndex index = NewIndex();

            // The cross assembly defines its own type with the fixture's full name, and its
            // CallSameFullNameTarget calls this method.
            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(CrossAssemblyCallerAssemblyName, FixtureTypeMetadataName, "CalledFromCrossAssembly") });

            Assert.That(mentioned, Is.False);
        }

        /// <summary>
        /// What: a second question about an unchanged dll is answered without reading it again.
        /// </summary>
        [Test]
        public void MentionsAny_SameDllTwice_ReadsOnce()
        {
            HotReloadReferencedMethodIndex index = NewIndex();
            string[] keys = { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") };

            index.MentionsAny(CrossDll(), keys);
            index.MentionsAny(CrossDll(), keys);

            Assert.That(index.LoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a dll whose write time changed after it was read is read again.
        /// </summary>
        [Test]
        public void MentionsAny_DllWriteTimeChanged_ReadsAgain()
        {
            HotReloadReferencedMethodIndex index = NewIndex();
            string copy = CopyCrossDllToTemp();
            string[] keys = { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") };

            bool first = index.MentionsAny(copy, keys);
            File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(1));
            bool second = index.MentionsAny(copy, keys);

            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(index.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a new index on the same directory answers from the set the first index persisted,
        /// without reading the dll.
        /// </summary>
        [Test]
        public void MentionsAny_NewIndexOnTheSameDirectory_AnswersFromThePersistedFileWithoutReadingTheDll()
        {
            HotReloadReferencedMethodIndex first = NewIndex();
            bool mentionedFirst = first.MentionsAny(CrossDll(), CalledKeys());
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentionedSecond = second.MentionsAny(CrossDll(), CalledKeys());

            Assert.That(mentionedFirst, Is.True);
            Assert.That(first.LoadCount, Is.EqualTo(1));
            Assert.That(mentionedSecond, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(0));
            Assert.That(second.PersistedLoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a dll written after its set was persisted makes a new index read the dll again and
        /// persist the set under the new stamp.
        /// </summary>
        [Test]
        public void MentionsAny_DllWriteTimeChangedAfterTheFileWasPersisted_ReadsTheDllAgainAndPersistsTheNewFile()
        {
            string copy = CopyCrossDllToTemp();
            bool mentionedFirst = NewIndex().MentionsAny(copy, CalledKeys());
            DateTime newWriteTime = File.GetLastWriteTimeUtc(copy).AddMinutes(1);
            File.SetLastWriteTimeUtc(copy, newWriteTime);
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentionedSecond = second.MentionsAny(copy, CalledKeys());

            string[] stampFields = File.ReadAllText(PersistedPath(copy)).Split('\n')[1].Split('\t');
            Assert.That(mentionedFirst, Is.True);
            Assert.That(mentionedSecond, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(1));
            Assert.That(
                stampFields[1],
                Is.EqualTo(File.GetLastWriteTimeUtc(copy).Ticks.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// What: an empty persisted file is treated as missing: the dll is read and the set is
        /// written again.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileEmpty_ReadsTheDll()
        {
            NewIndex().MentionsAny(CrossDll(), CalledKeys());
            File.WriteAllBytes(PersistedPath(CrossDll()), Array.Empty<byte>());
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentioned = second.MentionsAny(CrossDll(), CalledKeys());

            Assert.That(mentioned, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(1));
            Assert.That(new FileInfo(PersistedPath(CrossDll())).Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// What: a persisted file that lost its last key line, while still claiming the original
        /// count, is treated as missing.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileMissingItsLastLine_ReadsTheDll()
        {
            AssertRewrittenFileIsNotUsed(persisted =>
            {
                string withoutTrailingNewline = persisted.Substring(0, persisted.Length - 1);
                return withoutTrailingNewline.Substring(0, withoutTrailingNewline.LastIndexOf('\n') + 1);
            });
        }

        /// <summary>
        /// What: a persisted file with one key line more than its count, still ending with a
        /// newline, is treated as missing.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileWithAnExtraLine_ReadsTheDll()
        {
            AssertRewrittenFileIsNotUsed(persisted =>
            {
                string withoutTrailingNewline = persisted.Substring(0, persisted.Length - 1);
                string lastLine = withoutTrailingNewline.Substring(withoutTrailingNewline.LastIndexOf('\n') + 1);
                return persisted + lastLine + "\n";
            });
        }

        /// <summary>
        /// What: a persisted file whose stamp names another MVID is not used, because it was written
        /// for another build of the dll.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileWithAnotherStamp_ReadsTheDll()
        {
            AssertRewrittenFileIsNotUsed(persisted =>
            {
                string[] lines = persisted.Split('\n');
                string[] stampFields = lines[1].Split('\t');
                stampFields[2] = Guid.NewGuid().ToString("N");
                lines[1] = string.Join("\t", stampFields);
                return string.Join("\n", lines);
            });
        }

        /// <summary>
        /// What: a persisted file with text after its last newline is treated as missing, although
        /// splitting it still yields the expected number of parts.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileWithTextAfterTheLastNewline_ReadsTheDll()
        {
            AssertRewrittenFileIsNotUsed(persisted => persisted + "x");
        }

        /// <summary>
        /// What: a persisted file whose line endings became CRLF (a Windows checkout tool or editor)
        /// is still read instead of the dll.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileWithCarriageReturns_StillAnswersFromIt()
        {
            NewIndex().MentionsAny(CrossDll(), CalledKeys());
            string persisted = File.ReadAllText(PersistedPath(CrossDll()));
            File.WriteAllText(PersistedPath(CrossDll()), persisted.Replace("\n", "\r\n"));
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentioned = second.MentionsAny(CrossDll(), CalledKeys());

            Assert.That(mentioned, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(0));
            Assert.That(second.PersistedLoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a persisted file with three known keys answers for both its first and its last key,
        /// so a key loop that drops the first or the last line fails here instead of returning a
        /// set that is one key short.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedFileWithThreeKeys_AnswersForTheFirstAndTheLastKey()
        {
            string firstKey = Key(TestAssemblyName(), CrossAssemblyGenericHostTypeMetadataName, "Target");
            string middleKey = CalledKeys()[0];
            string lastKey = Key(
                TestAssemblyName(),
                CrossAssemblyTargetTypeMetadataName + "/Nested",
                "CalledFromOtherAssembly");
            NewIndex().MentionsAny(CrossDll(), CalledKeys());
            string[] lines = File.ReadAllText(PersistedPath(CrossDll())).Split('\n');
            string[] stampFields = lines[1].Split('\t');
            stampFields[3] = "3";
            File.WriteAllText(
                PersistedPath(CrossDll()),
                lines[0] + "\n" + string.Join("\t", stampFields) + "\n"
                + firstKey + "\n" + middleKey + "\n" + lastKey + "\n");
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentionsFirst = second.MentionsAny(CrossDll(), new[] { firstKey });
            bool mentionsLast = second.MentionsAny(CrossDll(), new[] { lastKey });

            Assert.That(mentionsFirst, Is.True);
            Assert.That(mentionsLast, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(0));
            Assert.That(second.PersistedLoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: when the persisted set cannot be moved into place, the question throws after the
        /// dll was read and no temp file is left behind.
        /// </summary>
        [Test]
        public void MentionsAny_PersistedPathBlocked_ThrowsAndLeavesNoTempFile()
        {
            HotReloadReferencedMethodIndex index = NewIndex();
            // Why a directory: File.Exists is false for it, so the write skips the delete and the move throws.
            Directory.CreateDirectory(PersistedPath(CrossDll()));
            TestDelegate mention = () => index.MentionsAny(CrossDll(), CalledKeys());

            Assert.That(mention, Throws.Exception);
            Assert.That(Directory.GetFiles(_persistenceDirectory, "*.tmp-*"), Is.Empty);
            Assert.That(index.LoadCount, Is.EqualTo(1));
        }

        // Persists the set with one index, rewrites the file, and asserts that a new index reads the
        // dll instead of answering from the rewritten file.
        private void AssertRewrittenFileIsNotUsed(Func<string, string> rewrite)
        {
            bool mentionedFirst = NewIndex().MentionsAny(CrossDll(), CalledKeys());
            string persisted = File.ReadAllText(PersistedPath(CrossDll()));
            Assert.That(persisted, Does.EndWith("\n"));
            File.WriteAllText(PersistedPath(CrossDll()), rewrite(persisted));
            HotReloadReferencedMethodIndex second = NewIndex();

            bool mentionedSecond = second.MentionsAny(CrossDll(), CalledKeys());

            Assert.That(mentionedFirst, Is.True);
            Assert.That(mentionedSecond, Is.True);
            Assert.That(second.LoadCount, Is.EqualTo(1));
            Assert.That(second.PersistedLoadCount, Is.EqualTo(0));
        }

        private HotReloadReferencedMethodIndex NewIndex()
        {
            return new HotReloadReferencedMethodIndex(_persistenceDirectory);
        }

        private string PersistedPath(string dllPath)
        {
            return Path.Combine(_persistenceDirectory, Path.GetFileNameWithoutExtension(dllPath) + ".txt");
        }

        private static string[] CalledKeys()
        {
            return new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") };
        }

        private static string Key(string assemblyName, string openDeclaringTypeFullName, string methodName)
        {
            return HotReloadReferencedMethodIndex.BuildKey(assemblyName, openDeclaringTypeFullName, methodName);
        }

        private string CopyCrossDllToTemp()
        {
            string sourceDllPath = CrossDll();
            Assert.That(File.Exists(sourceDllPath), Is.True, "Assembly dll missing: " + sourceDllPath);

            Directory.CreateDirectory(_temporaryDirectory);
            string copy = Path.Combine(_temporaryDirectory, Path.GetFileName(sourceDllPath));
            File.Copy(sourceDllPath, copy);
            return copy;
        }

        private static string CrossDll()
        {
            return DllPath(CrossAssemblyCallerAssemblyName);
        }

        private static string DllPath(string assemblyName)
        {
            return Path.Combine(
                ProjectRoot(),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string TestAssemblyName()
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            return Path.GetFileNameWithoutExtension(rawAssemblyName);
        }
    }
}
