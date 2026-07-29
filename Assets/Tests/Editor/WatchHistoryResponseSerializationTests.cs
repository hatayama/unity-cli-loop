using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies watch history values use the same preview serializer as CapturedVariables.
    /// </summary>
    [TestFixture]
    public sealed class WatchHistoryResponseSerializationTests
    {
        [Test]
        public void FromEntry_WithListOfInts_UsesCompactJsonPreview()
        {
            // Verifies materialized int lists preview as compact JSON instead of type-name ToString.
            List<int> value = new List<int> { 0, 1, 2 };
            WatchExpressionHistoryEntry entry = CreateSuccessfulEntry(value);

            WatchHistoryResponse response = WatchHistoryResponse.FromEntry(entry);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Value, Is.EqualTo("[0,1,2]"));
        }

        [Test]
        public void FromEntry_WithListOfVector2Int_QuotesElementToStringValues()
        {
            // Verifies Vector2Int list elements keep ToString form inside a JSON string array.
            List<Vector2Int> value = new List<Vector2Int>
            {
                new Vector2Int(9, 3),
                new Vector2Int(9, 2)
            };
            WatchExpressionHistoryEntry entry = CreateSuccessfulEntry(value);

            WatchHistoryResponse response = WatchHistoryResponse.FromEntry(entry);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Value, Is.EqualTo("[\"(9, 3)\",\"(9, 2)\"]"));
        }

        [Test]
        public void FromEntry_WithVector3_KeepsCustomToStringForm()
        {
            // Verifies types with a custom ToString keep that form instead of a field JSON preview.
            Vector3 value = new Vector3(1f, 2f, 3f);
            WatchExpressionHistoryEntry entry = CreateSuccessfulEntry(value);

            WatchHistoryResponse response = WatchHistoryResponse.FromEntry(entry);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Value, Is.EqualTo(value.ToString()));
        }

        [Test]
        public void FromEntry_WithNullValue_ReturnsNullLiteral()
        {
            // Verifies a successful null evaluation still stringifies as the literal "null".
            WatchExpressionHistoryEntry entry = CreateSuccessfulEntry(null);

            WatchHistoryResponse response = WatchHistoryResponse.FromEntry(entry);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Value, Is.EqualTo("null"));
        }

        private static WatchExpressionHistoryEntry CreateSuccessfulEntry(object value)
        {
            return new WatchExpressionHistoryEntry(
                frameCount: 1,
                evaluatedAtUtc: DateTime.UtcNow,
                result: WatchEvaluationResult.SuccessResult(value));
        }
    }
}
