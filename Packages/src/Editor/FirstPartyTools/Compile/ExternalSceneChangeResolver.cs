using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves external Scene file changes according to the compile request policy.
    /// </summary>
    internal sealed class ExternalSceneChangeResolver
    {
        private const string StopOnExternalSceneChangesOption = "--stop-on-external-scene-changes";
        private readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> _snapshots;
        private readonly Func<(string AssetPath, bool IsDirty)[]> _getOpenScenes;
        private readonly Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> _readFileFingerprint;
        private readonly Func<string[]> _saveDirtyOpenScenesBeforeReload;
        private readonly Func<bool> _reloadOpenSceneSetup;

        public ExternalSceneChangeResolver(
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            Func<(string AssetPath, bool IsDirty)[]> getOpenScenes,
            Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> readFileFingerprint,
            Func<string[]> saveDirtyOpenScenesBeforeReload,
            Func<bool> reloadOpenSceneSetup)
        {
            Debug.Assert(snapshots != null, "snapshots must not be null");
            Debug.Assert(getOpenScenes != null, "getOpenScenes must not be null");
            Debug.Assert(readFileFingerprint != null, "readFileFingerprint must not be null");
            Debug.Assert(saveDirtyOpenScenesBeforeReload != null, "saveDirtyOpenScenesBeforeReload must not be null");
            Debug.Assert(reloadOpenSceneSetup != null, "reloadOpenSceneSetup must not be null");

            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _getOpenScenes = getOpenScenes ?? throw new ArgumentNullException(nameof(getOpenScenes));
            _readFileFingerprint = readFileFingerprint ?? throw new ArgumentNullException(nameof(readFileFingerprint));
            _saveDirtyOpenScenesBeforeReload = saveDirtyOpenScenesBeforeReload ??
                                               throw new ArgumentNullException(nameof(saveDirtyOpenScenesBeforeReload));
            _reloadOpenSceneSetup = reloadOpenSceneSetup ?? throw new ArgumentNullException(nameof(reloadOpenSceneSetup));
        }

        public (bool CanProceed, string Message, string[] ScenePaths) ResolveExternalSceneChanges(
            bool reloadExternalSceneChanges)
        {
            (string AssetPath, bool IsDirty)[] openScenes = _getOpenScenes();
            List<string> changedScenePaths = new List<string>();
            List<string> unresolvedScenePaths = new List<string>();
            bool shouldReloadSceneSetup = false;

            for (int i = 0; i < openScenes.Length; i++)
            {
                (string AssetPath, bool IsDirty) scene = openScenes[i];
                (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                    _readFileFingerprint(scene.AssetPath);
                if (!_snapshots.ContainsKey(scene.AssetPath))
                {
                    _snapshots[scene.AssetPath] = currentFingerprint;
                    continue;
                }

                if (ExternalAssetFileStateComparer.HasSameFileState(
                    _snapshots[scene.AssetPath], currentFingerprint))
                {
                    continue;
                }

                changedScenePaths.Add(scene.AssetPath);
                if (!reloadExternalSceneChanges)
                {
                    continue;
                }

                if (!currentFingerprint.Exists)
                {
                    unresolvedScenePaths.Add(scene.AssetPath);
                    continue;
                }

                if (scene.IsDirty)
                {
                    unresolvedScenePaths.Add(scene.AssetPath);
                    continue;
                }

                shouldReloadSceneSetup = true;
            }

            if (changedScenePaths.Count > 0 && !reloadExternalSceneChanges)
            {
                return (false, CreateStoppedMessage(changedScenePaths.ToArray()), changedScenePaths.ToArray());
            }

            if (unresolvedScenePaths.Count > 0)
            {
                return (false, CreateUnresolvedMessage(unresolvedScenePaths.ToArray()), unresolvedScenePaths.ToArray());
            }

            if (shouldReloadSceneSetup)
            {
                string[] dirtySceneSaveFailures = _saveDirtyOpenScenesBeforeReload();
                Debug.Assert(dirtySceneSaveFailures != null, "dirty Scene save failures must not be null");
                if (dirtySceneSaveFailures.Length > 0)
                {
                    return (false, CreateDirtySaveBeforeReloadFailureMessage(dirtySceneSaveFailures), dirtySceneSaveFailures);
                }
            }

            if (shouldReloadSceneSetup && !_reloadOpenSceneSetup())
            {
                return (false, CreateReloadFailureMessage(changedScenePaths.ToArray()), changedScenePaths.ToArray());
            }

            return (true, null, Array.Empty<string>());
        }

        private static string CreateStoppedMessage(string[] scenePaths)
        {
            return "Compilation stopped because open Scene files changed externally. " +
                   "External Scene changes: " +
                   FormatScenePaths(scenePaths) +
                   $". Rerun without {StopOnExternalSceneChangesOption} to reload or save them automatically.";
        }

        private static string CreateUnresolvedMessage(string[] scenePaths)
        {
            return "Compilation cannot resolve externally changed Scene files before compile. " +
                   "Scenes that could not be safely saved or reloaded: " +
                   FormatScenePaths(scenePaths) +
                   ". Dirty Scenes changed externally are not overwritten automatically.";
        }

        private static string CreateReloadFailureMessage(string[] scenePaths)
        {
            return "Compilation cannot reload externally changed Scene files before compile. " +
                   "Scenes that could not be reloaded: " +
                   FormatScenePaths(scenePaths) +
                   ".";
        }

        private static string CreateDirtySaveBeforeReloadFailureMessage(string[] scenePaths)
        {
            return "Compilation cannot save dirty Scene files before reloading externally changed Scene files. " +
                   "Dirty Scenes that could not be saved: " +
                   FormatScenePaths(scenePaths) +
                   ".";
        }

        private static string FormatScenePaths(string[] scenePaths)
        {
            Debug.Assert(scenePaths != null, "scenePaths must not be null");
            Debug.Assert(scenePaths.Length > 0, "scenePaths must not be empty");

            string[] displayPaths = new string[scenePaths.Length];
            for (int i = 0; i < scenePaths.Length; i++)
            {
                displayPaths[i] = "Scene: " + scenePaths[i];
            }

            return string.Join(", ", displayPaths);
        }
    }
}
