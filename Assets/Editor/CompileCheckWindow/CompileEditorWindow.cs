using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Defines the Unity Editor window for Compile Editor workflows.
    /// </summary>
    public class CompileEditorWindow : EditorWindow
    {
        private CompileController _compileController;
        private CompileLogDisplay _logDisplay;
        private Vector2 _scrollPosition;
        private bool _forceRecompile = false;
        private bool _isPostCompileReadinessRunning = false;

        // Note: Compile window data is now managed via McpSessionManager

        [MenuItem("UnityCliLoop/Windows/Compile Tool")]
        public static void ShowWindow()
        {
            CompileEditorWindow window = GetWindow<CompileEditorWindow>();
            window.titleContent = new GUIContent("Compile Tool");
            window.Show();
        }

        private void OnEnable()
        {
            // Create instances only if they don't exist yet
            if (_compileController == null || _logDisplay == null)
            {
                _compileController = new CompileController(UnityCliLoopEditorSessionStateFacade.Service);
                _logDisplay = new CompileLogDisplay();

                // Subscribe to events
                _compileController.OnCompileStarted += _logDisplay.AppendStartMessage;
                _compileController.OnAssemblyCompiled += _logDisplay.AppendAssemblyMessage;
                _compileController.OnCompileCompleted += OnCompileCompleted;
            }
            else
            {
                // If an instance already exists, re-subscribe as the event subscription might have been lost
                if (!_compileController.IsCompiling)
                {
                    _compileController.OnCompileStarted += _logDisplay.AppendStartMessage;
                    _compileController.OnAssemblyCompiled += _logDisplay.AppendAssemblyMessage;
                    _compileController.OnCompileCompleted += OnCompileCompleted;
                }
            }
        }

        private void OnDisable()
        {
            DisposeInstances();

            // Set to null only on OnDisable for a complete cleanup
            _compileController = null;
            _logDisplay = null;
        }

        private void DisposeInstances()
        {
            if (_compileController != null)
            {
                // Unsubscribe from events
                if (_logDisplay != null)
                {
                    _compileController.OnCompileStarted -= _logDisplay.AppendStartMessage;
                    _compileController.OnAssemblyCompiled -= _logDisplay.AppendAssemblyMessage;
                }
                _compileController.OnCompileCompleted -= OnCompileCompleted;

                _compileController.Dispose();
            }

            if (_logDisplay != null)
            {
                _logDisplay.Dispose();
            }
        }

        private void OnGUI()
        {
            if (_compileController == null || _logDisplay == null) return;

            GUILayout.Label("Unity Compile Tool", EditorStyles.boldLabel);

            // Force recompile option
            _forceRecompile = EditorGUILayout.Toggle("Force Recompile", _forceRecompile);
            GUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(IsCompileActionBusy());
            string buttonText = CreateCompileButtonText();
            if (GUILayout.Button(buttonText, GUILayout.Height(30)))
            {
                // Execute compilation using async/await
                ExecuteCompileAsync().Forget();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(5);

            // Clear button
            if (GUILayout.Button("Clear Log", GUILayout.Height(25)))
            {
                ClearLog();
            }

            GUILayout.Space(10);

            GUILayout.Label("Compilation Result:", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(300));
            EditorGUILayout.TextArea(_logDisplay.LogText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            DrawMessageDetails();
        }

        private async Task ExecuteCompileAsync()
        {
            CompileResult result = await _compileController.TryCompileAsync(_forceRecompile, CancellationToken.None);
            if (ShouldRunExecuteDynamicCodeReadinessAfterCompile(result))
            {
                _isPostCompileReadinessRunning = true;
                Repaint();
                try
                {
                    await RunExecuteDynamicCodeReadinessProbesAfterCompileAsync(CancellationToken.None);
                }
                finally
                {
                    _isPostCompileReadinessRunning = false;
                    Repaint();
                }
            }
            
            // Output result to log (for debugging)
            string message = string.IsNullOrEmpty(result.Message) ? "(none)" : result.Message;
            bool displaySuccess = result.Success ?? false;
            bool shouldWarn = result.Success == false;
            string logMessage =
                $"Compilation finished: Success={displaySuccess}, Indeterminate={result.IsIndeterminate}, Errors={result.Errors.Length}, Warnings={result.Warnings.Length}, Message={message}";

            if (shouldWarn)
            {
                UnityEngine.Debug.LogWarning(logMessage);
                return;
            }

            UnityEngine.Debug.Log(logMessage);
        }

        private static bool ShouldRunExecuteDynamicCodeReadinessAfterCompile(CompileResult result)
        {
            return result.Success == true;
        }

        private static async Task RunExecuteDynamicCodeReadinessProbesAfterCompileAsync(CancellationToken ct)
        {
            // Why: the editor Compile Tool bypasses the native CLI's post-compile readiness wait,
            // so it must run the same hidden probe path before handing control back to the user.
            foreach (string code in ExecuteDynamicCodeReadinessProbe.CreateReturnStringProbeCodes())
            {
                ct.ThrowIfCancellationRequested();
                JObject parameters = new()
                {
                    ["Code"] = code,
                    ["CompileOnly"] = false,
                    ["YieldToForegroundRequests"] = false
                };
                await UnityCliLoopToolRegistrar.ExecuteToolAsync(
                    "execute-dynamic-code",
                    parameters,
                    ct);
            }
        }

        private bool IsCompileActionBusy()
        {
            return _compileController.IsCompiling || _isPostCompileReadinessRunning;
        }

        private string CreateCompileButtonText()
        {
            if (_compileController.IsCompiling)
            {
                return "Compiling...";
            }

            if (_isPostCompileReadinessRunning)
            {
                return "Preparing...";
            }

            return _forceRecompile ? "Run Force Compile" : "Run Compile";
        }

        private void OnCompileCompleted(CompileResult result)
        {
            _logDisplay.AppendCompletionMessage(result);
            Repaint();
        }

        private void DrawMessageDetails()
        {
            var messages = _compileController.CompileMessages;
            if (messages.Count > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Error/Warning Details ({messages.Count} items):", EditorStyles.boldLabel);

                foreach (CompilerMessage message in messages)
                {
                    GUIStyle style = message.type == CompilerMessageType.Error ?
                        EditorStyles.helpBox : EditorStyles.helpBox;

                    string prefix = message.type == CompilerMessageType.Error ? "[Error]" : "[Warning]";
                    EditorGUILayout.LabelField($"{prefix} {message.message}", style);
                }
            }
        }

        private void ClearLog()
        {
            _logDisplay.Clear();
            _compileController.ClearMessages();

            Repaint();
        }
    }
}
