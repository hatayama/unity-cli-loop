using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    // Related classes:
    // - IUnityCliLoopTool: The interface that this class implements.
    // - UnityCliLoopToolRegistry: Registers and manages instances of tool implementations.
    // - UnityCliLoopToolParameterSchemaGenerator: Generates the JSON schema for tool parameters.
    /// <summary>
    /// Abstract base class for type-safe Unity tools using Schema and Response types
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Tool Layer (CLI Command Interface)
    /// </summary>
    /// <typeparam name="TSchema">Schema type for tool parameters</typeparam>
    /// <typeparam name="TResponse">Response type for tool results</typeparam>
    public abstract class UnityCliLoopTool<TSchema, TResponse> : IUnityCliLoopTool
        where TSchema : UnityCliLoopToolSchema, new()
        where TResponse : UnityCliLoopToolResponse
    {
        public abstract string ToolName { get; }

        /// <summary>
        /// Automatically generates parameter schema from TSchema type
        /// </summary>
        public virtual ToolParameterSchema ParameterSchema =>
            UnityCliLoopToolParameterSchemaGenerator.FromDto<TSchema>();

        /// <summary>
        /// Execute tool with type-safe Schema parameters
        /// Note: This method is called from the main thread context. 
        /// MainThreadSwitcher.SwitchToMainThread() is already handled by the upper layer (JsonRpcProcessor),
        /// so individual tools do not need to call it again.
        /// </summary>
        /// <param name="parameters">Strongly typed parameters</param>
        /// <param name="cancellationToken">Cancellation token for timeout control</param>
        /// <returns>Strongly typed tool execution result</returns>
        protected abstract Task<TResponse> ExecuteAsync(TSchema parameters, CancellationToken cancellationToken);

        /// <summary>
        /// IUnityCliLoopTool implementation - converts JToken to Schema and returns UnityCliLoopToolResponse
        /// </summary>
        public async Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken)
        {
            // Convert JToken to strongly typed Schema
            TSchema parameters = ConvertToSchema(paramsToken);

            // Execute with type-safe parameters
            TResponse response = await ExecuteAsync(parameters, CancellationToken.None);

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
            
            // Create JsonSerializerSettings with CamelCasePropertyNamesContractResolver
            // This allows client side to use camelCase while C# uses PascalCase
            JsonSerializerSettings settings = new()            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            
            // Create JsonSerializer with custom settings
            JsonSerializer serializer = JsonSerializer.Create(settings);
            
            // Try to deserialize from JToken with custom serializer
            TSchema schema;
            try
            {
                schema = paramsToken.ToObject<TSchema>(serializer);
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
