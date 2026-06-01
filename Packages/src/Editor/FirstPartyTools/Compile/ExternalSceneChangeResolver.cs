using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tracks open Scene file snapshots so compile can resolve external disk changes before asset refresh.
    /// </summary>
    internal static class ExternalSceneChangeTracker
    {
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> Snapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorSceneManager.sceneSaved -= HandleSceneSaved;
            EditorSceneManager.sceneSaved += HandleSceneSaved;
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
            EditorSceneManager.sceneClosed += HandleSceneClosed;
            RecordOpenSceneSnapshots();
        }

        public static (bool CanProceed, string Message, string[] ScenePaths) ResolveForCompile(
            bool reloadExternalSceneChanges)
        {
            Initialize();
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                Snapshots,
                GetOpenSceneStates,
                ReadSceneFileFingerprint,
                SaveSceneByPath,
                ReloadOpenSceneSetup);
            return resolver.ResolveExternalSceneChanges(reloadExternalSceneChanges);
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            RecordSceneSnapshot(scene);
        }

        private static void HandleSceneSaved(Scene scene)
        {
            RecordSceneSnapshot(scene);
        }

        private static void HandleSceneClosed(Scene scene)
        {
            if (!IsTrackableScene(scene))
            {
                return;
            }

            Snapshots.Remove(NormalizeAssetPath(scene.path));
        }

        private static void RecordOpenSceneSnapshots()
        {
            (string AssetPath, bool IsDirty)[] scenes = GetOpenSceneStates();
            for (int i = 0; i < scenes.Length; i++)
            {
                Snapshots[scenes[i].AssetPath] = ReadSceneFileFingerprint(scenes[i].AssetPath);
            }
        }

        private static void RecordSceneSnapshot(Scene scene)
        {
            if (!IsTrackableScene(scene))
            {
                return;
            }

            string assetPath = NormalizeAssetPath(scene.path);
            Snapshots[assetPath] = ReadSceneFileFingerprint(assetPath);
        }

        private static (string AssetPath, bool IsDirty)[] GetOpenSceneStates()
        {
            List<(string AssetPath, bool IsDirty)> scenes = new List<(string AssetPath, bool IsDirty)>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!IsTrackableScene(scene))
                {
                    continue;
                }

                scenes.Add((NormalizeAssetPath(scene.path), scene.isDirty));
            }

            return scenes.ToArray();
        }

        private static bool IsTrackableScene(Scene scene)
        {
            return scene.IsValid() &&
                   scene.isLoaded &&
                   !string.IsNullOrEmpty(scene.path) &&
                   scene.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool Exists, DateTime LastWriteTimeUtc, long Length) ReadSceneFileFingerprint(
            string assetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            FileInfo fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return (false, DateTime.MinValue, 0);
            }

            return (true, fileInfo.LastWriteTimeUtc, fileInfo.Length);
        }

        private static bool SaveSceneByPath(string assetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty");

            Scene scene = SceneManager.GetSceneByPath(assetPath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            return EditorSceneManager.SaveScene(scene);
        }

        private static bool ReloadOpenSceneSetup()
        {
            SceneSetup[] sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            if (sceneSetup == null || sceneSetup.Length == 0)
            {
                return true;
            }

            EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            RecordOpenSceneSnapshots();
            return true;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty");
            return assetPath.Replace('\\', '/');
        }
    }

    /// <summary>
    /// Resolves external Scene file changes according to the compile request policy.
    /// </summary>
    internal sealed class ExternalSceneChangeResolver
    {
        private const string StopOnExternalSceneChangesOption = "--stop-on-external-scene-changes";
        private readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> _snapshots;
        private readonly Func<(string AssetPath, bool IsDirty)[]> _getOpenScenes;
        private readonly Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> _readFileFingerprint;
        private readonly Func<string, bool> _saveScene;
        private readonly Func<bool> _reloadOpenSceneSetup;

        public ExternalSceneChangeResolver(
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            Func<(string AssetPath, bool IsDirty)[]> getOpenScenes,
            Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> readFileFingerprint,
            Func<string, bool> saveScene,
            Func<bool> reloadOpenSceneSetup)
        {
            Debug.Assert(snapshots != null, "snapshots must not be null");
            Debug.Assert(getOpenScenes != null, "getOpenScenes must not be null");
            Debug.Assert(readFileFingerprint != null, "readFileFingerprint must not be null");
            Debug.Assert(saveScene != null, "saveScene must not be null");
            Debug.Assert(reloadOpenSceneSetup != null, "reloadOpenSceneSetup must not be null");

            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _getOpenScenes = getOpenScenes ?? throw new ArgumentNullException(nameof(getOpenScenes));
            _readFileFingerprint = readFileFingerprint ?? throw new ArgumentNullException(nameof(readFileFingerprint));
            _saveScene = saveScene ?? throw new ArgumentNullException(nameof(saveScene));
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

                if (HasSameFileState(_snapshots[scene.AssetPath], currentFingerprint))
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
                    if (!_saveScene(scene.AssetPath))
                    {
                        unresolvedScenePaths.Add(scene.AssetPath);
                        continue;
                    }

                    _snapshots[scene.AssetPath] = _readFileFingerprint(scene.AssetPath);
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

            if (shouldReloadSceneSetup && !_reloadOpenSceneSetup())
            {
                return (false, CreateReloadFailureMessage(changedScenePaths.ToArray()), changedScenePaths.ToArray());
            }

            return (true, null, Array.Empty<string>());
        }

        private static bool HasSameFileState(
            (bool Exists, DateTime LastWriteTimeUtc, long Length) previousFingerprint,
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint)
        {
            return previousFingerprint.Exists == currentFingerprint.Exists &&
                   previousFingerprint.LastWriteTimeUtc == currentFingerprint.LastWriteTimeUtc &&
                   previousFingerprint.Length == currentFingerprint.Length;
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
                   "Scenes that could not be saved or reloaded: " +
                   FormatScenePaths(scenePaths) +
                   ".";
        }

        private static string CreateReloadFailureMessage(string[] scenePaths)
        {
            return "Compilation cannot reload externally changed Scene files before compile. " +
                   "Scenes that could not be reloaded: " +
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
