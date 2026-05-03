using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Constants for parameter schema property names
    /// Ensures consistency between Unity and client sides
    /// </summary>
    public static class ParameterSchemaConstants
    {
        public const string TYPE_PROPERTY = "Type";
        public const string DESCRIPTION_PROPERTY = "Description";
        public const string DEFAULT_VALUE_PROPERTY = "DefaultValue";
        public const string ENUM_PROPERTY = "Enum";
        public const string PROPERTIES_PROPERTY = "Properties";
        public const string REQUIRED_PROPERTY = "Required";
    }

    /// <summary>
    /// Base interface for Unity CLI tool handlers.
    /// Following the Open-Closed principle, when adding new tools,
    /// create a new class that implements this interface
    /// </summary>
    public interface IUnityTool
    {
        /// <summary>
        /// Get tool name
        /// </summary>
        string ToolName { get; }

        /// <summary>
        /// Get parameter schema information for CLI clients.
        /// </summary>
        ToolParameterSchema ParameterSchema { get; }

        /// <summary>
        /// Execute tool
        /// </summary>
        /// <param name="paramsToken">JSON token for parameters</param>
        /// <returns>Execution result</returns>
        Task<BaseToolResponse> ExecuteAsync(JToken paramsToken);
    }

    /// <summary>
    /// Parameter schema definition for tools (immutable)
    /// </summary>
    public class ToolParameterSchema
    {
        public readonly Dictionary<string, ParameterInfo> Properties;
        public readonly string[] Required;

        public ToolParameterSchema(Dictionary<string, ParameterInfo> properties = null, string[] required = null)
        {
            Properties = properties ?? new Dictionary<string, ParameterInfo>();
            Required = required ?? new string[0];
        }
    }

    /// <summary>
    /// Individual parameter information (immutable)
    /// </summary>
    public class ParameterInfo
    {
        public readonly string Type;
        public readonly string Description;
        public readonly object DefaultValue;
        public readonly string[] Enum;

        public ParameterInfo(string type, string description, object defaultValue = null, string[] enumValues = null)
        {
            Type = type;
            Description = description;
            DefaultValue = defaultValue;
            Enum = enumValues;
        }
    }
}
