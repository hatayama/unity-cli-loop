using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Catalog DTO returned by the CLI tool-details command.
    /// </summary>
    public class ToolInfo
    {
        [JsonProperty("name")] public string Name { get; }

        [JsonProperty("parameterSchema")] public ToolParameterSchema ParameterSchema { get; }

        [JsonProperty("displayDevelopmentOnly")] public bool DisplayDevelopmentOnly { get; }

        public ToolInfo(string name, ToolParameterSchema parameterSchema, bool displayDevelopmentOnly = false)
        {
            Name = name;
            ParameterSchema = parameterSchema;
            DisplayDevelopmentOnly = displayDevelopmentOnly;
        }
    }
}
