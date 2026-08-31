using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Wraps one user expression in a compiled evaluator using the execute-dynamic-code compiler.
    /// </summary>
    public sealed class WatchExpressionCompiler
    {
        private const string WatchNamespace = "io.github.hatayama.UnityCliLoop.DynamicWatch";
        private const string WatchClassName = "WatchExpressionEntryPoint";
        private const string EvaluateMethodName = "Evaluate";

        private readonly IDynamicCompilationService _compilationService;

        public WatchExpressionCompiler(IDynamicCompilationService compilationService)
        {
            _compilationService = compilationService ?? throw new ArgumentNullException(nameof(compilationService));
        }

        public async Task<WatchCompilationResult> CompileAsync(string expression, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return WatchCompilationResult.FailureResult(
                    "Watch expression must not be empty.",
                    new List<CompilationError>());
            }

            CompilationResult compilationResult = await _compilationService.CompileAsync(
                new CompilationRequest
                {
                    Code = BuildSource(expression),
                    ClassName = WatchClassName,
                    Namespace = WatchNamespace
                },
                ct).ConfigureAwait(false);

            if (!compilationResult.Success)
            {
                return WatchCompilationResult.FailureResult(
                    "Watch expression compilation failed.",
                    compilationResult.Errors);
            }

            Type evaluatorType = compilationResult.CompiledAssembly.GetType(
                $"{WatchNamespace}.{WatchClassName}",
                false);
            if (evaluatorType == null)
            {
                return WatchCompilationResult.FailureResult(
                    "Compiled watch expression evaluator type was not found.",
                    new List<CompilationError>());
            }

            MethodInfo evaluateMethod = evaluatorType.GetMethod(
                EvaluateMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (evaluateMethod == null)
            {
                return WatchCompilationResult.FailureResult(
                    "Compiled watch expression evaluator method was not found.",
                    new List<CompilationError>());
            }

            object evaluatorInstance = Activator.CreateInstance(evaluatorType);
            IWatchExpressionEvaluator evaluator = new CompiledWatchExpressionEvaluator(
                evaluatorInstance,
                evaluateMethod);
            return WatchCompilationResult.SuccessResult(evaluator);
        }

        internal static string BuildSource(string expression)
        {
            return $"using System;\n"
                + "using io.github.hatayama.UnityCliLoop.Runtime;\n"
                + $"namespace {WatchNamespace}\n"
                + "{\n"
                + $"    public sealed class {WatchClassName}\n"
                + "    {\n"
                + "        public object Evaluate()\n"
                + "        {\n"
                + $"            return (object)({expression});\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
