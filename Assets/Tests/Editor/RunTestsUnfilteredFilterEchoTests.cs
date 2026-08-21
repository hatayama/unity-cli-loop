using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies unfiltered test-name echo when run-tests finds nothing under a filter.
    /// </summary>
    public sealed class RunTestsUnfilteredFilterEchoTests
    {
        /// <summary>
        /// What: a retrieved list appends the exact filter-mismatch sentence and copies echo fields.
        /// </summary>
        [Test]
        public void ApplyIfRetrieved_WhenFilterMisses_AppendsExactMessageAndEchoFields()
        {
            RunTestsResponse response = CreateNoTestsResponse();
            RunTestsUnfilteredTestListResult result = RunTestsUnfilteredTestListResult.Success(
                new[] { "Example.Tests.Alpha", "Example.Tests.Beta" });

            RunTestsUnfilteredFilterEcho.ApplyIfRetrieved(
                response,
                TestFilterType.regex,
                "Missing.*",
                result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria No tests matched FilterType 'regex' with FilterValue 'Missing.*'. 2 test(s) exist in this TestMode without the filter; compare UnfilteredTestNames against the filter value."));
            Assert.That(response.FilterType, Is.EqualTo("regex"));
            Assert.That(response.FilterValue, Is.EqualTo("Missing.*"));
            Assert.That(response.UnfilteredTestCount, Is.EqualTo(2));
            Assert.That(
                response.UnfilteredTestNames,
                Is.EqualTo(new List<string> { "Example.Tests.Alpha", "Example.Tests.Beta" }));
        }

        /// <summary>
        /// What: 21 retrieved names keep UnfilteredTestCount at 21 and cap UnfilteredTestNames at 20.
        /// </summary>
        [Test]
        public void ApplyIfRetrieved_WhenTwentyOneNames_CapsListedNamesAndKeepsTotalCount()
        {
            List<string> supplied = new List<string>(21);
            for (int index = 1; index <= 21; index++)
            {
                supplied.Add("Example.Tests.Test" + index.ToString("00"));
            }

            RunTestsResponse response = CreateNoTestsResponse();
            RunTestsUnfilteredFilterEcho.ApplyIfRetrieved(
                response,
                TestFilterType.exact,
                "Missing.Test",
                RunTestsUnfilteredTestListResult.Success(supplied));

            List<string> expectedListed = supplied.GetRange(0, 20);
            Assert.That(response.UnfilteredTestCount, Is.EqualTo(21));
            Assert.That(response.UnfilteredTestNames, Is.EqualTo(expectedListed));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria No tests matched FilterType 'exact' with FilterValue 'Missing.Test'. 21 test(s) exist in this TestMode without the filter; compare UnfilteredTestNames against the filter value."));
        }

        /// <summary>
        /// What: a failed retrieve leaves the original message and omits echo fields from JSON.
        /// </summary>
        [Test]
        public void ApplyIfRetrieved_WhenNotRetrieved_LeavesMessageAndOmitsEchoFieldsFromJson()
        {
            RunTestsResponse response = CreateNoTestsResponse();

            RunTestsUnfilteredFilterEcho.ApplyIfRetrieved(
                response,
                TestFilterType.exact,
                "Missing.Test",
                RunTestsUnfilteredTestListResult.NotRetrieved());

            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(response.ShouldSerializeFilterType(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeFilterValue(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestNames(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestCount(), Is.EqualTo(false));

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            JObject parsed = JObject.Parse(json);
            Assert.That(parsed.Property("FilterType"), Is.EqualTo(null));
            Assert.That(parsed.Property("FilterValue"), Is.EqualTo(null));
            Assert.That(parsed.Property("UnfilteredTestNames"), Is.EqualTo(null));
            Assert.That(parsed.Property("UnfilteredTestCount"), Is.EqualTo(null));
        }

        /// <summary>
        /// What: filter-all NoTestsFound omits echo fields even when an unfiltered list is supplied.
        /// </summary>
        [Test]
        public void ApplyIfRetrieved_WhenFilterTypeIsAll_OmitsEchoFieldsFromJson()
        {
            RunTestsResponse response = CreateNoTestsResponse();

            RunTestsUnfilteredFilterEcho.ApplyIfRetrieved(
                response,
                TestFilterType.all,
                "",
                RunTestsUnfilteredTestListResult.Success(new[] { "Example.Tests.Alpha" }));

            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(response.ShouldSerializeFilterType(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestNames(), Is.EqualTo(false));

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            JObject parsed = JObject.Parse(json);
            Assert.That(parsed.Property("FilterType"), Is.EqualTo(null));
            Assert.That(parsed.Property("UnfilteredTestNames"), Is.EqualTo(null));
            Assert.That(parsed.Property("UnfilteredTestCount"), Is.EqualTo(null));
        }

        private static RunTestsResponse CreateNoTestsResponse()
        {
            return new RunTestsResponse(
                success: false,
                message: RunTestsResponse.NoTestsFoundMessage,
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 0,
                passedCount: 0,
                failedCount: 0,
                skippedCount: 0,
                xmlPath: null,
                status: RunTestsExecutionStatus.NoTestsFound,
                hasFailures: false,
                noTestsFound: true,
                noTestsFoundExplanation: RunTestsResponse.NoTestsFoundExplanationText);
        }
    }
}
