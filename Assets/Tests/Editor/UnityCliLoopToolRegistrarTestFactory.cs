using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Creates tool registrar services for tests that need a registry-backed execution path.
    /// </summary>
    internal static class UnityCliLoopToolRegistrarTestFactory
    {
        internal static UnityCliLoopToolRegistrarService Create(
            Func<IReadOnlyList<IUnityCliLoopTool>> toolDiscovery)
        {
            IToolSettingsPort toolSettingsPort = new ToolSettingsRepository();
            return new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                toolDiscovery);
        }
    }
}
