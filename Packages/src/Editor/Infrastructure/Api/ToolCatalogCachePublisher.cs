using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Publishes the tool catalog to .uloop/tools.json after each domain reload so package updates refresh
    /// the CLI cache without a manual sync, and tells commands of the domain when that file is written.
    /// </summary>
    internal sealed class ToolCatalogCachePublisher
    {
        private const string TemporaryFileSuffix = ".tmp";

        private readonly Func<Action, Task> _runInBackground;
        private Task _whenPublished = Task.CompletedTask;

        /// <param name="runInBackground">Starts the given write away from the calling thread and returns
        /// a task that ends with it, faulted when the write throws.</param>
        internal ToolCatalogCachePublisher(Func<Action, Task> runInBackground)
        {
            _runInBackground = runInBackground ?? throw new ArgumentNullException(nameof(runInBackground));
        }

        /// <summary>
        /// Ends once the write started last has ended, whether it wrote, found nothing to change, or failed.
        /// Never faults. Already ended when no write was started.
        /// </summary>
        internal Task WhenPublished => _whenPublished;

        internal static ToolCatalogCachePublisher CreateForEditor()
        {
            return new ToolCatalogCachePublisher(write => Task.Run(write));
        }

        /// <summary>
        /// Reads the catalog and the Editor values the skill scan needs, then writes the cache off the main
        /// thread. Call it on the main thread.
        /// </summary>
        internal void StartPublish(UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            ToolInfo[] tools = GetToolDetailsBridgeCommand.CreateCatalogSnapshotIncludingInternal(toolRegistrarService);
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            SkillSourceScanContext scanContext = SkillSourceScanContext.CaptureForProject(projectRoot);
            string cachePath = Path.Combine(
                projectRoot,
                UnityCliLoopConstants.ULOOP_DIR,
                UnityCliLoopConstants.ULOOP_TOOLS_CACHE_FILE_NAME);
            StartWrite(
                tools,
                () => SkillSourceRootEnumerator.CollectInternalSkillToolNames(scanContext),
                cachePath);
        }

        /// <summary>
        /// Starts the background write of <paramref name="tools"/>, minus those
        /// <paramref name="collectInternalToolNames"/> reports, to <paramref name="cachePath"/>.
        /// </summary>
        internal void StartWrite(
            ToolInfo[] tools,
            Func<HashSet<string>> collectInternalToolNames,
            string cachePath)
        {
            Debug.Assert(tools != null, "tools must not be null");
            Debug.Assert(collectInternalToolNames != null, "collectInternalToolNames must not be null");
            Debug.Assert(!string.IsNullOrEmpty(cachePath), "cachePath must not be null or empty");

            Task write = _runInBackground(() => WriteCache(tools, collectInternalToolNames(), cachePath));
            _whenPublished = ObserveWriteAsync(write);
        }

        private static async Task ObserveWriteAsync(Task write)
        {
            try
            {
                // Why ConfigureAwait(false): the gate must open without waiting for a main-thread tick,
                // because uloop status reads it from a background thread.
                await write.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Why log instead of rethrow: a failed write leaves the previous cache in place, which
                // the CLI can still use; failing every command of the domain for it would be worse.
                Debug.LogException(exception);
            }
        }

        private static void WriteCache(ToolInfo[] tools, HashSet<string> internalToolNames, string cachePath)
        {
            GetToolDetailsResponse response = new GetToolDetailsResponse
            {
                Tools = tools.Where(tool => !internalToolNames.Contains(tool.Name)).ToArray()
            };
            // Why reuse JsonRpcResponseSerializer: tools.json must match the IPC result bytes that uloop sync writes.
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            if (File.Exists(cachePath) && File.ReadAllText(cachePath) == json)
            {
                return;
            }

            string directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Why write beside the cache and then move it into place: the next domain reload can unload
            // this domain while the write runs, and a write cut short in place would leave a truncated
            // cache the CLI cannot parse.
            string temporaryPath = cachePath + TemporaryFileSuffix;
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            try
            {
                MoveIntoPlace(temporaryPath, cachePath);
            }
            catch
            {
                DeleteTemporaryFileQuietly(temporaryPath);
                throw;
            }
        }

        // Why two branches: File.Replace needs an existing destination, and the overwriting
        // File.Move overload is missing from Unity's API compatibility levels.
        private static void MoveIntoPlace(string temporaryPath, string cachePath)
        {
            if (File.Exists(cachePath))
            {
                File.Replace(temporaryPath, cachePath, null);
                return;
            }

            File.Move(temporaryPath, cachePath);
        }

        private static void DeleteTemporaryFileQuietly(string temporaryPath)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Why ignore: the move already failed and is being reported; a leftover temporary file is
                // overwritten by the next write.
            }
            catch (UnauthorizedAccessException)
            {
                // Why ignore: same as above.
            }
        }
    }
}
