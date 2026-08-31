using System;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads the Unity CLI Loop JSON-RPC envelope without changing the wire keys.
    /// </summary>
    internal static class UloopEnvelope
    {
        private const string MethodPropertyName = "method";
        private const string ParamsPropertyName = "params";
        private const string IdPropertyName = "id";
        private const string MetadataPropertyName = "uloop";
        private const string ProjectRunnerVersionPropertyName = "projectRunnerVersion";
        private const string ProtocolVersionPropertyName = "protocolVersion";
        private const string AcceptsDispatchAckPropertyName = "acceptsDispatchAck";
        private const string AcceptsHeartbeatPropertyName = "acceptsHeartbeat";

        internal static JsonRpcRequest ParseJsonRpcRequest(string jsonRequest)
        {
            JObject request = JObject.Parse(jsonRequest);
            JObject metadata = ReadMetadata(request);

            return new JsonRpcRequest
            {
                Method = request[MethodPropertyName]?.ToString(),
                Params = request[ParamsPropertyName],
                ClientProjectRunnerVersion = ReadClientProjectRunnerVersion(metadata),
                ClientProtocolVersion = ReadClientProtocolVersion(metadata),
                AcceptsDispatchAck = ReadStrictBooleanMetadata(metadata, AcceptsDispatchAckPropertyName),
                AcceptsHeartbeat = ReadStrictBooleanMetadata(metadata, AcceptsHeartbeatPropertyName),
                Id = request[IdPropertyName]?.ToObject<object>()
            };
        }

        private static JObject ReadMetadata(JObject request)
        {
            return request[MetadataPropertyName] as JObject;
        }

        private static string ReadClientProjectRunnerVersion(JObject metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            string projectRunnerVersion = metadata[ProjectRunnerVersionPropertyName]?.ToString();
            return string.IsNullOrWhiteSpace(projectRunnerVersion) ? null : projectRunnerVersion;
        }

        private static int? ReadClientProtocolVersion(JObject metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            JToken protocolVersionToken = metadata[ProtocolVersionPropertyName];
            if (protocolVersionToken == null || protocolVersionToken.Type != JTokenType.Integer)
            {
                return null;
            }

            JValue protocolVersionValue = protocolVersionToken as JValue;
            object rawProtocolVersion = protocolVersionValue?.Value;
            if (rawProtocolVersion is int protocolVersion)
            {
                return protocolVersion;
            }

            if (!(rawProtocolVersion is long longProtocolVersion))
            {
                return null;
            }

            if (longProtocolVersion < int.MinValue || longProtocolVersion > int.MaxValue)
            {
                return null;
            }

            return (int)longProtocolVersion;
        }

        private static bool ReadStrictBooleanMetadata(JObject metadata, string propertyName)
        {
            return StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                metadata,
                propertyName,
                StringComparison.Ordinal) ?? false;
        }
    }
}
