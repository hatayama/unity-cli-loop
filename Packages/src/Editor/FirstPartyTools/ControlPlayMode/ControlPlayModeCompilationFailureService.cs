using System;
using System.Collections.Generic;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides saved compiler diagnostics for PlayMode entry checks.
    /// </summary>
    public interface IControlPlayModeCompilationFailureProvider
    {
        ControlPlayModeCompileError[] GetLastFailedErrors();
    }

    /// <summary>
    /// Provides the current Unity compiler-failure gate used before PlayMode entry.
    /// </summary>
    public interface IControlPlayModeCompilationFailureGate
    {
        bool HasScriptCompilationFailed();
    }

    /// <summary>
    /// Records compiler errors from the current Editor domain so PlayMode failure responses are not inferred from stale Console logs.
    /// </summary>
    internal sealed class ControlPlayModeCompilationFailureService : IControlPlayModeCompilationFailureProvider
    {
        private readonly List<ControlPlayModeCompileError> _currentErrors = new();
        private ControlPlayModeCompileError[] _lastFailedErrors = Array.Empty<ControlPlayModeCompileError>();

        internal void InitializeForEditorStartup()
        {
            CompilationPipeline.compilationStarted -= HandleCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= HandleCompilationFinished;

            CompilationPipeline.compilationStarted += HandleCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += HandleAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += HandleCompilationFinished;
        }

        internal void HandleCompilationStarted(object context)
        {
            _currentErrors.Clear();
            _lastFailedErrors = Array.Empty<ControlPlayModeCompileError>();
        }

        internal void HandleAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] compilerMessages)
        {
            if (compilerMessages == null)
            {
                return;
            }

            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type != CompilerMessageType.Error)
                {
                    continue;
                }

                _currentErrors.Add(ControlPlayModeCompileError.FromCompilerMessage(compilerMessage));
            }
        }

        internal void HandleCompilationFinished(object context)
        {
            if (_currentErrors.Count == 0)
            {
                _lastFailedErrors = Array.Empty<ControlPlayModeCompileError>();
                return;
            }

            _lastFailedErrors = _currentErrors.ToArray();
            _currentErrors.Clear();
        }

        public ControlPlayModeCompileError[] GetLastFailedErrors()
        {
            ControlPlayModeCompileError[] errors = new ControlPlayModeCompileError[_lastFailedErrors.Length];
            Array.Copy(_lastFailedErrors, errors, _lastFailedErrors.Length);
            return errors;
        }
    }

    /// <summary>
    /// Reads Unity's current script-compilation failure flag for PlayMode entry gating.
    /// </summary>
    internal sealed class ControlPlayModeCompilationFailureGate : IControlPlayModeCompilationFailureGate
    {
        public bool HasScriptCompilationFailed()
        {
            return UnityEditor.EditorUtility.scriptCompilationFailed;
        }
    }
}
