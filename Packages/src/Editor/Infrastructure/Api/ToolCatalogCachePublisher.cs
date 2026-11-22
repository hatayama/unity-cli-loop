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
            UTF8Encoding encoding = new UTF8Encoding(false);
            File.WriteAllText(temporaryPath, json, encoding);
            try
            {
                MoveIntoPlace(temporaryPath, cachePath);
            }
            catch (Exception exception) when (IsFileAccessFailure(exception))
            {
                DeleteTemporaryFileQuietly(temporaryPath);
                // Why fall back to writing in place: on Windows the move fails while another process holds
                // the cache open, such as the CLI reading it or Defender scanning it. Writing in place
                // still succeeds there, so the cache does not stay stale for the whole domain.
                // Unlike the move, this write is not atomic: a reload that cuts it short leaves a truncated
                // cache, which the CLI ignores until the next domain rewrites it. Every write took that risk
                // before the move was added.
                File.WriteAllText(cachePath, json, encoding);
            }
        }

        private static bool IsFileAccessFailure(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException;
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
            catch (Exception exception) when (IsFileAccessFailure(exception))
            {
                // Why ignore: a leftover temporary file is overwritten by the next write.
            }
        }
    }
}
