using System.ComponentModel;
using io.github.hatayama.UnityCliLoop;

namespace Samples
{
    /// <summary>
    /// Supported languages for greeting
    /// </summary>
    public enum GreetingLanguage
    {
        english,
        japanese,
        spanish,
        french
    }

    /// <summary>
    /// Schema for HelloWorld tool parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class HelloWorldSchema : BaseToolSchema
    {
        /// <summary>
        /// Name to greet
        /// </summary>
        [Description("Name to greet")]
        public string Name { get; set; } = "World";

        /// <summary>
        /// Language for greeting
        /// </summary>
        [Description("Language for greeting")]
        public GreetingLanguage Language { get; set; } = GreetingLanguage.english;

        /// <summary>
        /// Whether to include timestamp in response
        /// </summary>
        [Description("Whether to include timestamp in response")]
        public bool IncludeTimestamp { get; set; } = true;

        /// <summary>
        /// Get language as string value for greeting generation
        /// </summary>
        public string GetLanguageString()
        {
            return Language.ToString();
        }
    }
} 