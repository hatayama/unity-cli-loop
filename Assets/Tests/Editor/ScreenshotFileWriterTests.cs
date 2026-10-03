using System;
using System.IO;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the screenshot file writer creates explicit output directories, replaces characters that are
    /// not allowed in file names, and writes PNG files. Every file goes under a per-test temp directory.
    /// </summary>
    public sealed class ScreenshotFileWriterTests
    {
        private string _tempRoot;
        private Texture2D _texture;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "ScreenshotFileWriterTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
            }

            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Verifies an explicit output directory is created, including missing parents, and returned as a full path.
        /// </summary>
        [Test]
        public void EnsureOutputDirectoryExists_WithAMissingNestedDirectory_CreatesAndReturnsIt()
        {
            string requested = Path.Combine(_tempRoot, "nested", "shots");

            string resolved = ScreenshotFileWriter.EnsureOutputDirectoryExists(requested);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(requested)));
            Assert.That(Directory.Exists(requested), Is.True);
        }

        /// <summary>
        /// Verifies reserved file name characters and control characters become underscores and other text is kept.
        /// </summary>
        [Test]
        public void SanitizeFileName_ReplacesReservedAndControlCharacters()
        {
            string sanitized = ScreenshotFileWriter.SanitizeFileName("a<b>:c\"d/e\\f|g?h*i\tj k");

            Assert.That(sanitized, Is.EqualTo("a_b__c_d_e_f_g_h_i_j k"));
        }

        /// <summary>
        /// Verifies a texture is written as a PNG file.
        /// </summary>
        [Test]
        public void SaveTextureAsPng_WritesAPngFile()
        {
            Directory.CreateDirectory(_tempRoot);
            string path = Path.Combine(_tempRoot, "shot.png");
            _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            _texture.Apply();

            ScreenshotFileWriter.SaveTextureAsPng(_texture, path);

            byte[] written = File.ReadAllBytes(path);
            Assert.That(written.Length, Is.GreaterThan(8));
            Assert.That(new[] { written[0], written[1], written[2], written[3] }, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        }
    }
}
