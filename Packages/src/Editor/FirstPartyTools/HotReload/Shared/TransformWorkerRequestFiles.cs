using System;
using System.IO;
using System.Text;

using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The temporary directory one transform-worker request exchanges its JSON documents through,
    /// owned for the length of that request.
    /// </summary>
    internal sealed class TransformWorkerRequestFiles : IDisposable
    {
        private readonly string _directory;

        internal TransformWorkerRequestFiles()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "uloop-hot-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            OutputJsonPath = Path.Combine(_directory, "output.json");
        }

        /// Where the worker is asked to write its output document.
        internal string OutputJsonPath { get; }

        /// <summary>
        /// Writes the request document and returns its path. Why UTF-8 without a BOM: the worker
        /// reads the file as plain UTF-8, and a byte-order mark would land in the first token.
        /// </summary>
        internal string WriteInputJson(TransformWorkerInputDto input)
        {
            string inputJsonPath = Path.Combine(_directory, "input.json");
            File.WriteAllText(
                inputJsonPath,
                JsonConvert.SerializeObject(input),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return inputJsonPath;
        }

        // Why swallow only these two: a worker killed on the timeout path can still hold the
        // request files open (Windows most of all), and losing a decided result to a cleanup
        // error would be worse than leaving a temp directory behind for the OS to reap.
        public void Dispose()
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException ex)
            {
                LogCleanupFailure(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogCleanupFailure(ex.Message);
            }
        }

        private void LogCleanupFailure(string reason)
        {
            VibeLogger.LogWarning(
                HotReloadConstants.VibeLogWorkerHostTempCleanupFailed,
                "Transform worker request files could not be deleted.",
                new { path = _directory, reason });
        }
    }
}
