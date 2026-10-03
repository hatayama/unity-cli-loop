using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies asmdef GUID references are read from Unity .meta files next to asmdefs.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAsmdefMetaGuidReaderTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        /// <summary>
        /// Verifies that the guid line of the asmdef .meta file becomes a GUID-prefixed assembly reference.
        /// </summary>
        [Test]
        public void ReadAsmdefGuidReferenceFromAsmdefPath_WhenMetaHasGuid_ReturnsGuidReference()
        {
            string asmdefPath = WriteAsmdefWithMeta("fileFormatVersion: 2\n  guid: 0123456789abcdef0123456789abcdef  \nAssemblyDefinitionImporter:\n");

            string reference = ThirdPartyToolMigrationAsmdefMetaGuidReader.ReadAsmdefGuidReferenceFromAsmdefPath(asmdefPath);

            Assert.That(reference, Is.EqualTo("GUID:0123456789abcdef0123456789abcdef"));
        }

        /// <summary>
        /// Verifies that a guid line without a value yields no assembly reference.
        /// </summary>
        [Test]
        public void ReadAsmdefGuidReferenceFromAsmdefPath_WhenGuidValueIsEmpty_ReturnsEmpty()
        {
            string asmdefPath = WriteAsmdefWithMeta("fileFormatVersion: 2\nguid:   \n");

            string reference = ThirdPartyToolMigrationAsmdefMetaGuidReader.ReadAsmdefGuidReferenceFromAsmdefPath(asmdefPath);

            Assert.That(reference, Is.Empty);
        }

        /// <summary>
        /// Verifies that a .meta file without any guid line yields no assembly reference.
        /// </summary>
        [Test]
        public void ReadAsmdefGuidReferenceFromAsmdefPath_WhenMetaHasNoGuidLine_ReturnsEmpty()
        {
            string asmdefPath = WriteAsmdefWithMeta("fileFormatVersion: 2\nAssemblyDefinitionImporter:\n");

            string reference = ThirdPartyToolMigrationAsmdefMetaGuidReader.ReadAsmdefGuidReferenceFromAsmdefPath(asmdefPath);

            Assert.That(reference, Is.Empty);
        }

        private string WriteAsmdefWithMeta(string metaContent)
        {
            string asmdefPath = Path.Combine(_tempDirectory, "VendorTools.Editor.asmdef");
            File.WriteAllText(asmdefPath, "{ \"name\": \"VendorTools.Editor\" }");
            File.WriteAllText(asmdefPath + ".meta", metaContent);
            return asmdefPath;
        }
    }
}
