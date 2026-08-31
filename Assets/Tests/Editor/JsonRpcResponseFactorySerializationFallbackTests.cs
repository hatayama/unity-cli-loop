using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies JSON-RPC success serialization failures become error frames instead of fake success payloads.
    /// </summary>
    public sealed class JsonRpcResponseFactorySerializationFallbackTests
    {
        [Test]
        public void CreateSuccessResponse_WhenResultCannotBeSerialized_ReturnsJsonRpcError()
        {
            // Verifies serialization failure returns INTERNAL_ERROR with internal_error data instead of a
            // success frame that embeds an error-shaped object in result.
            string responseJson = JsonRpcResponseFactory.CreateSuccessResponse(
                id: 1,
                result: new UnserializableToolResponse());
            JObject response = JObject.Parse(responseJson);

            Assert.That(response["result"], Is.Null);
            Assert.That(response["error"], Is.Not.Null);
            Assert.That(response["error"]!["code"]!.Value<int>(), Is.EqualTo(UnityCliLoopServerConfig.INTERNAL_ERROR_CODE));
            Assert.That(response["error"]!["data"]!["type"]!.Value<string>(), Is.EqualTo(JsonRpcErrorTypes.InternalError));
        }

        private sealed class UnserializableToolResponse : UnityCliLoopToolResponse
        {
            public string Boom => throw new System.InvalidOperationException("intentional serialization failure");
        }
    }
}
