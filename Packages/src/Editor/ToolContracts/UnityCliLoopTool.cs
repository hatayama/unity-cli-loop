using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Shared serializer for converting CLI request parameters into schema DTOs
    /// </summary>
    internal static class UnityCliLoopToolParameterSerializer
    {
        // A single shared instance lets the contract resolver reuse its per-type metadata
        // cache across all tools and requests instead of rebuilding it per invocation.
        // Both JsonSerializer and the resolver are thread-safe once configured.
        internal static readonly JsonSerializer CamelCaseSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Converters = { new CaseInsensitiveStringEnumConverter() }
        });
    }

    // Related classes:
    // - IUnityCliLoopTool: The interface that this class implements.
    // - UnityCliLoopToolRegistry: Registers and manages instances of tool implementations.
    // - UnityCliLoopToolParameterSchemaGenerator: Generates the JSON schema for tool parameters.
    /// <summary>
    /// Abstract base class for type-safe Unity tools using Schema and Response types
    /// </summary>
    /// <typeparam name="TSchema">Schema type for tool parameters</typeparam>
    /// <typeparam name="TResponse">Response type for tool results</typeparam>
    public abstract class UnityCliLoopTool<TSchema, TResponse> : IUnityCliLoopTool
        where TSchema : UnityCliLoopToolSchema, new()
        where TResponse : UnityCliLoopToolResponse
    {
        public abstract string ToolName { get; }

        // The schema is pure reflection output over an immutable type, so it is generated
        // once per TSchema (static fields on a generic class are per closed generic type).
        private static readonly ToolParameterSchema CachedParameterSchema =
            UnityCliLoopToolParameterSchemaGenerator.FromDto<TSchema>();

        /// <summary>
        /// Automatically generates parameter schema from TSchema type
        /// </summary>
        public virtual ToolParameterSchema ParameterSchema => CachedParameterSchema;

        /// <summary>
        /// Execute tool with type-safe Schema parameters.
        /// </summary>
        /// <remarks>
        /// Implementations must use <c>ConfigureAwait(false)</c> on every await in this method
        /// and everything it calls into. Continuations posted to Unity's SynchronizationContext
        /// are not executed while Play Mode is paused (observed: a pause-point hit mid-command
        /// left a captured continuation unexecuted while EditorApplication.update kept ticking),
        /// so a single captured await anywhere in the chain can hang a tool forever once the
        /// Editor pauses mid-command.
        /// </remarks>
        /// <param name="parameters">Strongly typed parameters</param>
        /// <param name="ct">Cancellation token for timeout control</param>
        /// <returns>Strongly typed tool execution result</returns>
        protected abstract Task<TResponse> ExecuteAsync(TSchema parameters, CancellationToken ct);

        /// <summary>
        /// IUnityCliLoopTool implementation - converts JToken to Schema and returns UnityCliLoopToolResponse
        /// </summary>
        public async Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
        {
            // Convert JToken to strongly typed Schema
            TSchema parameters = ConvertToSchema(paramsToken);

            // Execute with type-safe parameters
            TResponse response = await ExecuteAsync(parameters, ct).ConfigureAwait(false);

            // Return as UnityCliLoopToolResponse for IUnityCliLoopTool interface compatibility
            return response;
        }

        /// <summary>
        /// Convert JToken to strongly typed Schema with default value fallback
        /// </summary>
        private TSchema ConvertToSchema(JToken paramsToken)
        {
            if (paramsToken == null || paramsToken.Type == JTokenType.Null)
            {
                // Return default instance if no parameters provided
                return new TSchema();
            }
            
            // Try to deserialize from JToken with the shared camelCase serializer.
            // This allows client side to use camelCase while C# uses PascalCase.
            TSchema schema;
            try
            {
                schema = paramsToken.ToObject<TSchema>(UnityCliLoopToolParameterSerializer.CamelCaseSerializer);
            }
            catch (JsonSerializationException ex)
            {
                // Create detailed error message for type mismatches
                string errorMessage = $"Parameter type mismatch for tool '{ToolName}': {ex.Message}";
                
                // Check for specific Dictionary<string, object> conversion errors
                if (ex.Message.Contains("Dictionary") && ex.Message.Contains("Error converting value"))
                {
                    string received = paramsToken["parameters"]?.ToString() ?? paramsToken["Parameters"]?.ToString() ?? "null";
                    errorMessage =
                        "Parameter 'Parameters' must be an object, not a string. " +
                        "Do: omit 'Parameters' or use {}. " +
                        "Don't: \"{}\" (string). " +
                        $"Received: {received}";
                }
                
                throw new UnityCliLoopToolParameterValidationException(errorMessage, ex);
            }

            // If deserialization returns null, create default instance
            if (schema == null)
            {
                schema = new TSchema();
            }

            // Apply default values for null properties
            return ApplyDefaultValues(schema);
        }

        /// <summary>
        /// Apply default values to Schema properties if they are null
        /// Override this method to provide custom default value logic
        /// </summary>
        protected virtual TSchema ApplyDefaultValues(TSchema schema)
        {
            // Default implementation - return as is
            // Subclasses can override to apply specific default values
            return schema;
        }
    }
}
