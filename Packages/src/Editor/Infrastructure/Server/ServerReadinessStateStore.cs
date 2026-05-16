using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal enum ServerReadinessPhase
    {
        Stopped,
        Starting,
        Compiling,
        Reloading,
        Recovering,
        Ready,
        Failed,
        Stopping
    }

    /// <summary>
    /// External readiness snapshot consumed by native CLI processes across domain reload boundaries.
    /// </summary>
    internal sealed class ServerReadinessState
    {
        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("generationId")]
        public string GenerationId { get; set; }

        [JsonProperty("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("endpoint")]
        public string Endpoint { get; set; }

        [JsonProperty("lastError")]
        public string LastError { get; set; }
    }

    /// <summary>
    /// Writes the server readiness state file that lets CLI callers distinguish startup, reload, recovery, ready, and failed states.
    /// </summary>
    internal sealed class ServerReadinessStateStore
    {
        private readonly string _stateFilePath;

        internal ServerReadinessStateStore(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be null or empty");

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("projectRoot must not be null or empty.", nameof(projectRoot));
            }

            _stateFilePath = Path.Combine(
                projectRoot,
                UnityCliLoopConstants.TEMP_DIR,
                UnityCliLoopConstants.UNITYCLILOOP_DIR,
                UnityCliLoopConstants.SERVER_STATE_FILE_NAME);
        }

        internal string StateFilePath => _stateFilePath;

        internal void Write(
            ServerReadinessPhase phase,
            string generationId,
            string reason,
            string endpoint,
            string lastError)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(generationId), "generationId must not be null or empty");

            if (string.IsNullOrWhiteSpace(generationId))
            {
                throw new ArgumentException("generationId must not be null or empty.", nameof(generationId));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath));
            ServerReadinessState state = new()
            {
                Phase = ToWirePhase(phase),
                GenerationId = generationId,
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Reason = reason,
                Endpoint = endpoint,
                LastError = lastError
            };
            string content = JsonConvert.SerializeObject(state, Formatting.Indented);
            AtomicFileWriter.Write(_stateFilePath, content);
        }

        internal ServerReadinessState Read()
        {
            AtomicFileWriter.RecoverSidecarFiles(_stateFilePath);
            if (!File.Exists(_stateFilePath))
            {
                return null;
            }

            string content = File.ReadAllText(_stateFilePath);
            return JsonConvert.DeserializeObject<ServerReadinessState>(content);
        }

        internal void Delete()
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }

            AtomicFileWriter.CleanupBackup(_stateFilePath + ".bak");
        }

        internal static string CreateGenerationId()
        {
            return Guid.NewGuid().ToString(UnityCliLoopConstants.GUID_FORMAT_NO_HYPHENS);
        }

        private static string ToWirePhase(ServerReadinessPhase phase)
        {
            return phase.ToString().ToLowerInvariant();
        }
    }
}
