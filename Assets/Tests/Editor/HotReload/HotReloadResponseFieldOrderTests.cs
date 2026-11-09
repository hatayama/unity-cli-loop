using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the end of the hot-reload answer, the part a reader who cuts the output keeps.
    /// </summary>
    public class HotReloadResponseFieldOrderTests
    {
        /// <summary>
        /// Verifies that an answer holding every field written after the totals still closes with
        /// RecommendedNextAction, Outcome and Success, so a reader of only its last lines learns
        /// whether the edit went live and not just that the run succeeded.
        /// </summary>
        [Test]
        public void Serialize_WhenEditsStayedUnapplied_ClosesWithNextActionOutcomeAndSuccess()
        {
            HotReloadResponse response = new HotReloadResponse
            {
                Outcome = "PartiallyApplied",
                RecommendedNextAction = "next action",
                SelectedFiles = new[] { "Assets/Sample.cs" },
                Timing = new HotReloadTimingResponse(),
            };

            string json = JsonConvert.SerializeObject(
                response,
                UnityCliLoopJsonResponseSerializerSettings.Settings);

            string[] lastFields = JObject.Parse(json)
                .Properties()
                .Select(property => property.Name)
                .TakeLast(3)
                .ToArray();
            Assert.That(lastFields, Is.EqualTo(new[] { "RecommendedNextAction", "Outcome", "Success" }));
        }
    }
}
