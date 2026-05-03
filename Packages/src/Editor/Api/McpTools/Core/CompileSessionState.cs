using UnityEditor;
using System;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// SessionState management class for forced recompilation
    /// Preserves request information even when assembly reload occurs
    /// </summary>
    public static class CompileSessionState
    {
        // Note: Compile requests are now managed via McpSessionManager

        /// <summary>
        /// Forced recompilation request information
        /// </summary>
        [Serializable]
        public class CompileRequestInfo
        {
            public string requestId;
            public bool forceRecompile;
            public string clientEndpoint;
            public DateTime requestTime;
            public bool isCompleted;

            public CompileRequestInfo(string requestId, bool forceRecompile, string clientEndpoint)
            {
                this.requestId = requestId;
                this.forceRecompile = forceRecompile;
                this.clientEndpoint = clientEndpoint;
                this.requestTime = DateTime.Now;
                this.isCompleted = false;
            }
        }

        /// <summary>
        /// Save forced recompilation request
        /// </summary>
        public static void SaveCompileRequest(string requestId, bool forceRecompile, string clientEndpoint = "unknown")
        {
            if (!forceRecompile) return; // Don't save normal compilation

            CompileRequestInfo requestInfo = new CompileRequestInfo(requestId, forceRecompile, clientEndpoint);
            string json = JsonConvert.SerializeObject(requestInfo);
            
            McpEditorSettings.SetCompileRequestJson(requestId, json);
            McpEditorSettings.AddPendingCompileRequest(requestId);
            
        }

        /// <summary>
        /// Get pending request IDs
        /// </summary>
        public static string[] GetPendingRequestIds()
        {
            return McpEditorSettings.GetPendingCompileRequestIds();
        }

        /// <summary>
        /// Get saved request information
        /// </summary>
        public static CompileRequestInfo GetCompileRequest(string requestId)
        {
            string json = McpEditorSettings.GetCompileRequestJson(requestId);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonConvert.DeserializeObject<CompileRequestInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Mark request as completed
        /// </summary>
        public static void MarkRequestCompleted(string requestId)
        {
            CompileRequestInfo requestInfo = GetCompileRequest(requestId);
            if (requestInfo == null) return;

            requestInfo.isCompleted = true;
            string json = JsonConvert.SerializeObject(requestInfo);
            McpEditorSettings.SetCompileRequestJson(requestId, json);
            McpEditorSettings.RemovePendingCompileRequest(requestId);
            
        }

        // Note: RemoveFromPendingRequests is now handled by McpSessionManager

        /// <summary>
        /// Start forced recompilation
        /// </summary>
        public static void StartForceRecompile()
        {
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(
                UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache
            );
        }

        /// <summary>
        /// Clear SessionState
        /// </summary>
        public static void ClearAll()
        {
            McpEditorSettings.ClearAllCompileRequests();
            
        }
    }
} 