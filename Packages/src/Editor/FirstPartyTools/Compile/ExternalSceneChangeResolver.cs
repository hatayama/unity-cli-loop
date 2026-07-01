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
        private const string AutoRefreshHeldSessionStateKey =
            "io.github.hatayama.UnityCliLoop.ExternalSceneChangeTracker.AutoRefreshHeld";
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> SceneSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> PrefabStageSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
        private static readonly ExternalAssetFocusReturnService FocusReturnService =
            new ExternalAssetFocusReturnService(
                () => SessionState.GetBool(AutoRefreshHeldSessionStateKey, false),
                isHeld => SessionState.SetBool(AutoRefreshHeldSessionStateKey, isHeld),
                () => EditorApplication.isFocused,
                AssetDatabase.DisallowAutoRefresh,
                AssetDatabase.AllowAutoRefresh,
                ResolveForFocusReturn);
        private static bool _initialized;

        public static void Initialize()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            if (_initialized)
            {
                return;
            }

            FocusReturnService.RestoreAutoRefreshIfHeld();

            _initialized = true;
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorSceneManager.sceneSaved -= HandleSceneSaved;
            EditorSceneManager.sceneSaved += HandleSceneSaved;
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
            EditorSceneManager.sceneClosed += HandleSceneClosed;
            PrefabStage.prefabStageOpened -= HandlePrefabStageOpened;
            PrefabStage.prefabStageOpened += HandlePrefabStageOpened;
            PrefabStage.prefabStageClosing -= HandlePrefabStageClosing;
            PrefabStage.prefabStageClosing += HandlePrefabStageClosing;
            PrefabStage.prefabSaved -= HandlePrefabSaved;
            PrefabStage.prefabSaved += HandlePrefabSaved;
            EditorApplication.focusChanged -= HandleFocusChanged;
            EditorApplication.focusChanged += HandleFocusChanged;
            RecordOpenSceneSnapshots();
            RecordCurrentPrefabStageSnapshot();
        }

        public static (bool CanProceed, string Message, string[] ScenePaths) ResolveForCompile(
            bool reloadExternalSceneChanges)
        {
            Initialize();
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                SceneSnapshots,
                GetOpenSceneStates,
                ReadAssetFileFingerprint,
                SaveDirtyOpenScenesBeforeReload,
                ReloadOpenSceneSetup);
            return resolver.ResolveExternalSceneChanges(reloadExternalSceneChanges);
        }

        private static void HandleFocusChanged(bool isFocused)
        {
            FocusReturnService.HandleFocusChanged(isFocused);
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

            SceneSnapshots.Remove(NormalizeAssetPath(scene.path));
        }

        private static void HandlePrefabStageOpened(PrefabStage prefabStage)
        {
            RecordPrefabStageSnapshot(prefabStage);
        }

        private static void HandlePrefabStageClosing(PrefabStage prefabStage)
        {
            if (!IsTrackablePrefabStage(prefabStage))
            {
                return;
            }

            PrefabStageSnapshots.Remove(NormalizeAssetPath(prefabStage.assetPath));
        }

        private static void HandlePrefabSaved(GameObject prefabRoot)
        {
            RecordCurrentPrefabStageSnapshot();
        }

        private static void ResolveForFocusReturn()
        {
            string[] dirtySceneSaveFailures = SaveDirtyOpenScenesBeforeReload();
            LogFocusReturnFailures("save dirty Scene files", dirtySceneSaveFailures);

            string[] missingSceneSaveFailures = SaveMissingOpenScenesFromUnity();
            LogFocusReturnFailures("restore missing Scene files from the Unity state", missingSceneSaveFailures);

            string[] dirtyPrefabSaveFailures = SaveDirtyCurrentPrefabStage();
            LogFocusReturnFailures("save the dirty Prefab Stage", dirtyPrefabSaveFailures);

            string[] missingPrefabSaveFailures = SaveMissingCurrentPrefabStageAsset();
            LogFocusReturnFailures("restore the missing Prefab asset from the Unity state", missingPrefabSaveFailures);

            ResolveSceneExternalChangesForFocusReturn();
            if (dirtyPrefabSaveFailures.Length > 0 ||
                missingPrefabSaveFailures.Length > 0 ||
                IsCurrentPrefabStageDirty())
            {
                Debug.LogWarning(
                    "Unity CLI Loop skipped Prefab Stage external-change reload because the current Prefab Stage is still dirty or could not be saved.");
                return;
            }

            ResolveCurrentPrefabStageExternalChangeForFocusReturn();
        }

        private static void RecordOpenSceneSnapshots()
        {
            (string AssetPath, bool IsDirty)[] scenes = GetOpenSceneStates();
            for (int i = 0; i < scenes.Length; i++)
            {
                SceneSnapshots[scenes[i].AssetPath] = ReadAssetFileFingerprint(scenes[i].AssetPath);
            }
        }

        private static void RecordSceneSnapshot(Scene scene)
        {
            if (!IsTrackableScene(scene))
            {
                return;
            }

            string assetPath = NormalizeAssetPath(scene.path);
            SceneSnapshots[assetPath] = ReadAssetFileFingerprint(assetPath);
        }

        private static void RecordCurrentPrefabStageSnapshot()
        {
            RecordPrefabStageSnapshot(PrefabStageUtility.GetCurrentPrefabStage());
        }

        private static void RecordPrefabStageSnapshot(PrefabStage prefabStage)
        {
            if (!IsTrackablePrefabStage(prefabStage))
            {
                return;
            }

            string assetPath = NormalizeAssetPath(prefabStage.assetPath);
            PrefabStageSnapshots[assetPath] = ReadAssetFileFingerprint(assetPath);
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

        private static bool IsTrackablePrefabStage(PrefabStage prefabStage)
        {
            return prefabStage != null &&
                   prefabStage.scene.IsValid() &&
                   !string.IsNullOrEmpty(prefabStage.assetPath) &&
                   prefabStage.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool Exists, DateTime LastWriteTimeUtc, long Length) ReadAssetFileFingerprint(
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

        private static void ResolveSceneExternalChangesForFocusReturn()
        {
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                SceneSnapshots,
                GetOpenSceneStates,
                ReadAssetFileFingerprint,
                SaveDirtyOpenScenesBeforeReload,
                ReloadOpenSceneSetup);
            (bool CanProceed, string Message, string[] ScenePaths) result =
                resolver.ResolveExternalSceneChanges(reloadExternalSceneChanges: true);
            if (result.CanProceed)
            {
                return;
            }

            Debug.LogWarning("Unity CLI Loop could not resolve external Scene changes on focus return. " +
                             result.Message);
        }

        private static void ResolveCurrentPrefabStageExternalChangeForFocusReturn()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackablePrefabStage(prefabStage))
            {
                return;
            }

            string assetPath = NormalizeAssetPath(prefabStage.assetPath);
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                ReadAssetFileFingerprint(assetPath);
            if (!PrefabStageSnapshots.ContainsKey(assetPath))
            {
                PrefabStageSnapshots[assetPath] = currentFingerprint;
                return;
            }

            if (HasSameFileState(PrefabStageSnapshots[assetPath], currentFingerprint))
            {
                return;
            }

            if (!currentFingerprint.Exists)
            {
                string[] saveFailures = SaveMissingCurrentPrefabStageAsset();
                LogFocusReturnFailures("restore the missing Prefab asset from the Unity state", saveFailures);
                return;
            }

            AssetDatabase.ImportAsset(assetPath);
            PrefabStage reopenedStage = PrefabStageUtility.OpenPrefab(assetPath);
            if (reopenedStage == null)
            {
                Debug.LogWarning("Unity CLI Loop could not reopen externally changed Prefab asset on focus return. " +
                                 "Prefab Stage: " + assetPath);
                return;
            }

            RecordPrefabStageSnapshot(reopenedStage);
        }

        private static string[] SaveDirtyOpenScenesBeforeReload()
        {
            List<string> failedScenePaths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path) || !EditorSceneManager.SaveScene(scene))
                {
                    failedScenePaths.Add(GetSceneDisplayPath(scene));
                    continue;
                }

                RecordSceneSnapshot(scene);
            }

            return failedScenePaths.ToArray();
        }

        private static string[] SaveMissingOpenScenesFromUnity()
        {
            List<string> failedScenePaths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!IsTrackableScene(scene))
                {
                    continue;
                }

                string assetPath = NormalizeAssetPath(scene.path);
                (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                    ReadAssetFileFingerprint(assetPath);
                if (currentFingerprint.Exists)
                {
                    continue;
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    failedScenePaths.Add(GetSceneDisplayPath(scene));
                    continue;
                }

                RecordSceneSnapshot(scene);
            }

            return failedScenePaths.ToArray();
        }

        private static string[] SaveDirtyCurrentPrefabStage()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackablePrefabStage(prefabStage) || !prefabStage.scene.isDirty)
            {
                return Array.Empty<string>();
            }

            if (TrySavePrefabStage(prefabStage))
            {
                return Array.Empty<string>();
            }

            return new[] { GetPrefabStageDisplayPath(prefabStage) };
        }

        private static string[] SaveMissingCurrentPrefabStageAsset()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackablePrefabStage(prefabStage))
            {
                return Array.Empty<string>();
            }

            string assetPath = NormalizeAssetPath(prefabStage.assetPath);
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                ReadAssetFileFingerprint(assetPath);
            if (currentFingerprint.Exists)
            {
                return Array.Empty<string>();
            }

            if (TrySavePrefabStage(prefabStage))
            {
                return Array.Empty<string>();
            }

            return new[] { GetPrefabStageDisplayPath(prefabStage) };
        }

        private static bool IsCurrentPrefabStageDirty()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            return IsTrackablePrefabStage(prefabStage) && prefabStage.scene.isDirty;
        }

        private static bool TrySavePrefabStage(PrefabStage prefabStage)
        {
            Debug.Assert(prefabStage != null, "prefabStage must not be null");

            if (string.IsNullOrEmpty(prefabStage.assetPath))
            {
                return false;
            }

            bool success;
            PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabStage.assetPath, out success);
            if (!success)
            {
                return false;
            }

            prefabStage.ClearDirtiness();
            RecordPrefabStageSnapshot(prefabStage);
            return true;
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

        private static string GetSceneDisplayPath(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path))
            {
                return NormalizeAssetPath(scene.path);
            }

            if (!string.IsNullOrEmpty(scene.name))
            {
                return scene.name;
            }

            return "Untitled scene";
        }

        private static string GetPrefabStageDisplayPath(PrefabStage prefabStage)
        {
            Debug.Assert(prefabStage != null, "prefabStage must not be null");

            if (!string.IsNullOrEmpty(prefabStage.assetPath))
            {
                return NormalizeAssetPath(prefabStage.assetPath);
            }

            return GetSceneDisplayPath(prefabStage.scene);
        }

        private static void LogFocusReturnFailures(string action, string[] failedAssetPaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(action), "action must not be empty");
            Debug.Assert(failedAssetPaths != null, "failedAssetPaths must not be null");

            if (failedAssetPaths.Length == 0)
            {
                return;
            }

            Debug.LogWarning("Unity CLI Loop could not " + action + " before Unity refreshes assets on focus return. " +
                             "Affected assets: " + string.Join(", ", failedAssetPaths));
        }

        private static bool HasSameFileState(
            (bool Exists, DateTime LastWriteTimeUtc, long Length) previousFingerprint,
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint)
        {
            return previousFingerprint.Exists == currentFingerprint.Exists &&
                   previousFingerprint.LastWriteTimeUtc == currentFingerprint.LastWriteTimeUtc &&
                   previousFingerprint.Length == currentFingerprint.Length;
        }
    }

    /// <summary>
    /// Coordinates Auto Refresh suspension while Unity is unfocused.
    /// </summary>
    internal sealed class ExternalAssetFocusReturnService
    {
        private readonly Func<bool> _getAutoRefreshHeld;
        private readonly Action<bool> _setAutoRefreshHeld;
        private readonly Func<bool> _isEditorFocused;
        private readonly Action _disallowAutoRefresh;
        private readonly Action _allowAutoRefresh;
        private readonly Action _resolveFocusReturnChanges;

        internal ExternalAssetFocusReturnService(
            Func<bool> getAutoRefreshHeld,
            Action<bool> setAutoRefreshHeld,
            Func<bool> isEditorFocused,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Action resolveFocusReturnChanges)
        {
            Debug.Assert(getAutoRefreshHeld != null, "getAutoRefreshHeld must not be null");
            Debug.Assert(setAutoRefreshHeld != null, "setAutoRefreshHeld must not be null");
            Debug.Assert(isEditorFocused != null, "isEditorFocused must not be null");
            Debug.Assert(disallowAutoRefresh != null, "disallowAutoRefresh must not be null");
            Debug.Assert(allowAutoRefresh != null, "allowAutoRefresh must not be null");
            Debug.Assert(resolveFocusReturnChanges != null, "resolveFocusReturnChanges must not be null");

            _getAutoRefreshHeld = getAutoRefreshHeld ?? throw new ArgumentNullException(nameof(getAutoRefreshHeld));
            _setAutoRefreshHeld = setAutoRefreshHeld ?? throw new ArgumentNullException(nameof(setAutoRefreshHeld));
            _isEditorFocused = isEditorFocused ?? throw new ArgumentNullException(nameof(isEditorFocused));
            _disallowAutoRefresh = disallowAutoRefresh ?? throw new ArgumentNullException(nameof(disallowAutoRefresh));
            _allowAutoRefresh = allowAutoRefresh ?? throw new ArgumentNullException(nameof(allowAutoRefresh));
            _resolveFocusReturnChanges =
                resolveFocusReturnChanges ?? throw new ArgumentNullException(nameof(resolveFocusReturnChanges));
        }

        internal void RestoreAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return;
            }

            if (!_isEditorFocused())
            {
                return;
            }

            HandleFocusChanged(true);
        }

        internal void HandleFocusChanged(bool isFocused)
        {
            if (!isFocused)
            {
                HoldAutoRefreshIfNeeded();
                return;
            }

            try
            {
                _resolveFocusReturnChanges();
            }
            finally
            {
                ReleaseAutoRefreshIfHeld();
            }
        }

        private void HoldAutoRefreshIfNeeded()
        {
            if (_getAutoRefreshHeld())
            {
                return;
            }

            _disallowAutoRefresh();
            _setAutoRefreshHeld(true);
        }

        private void ReleaseAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return;
            }

            _allowAutoRefresh();
            _setAutoRefreshHeld(false);
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
