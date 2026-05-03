using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
    [TestFixture]
    public class ExecuteDynamicCodeResponseTimingAugmenterTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [Test]
        public void AppendTimingEntries_WhenResponseHasExistingTimings_ShouldAppendRpcTimings()
        {
            DynamicCodeStartupTelemetry.MarkServerReady();
            DynamicCodeStartupTelemetry.MarkPrewarmCompleted();
            ExecuteDynamicCodeResponse response = new ExecuteDynamicCodeResponse
            {
                Timings = new List<string>
                {
                    "[Perf] Backend: SharedRoslynWorker"
                }
            };

            ExecuteDynamicCodeResponseTimingAugmenter.AppendTimingEntries(
                response,
                12.3,
                45.6,
                78.9);

            Assert.That(response.Timings, Has.Member("[Perf] Backend: SharedRoslynWorker"));
            Assert.That(response.Timings, Has.Member("[Perf] MainThreadWait: 12.3ms"));
            Assert.That(response.Timings, Has.Member("[Perf] ToolTotal: 45.6ms"));
            Assert.That(response.Timings, Has.Member("[Perf] RequestTotal: 78.9ms"));
            Assert.That(response.Timings, Has.Member("[Perf] WarmReady: True"));
            Assert.That(response.Timings, Has.Member("[Perf] PrewarmState: Completed"));
        }

        [Test]
        public void AppendTimingEntries_WhenResponseTimingsAreNull_ShouldCreateTimingList()
        {
            ExecuteDynamicCodeResponse response = new ExecuteDynamicCodeResponse
            {
                Timings = null
            };

            ExecuteDynamicCodeResponseTimingAugmenter.AppendTimingEntries(
                response,
                1.0,
                2.0,
                3.0);

            Assert.That(response.Timings, Is.Not.Null);
            Assert.That(response.Timings, Has.Count.EqualTo(5));
            Assert.That(response.Timings, Has.Member("[Perf] WarmReady: False"));
            Assert.That(response.Timings, Has.Member("[Perf] PrewarmState: NotRequested"));
        }

        [Test]
        public void Serialize_WhenTimingsExist_ShouldOmitTimingsFromJson()
        {
            ExecuteDynamicCodeResponse response = new ExecuteDynamicCodeResponse
            {
                Success = true,
                Timings = new List<string>
                {
                    "[Perf] RequestTotal: 78.9ms"
                }
            };

            string json = JsonConvert.SerializeObject(response);

            Assert.That(response.Timings, Has.Member("[Perf] RequestTotal: 78.9ms"));
            Assert.That(json, Does.Not.Contain("\"Timings\""));
            Assert.That(json, Does.Contain("\"Success\":true"));
        }
    }
}
