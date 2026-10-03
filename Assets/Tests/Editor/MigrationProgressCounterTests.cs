using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies migration progress counting around cancellation.
    /// </summary>
    public sealed class MigrationProgressCounterTests
    {
        /// <summary>
        /// Verifies that reporting a processed item with a cancelled token neither counts it nor reports progress.
        /// </summary>
        [Test]
        public void ReportProcessedItemAsync_WhenTokenIsCancelled_DoesNotCountItem()
        {
            List<ThirdPartyToolMigrationProgress> reports = new List<ThirdPartyToolMigrationProgress>();
            MigrationProgressCounter counter = new MigrationProgressCounter(3, new RecordingProgress(reports));

            Task task = counter.ReportProcessedItemAsync(new CancellationToken(true));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports[0].ProcessedItemCount, Is.EqualTo(0));
        }

        private sealed class RecordingProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly List<ThirdPartyToolMigrationProgress> _reports;

            public RecordingProgress(List<ThirdPartyToolMigrationProgress> reports)
            {
                _reports = reports;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                _reports.Add(value);
            }
        }
    }
}
