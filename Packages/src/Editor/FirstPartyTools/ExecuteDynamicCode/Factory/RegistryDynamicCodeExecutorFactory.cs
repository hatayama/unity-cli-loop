using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory
{
    /// <summary>
    /// Creates Registry Dynamic Code Executor instances with the dependencies required by this module.
    /// </summary>
    internal sealed class RegistryDynamicCodeExecutorFactory : IDynamicCodeExecutorProvider
    {
        private readonly IDynamicCompilationServiceFactory _compilationServiceFactory;
        private readonly IDynamicCodeSourcePreparationService _sourcePreparationService;
        private readonly CompiledCommandEntryPointResolver _entryPointResolver;

        public RegistryDynamicCodeExecutorFactory(
            IDynamicCompilationServiceFactory compilationServiceFactory,
            IDynamicCodeSourcePreparationService sourcePreparationService,
            CompiledCommandEntryPointResolver entryPointResolver)
        {
            UnityEngine.Debug.Assert(compilationServiceFactory != null, "compilationServiceFactory must not be null");
            UnityEngine.Debug.Assert(sourcePreparationService != null, "sourcePreparationService must not be null");
            UnityEngine.Debug.Assert(entryPointResolver != null, "entryPointResolver must not be null");

            _compilationServiceFactory = compilationServiceFactory;
            _sourcePreparationService = sourcePreparationService;
            _entryPointResolver = entryPointResolver;
        }

        public IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel)
        {
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            IDynamicCompilationService compiler = _compilationServiceFactory.Create(securityLevel);
            if (compiler == null)
            {
                VibeLogger.LogWarning(
                    "dynamic_executor_stub_created",
                    "DynamicCodeExecutorStub created (compilation provider unavailable)",
                    new
                    {
                        security_level = securityLevel.ToString()
                    },
                    correlationId,
                    "Dynamic code execution provider was not registered",
                    "Verify Roslyn assembly loading and define configuration");

                return new DynamicCodeExecutorStub();
            }

            ICompiledCommandInvoker invoker = new CommandRunner(_entryPointResolver);
            DynamicCodeExecutor executor = new(
                compiler,
                invoker,
                _sourcePreparationService);

            VibeLogger.LogInfo(
                "dynamic_executor_created",
                $"DynamicCodeExecutor created with security level: {securityLevel}",
                new
                {
                    security_level = securityLevel.ToString(),
                    compiler_type = compiler.GetType().Name,
                    runner_type = invoker.GetType().Name
                },
                correlationId,
                "Dynamic code execution system initialization completed",
                "Ready for execution");

            return executor;
        }
    }
}
