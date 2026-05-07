using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dangerous API Catalog behavior.
    /// </summary>
    [TestFixture]
    public class DangerousApiCatalogTests
    {
        [Test]
        public void EnumerateDangerousMembers_ShouldReturnDefensiveCopies()
        {
            List<KeyValuePair<string, IReadOnlyCollection<string>>> snapshot =
                new List<KeyValuePair<string, IReadOnlyCollection<string>>>(DangerousApiCatalog.EnumerateDangerousMembers());

            int processEntryIndex = snapshot.FindIndex(entry => entry.Key == "System.Diagnostics.Process");
            Assert.AreNotEqual(-1, processEntryIndex, "Process entry should exist in the catalog");

            KeyValuePair<string, IReadOnlyCollection<string>> processEntry = snapshot[processEntryIndex];
            List<string> mutableCopy = processEntry.Value as List<string>;
            Assert.IsNotNull(mutableCopy, "Returned collection should be a mutable defensive copy");
            Assert.Contains("Start", mutableCopy, "The defensive copy should contain the dangerous API entry");

            mutableCopy.Remove("Start");

            Assert.IsTrue(
                DangerousApiCatalog.IsDangerousApi("System.Diagnostics.Process", "Start"),
                "Mutating an enumerated copy must not weaken the catalog");
        }
    }
}
