using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Console log counter counts each log type regardless of the Console filter and puts the
    /// user's filter back afterwards.
    /// </summary>
    public sealed class GenericConsoleWindowUtilityTests
    {
        private ConsoleLogRetriever _retriever;
        private int _originalSimpleMask;

        [SetUp]
        public void SetUp()
        {
            _retriever = new ConsoleLogRetriever();
            _originalSimpleMask = ToSimpleMask(_retriever.GetCurrentMask());
        }

        [TearDown]
        public void TearDown()
        {
            _retriever.SetMask(_originalSimpleMask);
        }

        /// <summary>
        /// Verifies new logs and warnings are counted even while the Console shows only errors, and that the
        /// error-only filter is still set afterwards.
        /// </summary>
        [Test]
        public void GetConsoleLogCounts_WithAFilteredConsole_CountsEveryTypeAndRestoresTheFilter()
        {
            _retriever.SetMask(1);
            int filteredMask = _retriever.GetCurrentMask();
            GenericConsoleWindowUtility.GetConsoleLogCounts(out int errorsBefore, out int warningsBefore, out int logsBefore);

            Debug.Log("GenericConsoleWindowUtilityTests log " + System.Guid.NewGuid());
            Debug.LogWarning("GenericConsoleWindowUtilityTests warning " + System.Guid.NewGuid());
            GenericConsoleWindowUtility.GetConsoleLogCounts(out int errorsAfter, out int warningsAfter, out int logsAfter);

            Assert.That(logsAfter, Is.EqualTo(logsBefore + 1));
            Assert.That(warningsAfter, Is.EqualTo(warningsBefore + 1));
            Assert.That(errorsAfter, Is.EqualTo(errorsBefore));
            Assert.That(_retriever.GetCurrentMask(), Is.EqualTo(filteredMask));
        }

        /// <summary>
        /// Verifies a filter that shows errors and logs but hides warnings is put back exactly, so each type's
        /// flag is mapped back on its own.
        /// </summary>
        [Test]
        public void GetConsoleLogCounts_WithOnlyWarningsHidden_KeepsWarningsHidden()
        {
            _retriever.SetMask(5);
            int errorAndLogMask = _retriever.GetCurrentMask();

            GenericConsoleWindowUtility.GetConsoleLogCounts(out int _, out int _, out int _);

            Assert.That(_retriever.GetCurrentMask(), Is.EqualTo(errorAndLogMask));
        }

        private static int ToSimpleMask(int unityMask)
        {
            // Mirrors the Console's internal error, warning, and log flag bits.
            int simpleMask = 0;
            if ((unityMask & 0x200) != 0)
            {
                simpleMask |= 1;
            }

            if ((unityMask & 0x100) != 0)
            {
                simpleMask |= 2;
            }

            if ((unityMask & 0x80) != 0)
            {
                simpleMask |= 4;
            }

            return simpleMask;
        }
    }
}
