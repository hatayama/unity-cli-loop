#if ULOOP_HAS_INPUT_SYSTEM
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the record-input key filter accepts only defined key names.
    /// </summary>
    public sealed class InputRecordingKeyFilterTests
    {
        /// <summary>
        /// Tests that named keys are accepted case-insensitively and no name is reported invalid.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenGivenKeyNames_KeepsThemAll()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter("space, w");

            Assert.IsEmpty(result.InvalidKeyNames);
            Assert.IsNotNull(result.Filter);
            Assert.AreEqual(new HashSet<Key> { Key.Space, Key.W }, result.Filter);
        }

        /// <summary>
        /// Tests that a bare digit is reported invalid instead of silently filtering the key whose
        /// enum ordinal it happens to be.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenGivenAnOrdinal_ReportsItInvalid()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter("3");

            Assert.AreEqual(new[] { "3" }, result.InvalidKeyNames);
            Assert.IsNull(result.Filter);
        }

        /// <summary>
        /// Tests that an invalid entry alongside a valid one is still reported, so a partially
        /// applied filter is never mistaken for the requested one.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenOneEntryIsInvalid_ReportsThatEntry()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter("W, 3");

            Assert.AreEqual(new[] { "3" }, result.InvalidKeyNames);
        }

        /// <summary>
        /// Tests that a filter made only of empty entries is reported invalid: it would otherwise
        /// record every key while looking like no filter was requested.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenEveryEntryIsEmpty_ReportsTheRawInputInvalid()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter(", ,");

            Assert.AreEqual(new[] { ", ," }, result.InvalidKeyNames);
            Assert.IsNull(result.Filter);
        }

        /// <summary>
        /// Tests that a trailing comma is harmless once another entry names a key.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenAnEntryIsEmptyBesideANamedKey_KeepsTheKey()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter("W,");

            Assert.IsEmpty(result.InvalidKeyNames);
            Assert.AreEqual(new HashSet<Key> { Key.W }, result.Filter);
        }

        /// <summary>
        /// Tests that no filter and no invalid name is reported when the parameter is omitted.
        /// </summary>
        [Test]
        public void ParseKeyFilter_WhenGivenNothing_ReportsNoFilter()
        {
            KeyFilterParseResult result = InputRecordingFileHelper.ParseKeyFilter("");

            Assert.IsEmpty(result.InvalidKeyNames);
            Assert.IsNull(result.Filter);
        }
    }
}
#endif
