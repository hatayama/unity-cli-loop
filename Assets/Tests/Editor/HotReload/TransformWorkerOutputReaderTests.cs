using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker output files that exist but cannot be read.
    /// </summary>
    public class TransformWorkerOutputReaderTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-output-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: an output path that is a directory rather than a file is reported as a read failure
        /// instead of throwing, whichever exception type the platform raises for it.
        /// </summary>
        [Test]
        public void TryRead_PathIsDirectory_ReturnsNullWithReadFailureReason()
        {
            string directoryAsOutputPath = Path.Combine(_tempDirectory, "output.json");
            Directory.CreateDirectory(directoryAsOutputPath);

            TransformWorkerOutputDto output = TransformWorkerOutputReader.TryRead(directoryAsOutputPath, 1, out string error);

            Assert.That(output, Is.Null);
            Assert.That(error, Does.Contain("could not be read"));
        }
    }
}
