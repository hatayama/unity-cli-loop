using System;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Samples
{
    /// <summary>
    /// Hello World custom tool - Type-safe implementation using Schema and Response
    /// Basic implementation example of a custom tool with strongly typed parameters and response
    /// </summary>
    [UnityCliLoopTool]
    public class HelloWorldTool : UnityCliLoopTool<HelloWorldSchema, HelloWorldResponse>
    {
        public override string ToolName => "hello-world";
        
        protected override Task<HelloWorldResponse> ExecuteAsync(HelloWorldSchema parameters, CancellationToken cancellationToken)
        {
            // Type-safe parameter access - no more string parsing!
            string name = parameters.Name;
            GreetingLanguage language = parameters.Language;
            bool includeTimestamp = parameters.IncludeTimestamp;

            // Generate greeting based on language
            string greeting = language switch
            {
                GreetingLanguage.japanese => $"こんにちは、{name}さん！",
                GreetingLanguage.spanish => $"¡Hola, {name}!",
                GreetingLanguage.french => $"Bonjour, {name}!",
                _ => $"Hello, {name}!"
            };

            // Create type-safe response
            HelloWorldResponse response = new(
                message: greeting,
                language: language.ToString().ToLower(),
                timestamp: includeTimestamp ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : null
            );

            return Task.FromResult(response);
        }
    }
} 