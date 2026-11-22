using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the tools cache write that runs off the main thread after a domain reload: what it writes,
    /// and when the gate that holds commands of the new domain opens.
    /// </summary>
    [TestFixture]
    public sealed class ToolCatalogCachePublisherTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(ToolCatalogCachePublisherTests),
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        /// <summary>
        /// What: the write leaves out the tools the internal skill scan reports, and creates the cache
        /// directory when it is missing.
        /// </summary>
        [Test]
        public async Task StartWrite_WritesTheCatalogWithoutTheToolsTheScanReportsInternal()
        {
            ToolCatalogCachePublisher publisher = new ToolCatalogCachePublisher(RunInline);
            string cachePath = Path.Combine(_temporaryRoot, ".uloop", "tools.json");

            publisher.StartWrite(
                CreateTools("public-tool", "internal-tool"),
                () => new HashSet<string>(StringComparer.Ordinal) { "internal-tool" },
                cachePath);
            await publisher.WhenPublished;

            string json = File.ReadAllText(cachePath);
            Assert.That(json, Does.Contain("\"public-tool\""));
            Assert.That(json, Does.Not.Contain("\"internal-tool\""));
        }

        /// <summary>
        /// What: the gate is open before anything is published, stays closed while the background write
        /// runs, and opens once it ends, so commands wait for the cache only while it is being written.
        /// </summary>
        [Test]
        public async Task WhenPublished_IsOpenBeforeAWrite_ClosedWhileItRuns_AndOpenOnceItEnds()
        {
            TaskCompletionSource<bool> backgroundWrite = new TaskCompletionSource<bool>();
            Action pendingWrite = null;
            ToolCatalogCachePublisher publisher = new ToolCatalogCachePublisher(write =>
            {
                pendingWrite = write;
                return backgroundWrite.Task;
            });
            string cachePath = Path.Combine(_temporaryRoot, ".uloop", "tools.json");
            bool openBeforeWrite = publisher.WhenPublished.IsCompleted;

            publisher.StartWrite(CreateTools("public-tool"), () => new HashSet<string>(), cachePath);
            try
            {
                Assert.That(openBeforeWrite, Is.True);
                Assert.That(publisher.WhenPublished.IsCompleted, Is.False);
                Assert.That(File.Exists(cachePath), Is.False);
            }
            finally
            {
                // Why in finally: a failed assertion must not leave the background task pending.
                pendingWrite();
                backgroundWrite.SetResult(true);
            }

            await publisher.WhenPublished;

            Assert.That(publisher.WhenPublished.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(File.Exists(cachePath), Is.True);
        }

        /// <summary>
        /// What: a write that fails is logged and still opens the gate without faulting it, so a broken
        /// scan never holds or fails the commands of the domain.
        /// </summary>
        [Test]
        public async Task WhenPublished_WhenTheWriteFails_LogsTheFailureAndOpensWithoutFaulting()
        {
            ToolCatalogCachePublisher publisher = new ToolCatalogCachePublisher(RunInline);
            string cachePath = WritePreviousCache("{\"Tools\":[]}");
            LogAssert.Expect(LogType.Exception, new Regex("skill scan failed"));

            publisher.StartWrite(
                CreateTools("public-tool"),
                () => throw new IOException("skill scan failed"),
                cachePath);
            await publisher.WhenPublished;

            Assert.That(publisher.WhenPublished.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(File.ReadAllText(cachePath), Is.EqualTo("{\"Tools\":[]}"));
        }

        /// <summary>
        /// What: a changed catalog replaces the previous cache and leaves no temporary file behind, which is
        /// the path every reload after the first one takes.
        /// </summary>
        [Test]
        public async Task StartWrite_WhenACacheExists_ReplacesItAndLeavesNoTemporaryFile()
        {
            ToolCatalogCachePublisher publisher = new ToolCatalogCachePublisher(RunInline);
            string cachePath = WritePreviousCache("{\"Tools\":[]}");

            publisher.StartWrite(CreateTools("public-tool"), () => new HashSet<string>(), cachePath);
            await publisher.WhenPublished;

            Assert.That(File.ReadAllText(cachePath), Does.Contain("\"public-tool\""));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(cachePath)), Is.EqualTo(new[] { cachePath }));
        }

        /// <summary>
        /// What: a catalog equal to the cache does not rewrite the file, so reloads that change no tool leave
        /// the cache untouched.
        /// </summary>
        [Test]
        public async Task StartWrite_WhenTheCatalogIsUnchanged_DoesNotRewriteTheCache()
        {
            ToolCatalogCachePublisher publisher = new ToolCatalogCachePublisher(RunInline);
            string cachePath = Path.Combine(_temporaryRoot, ".uloop", "tools.json");
            publisher.StartWrite(CreateTools("public-tool"), () => new HashSet<string>(), cachePath);
            await publisher.WhenPublished;
            DateTime backdated = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(cachePath, backdated);

            publisher.StartWrite(CreateTools("public-tool"), () => new HashSet<string>(), cachePath);
            await publisher.WhenPublished;

            Assert.That(File.GetLastWriteTimeUtc(cachePath), Is.EqualTo(backdated));
        }

        // Why inline: EditMode tests must not start thread-pool work, so the background step runs on
        // the calling thread and hands back a task in the state the work ended in.
        private static Task RunInline(Action write)
        {
            try
            {
                write();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        private string WritePreviousCache(string content)
        {
            string cachePath = Path.Combine(_temporaryRoot, ".uloop", "tools.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllText(cachePath, content);
            return cachePath;
        }

        private static ToolInfo[] CreateTools(params string[] toolNames)
        {
            ToolInfo[] tools = new ToolInfo[toolNames.Length];
            for (int i = 0; i < toolNames.Length; i++)
            {
                tools[i] = new ToolInfo(toolNames[i], new ToolParameterSchema());
            }

            return tools;
        }
    }
}
