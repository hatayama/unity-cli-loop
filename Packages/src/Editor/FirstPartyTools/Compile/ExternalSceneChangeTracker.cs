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
        private const string SceneSnapshotsSessionStateKey =
            "io.github.hatayama.UnityCliLoop.ExternalSceneChangeTracker.SceneSnapshots";
        private const string PrefabStageSnapshotsSessionStateKey =
            "io.github.hatayama.UnityCliLoop.ExternalSceneChangeTracker.PrefabStageSnapshots";
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> SceneSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> PrefabStageSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
        private static readonly ExternalAssetFocusReturnService FocusReturnService =
            new ExternalAssetFocusReturnService(
                IsAutoRefreshHeld,
                SetAutoRefreshHeld,
                () => EditorApplication.isFocused,
                AssetDatabase.DisallowAutoRefresh,
                AssetDatabase.AllowAutoRefresh,
                ResolveForFocusReturn);
        // Why throttle: reconcile must not call Disallow/Allow every frame when already aligned.
        private const double AutoRefreshReconcileIntervalSeconds = 0.5d;
        private static bool _initialized;
        private static double _nextAutoRefreshReconcileTime;

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

            RestoreSnapshotsFromSessionState();
            bool restoredHeldAutoRefresh = FocusReturnService.RestoreAutoRefreshIfHeld();

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
            EditorApplication.update -= ReconcileAutoRefreshHoldOnUpdate;
            EditorApplication.update += ReconcileAutoRefreshHoldOnUpdate;
            // Why record before Hold: fingerprints must exist for focus-return resolve after startup Hold.
            if (!restoredHeldAutoRefresh && !IsAutoRefreshHeld())
            {
                RecordOpenSceneSnapshots();
                RecordCurrentPrefabStageSnapshot();
            }

            // Why immediate Hold: background launch never fires focusChanged(false), so Auto Refresh
            // would stay enabled until the first focus and show a native external-change dialog.
            FocusReturnService.HoldIfCurrentlyUnfocused();
        }

        private static void ReconcileAutoRefreshHoldOnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextAutoRefreshReconcileTime)
            {
                return;
            }

            _nextAutoRefreshReconcileTime = now + AutoRefreshReconcileIntervalSeconds;
            FocusReturnService.ReconcileAutoRefreshHoldWithFocus();
        }

        public static (bool CanProceed, string Message, string[] ScenePaths) ResolveForCompile(
            bool reloadExternalSceneChanges)
        {
            Initialize();
            ExternalSceneChangeResolver resolver = CreateSceneChangeResolver();
            (bool CanProceed, string Message, string[] ScenePaths) result =
                resolver.ResolveExternalSceneChanges(reloadExternalSceneChanges);
            SaveSceneSnapshotsToSessionState();
            return result;
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
            SaveSceneSnapshotsToSessionState();
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
            SavePrefabStageSnapshotsToSessionState();
        }

        private static void HandlePrefabSaved(GameObject prefabRoot)
        {
            RecordCurrentPrefabStageSnapshot();
        }

        private static void ResolveForFocusReturn()
        {
            // Focus return treats Unity's in-memory editor state as authoritative because source-control
            // operations can replace files while Unity is unfocused and would otherwise trigger reload dialogs.
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
            SceneSnapshots.Clear();
            (string AssetPath, bool IsDirty)[] scenes = GetOpenSceneStates();
            for (int i = 0; i < scenes.Length; i++)
            {
                SceneSnapshots[scenes[i].AssetPath] = ReadAssetFileFingerprint(scenes[i].AssetPath);
            }

            SaveSceneSnapshotsToSessionState();
        }

        private static void RecordSceneSnapshot(Scene scene)
        {
            if (RecordSceneSnapshotIfTrackable(scene))
            {
                SaveSceneSnapshotsToSessionState();
            }
        }

        private static bool RecordSceneSnapshotIfTrackable(Scene scene)
        {
            if (!IsTrackableScene(scene))
            {
                return false;
            }

            string assetPath = NormalizeAssetPath(scene.path);
            SceneSnapshots[assetPath] = ReadAssetFileFingerprint(assetPath);
            return true;
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
            SavePrefabStageSnapshotsToSessionState();
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
            ExternalSceneChangeResolver resolver = CreateSceneChangeResolver();
            (bool CanProceed, string Message, string[] ScenePaths) result =
                resolver.ResolveExternalSceneChanges(reloadExternalSceneChanges: true);
            SaveSceneSnapshotsToSessionState();
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
                SavePrefabStageSnapshotsToSessionState();
                return;
            }

            if (ExternalAssetFileStateComparer.HasSameFileState(
                PrefabStageSnapshots[assetPath], currentFingerprint))
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
            UnityEngine.Object prefabAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (prefabAsset == null)
            {
                Debug.LogWarning("Unity CLI Loop could not reopen externally changed Prefab asset on focus return. " +
                                 "Prefab Stage: " + assetPath);
                return;
            }

            (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) reopenContext =
                CreatePrefabStageReopenContext(
                    prefabStage.openedFromInstanceObject,
                    prefabStage.mode,
                    PrefabUtility.IsPartOfPrefabInstance);
            PrefabStage reopenedStage =
                PrefabStageUtility.OpenPrefab(assetPath, reopenContext.OpenedFromInstanceObject, reopenContext.Mode);
            if (reopenedStage == null)
            {
                Debug.LogWarning("Unity CLI Loop could not reopen externally changed Prefab asset on focus return. " +
                                 "Prefab Stage: " + assetPath);
                return;
            }

            RecordPrefabStageSnapshot(reopenedStage);
        }

        internal static (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) CreatePrefabStageReopenContext(
            GameObject openedFromInstanceObject,
            PrefabStage.Mode prefabStageMode,
            Func<GameObject, bool> isPartOfPrefabInstance)
        {
            Debug.Assert(isPartOfPrefabInstance != null, "isPartOfPrefabInstance must not be null");

            if (openedFromInstanceObject == null)
            {
                return (null, PrefabStage.Mode.InIsolation);
            }

            if (isPartOfPrefabInstance(openedFromInstanceObject))
            {
                return (openedFromInstanceObject, prefabStageMode);
            }

            return (null, PrefabStage.Mode.InIsolation);
        }

        private static string[] SaveDirtyOpenScenesBeforeReload()
        {
            List<string> failedScenePaths = new List<string>();
            bool hasRecordedSceneSnapshot = false;
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

                hasRecordedSceneSnapshot = RecordSceneSnapshotIfTrackable(scene) || hasRecordedSceneSnapshot;
            }

            if (hasRecordedSceneSnapshot)
            {
                SaveSceneSnapshotsToSessionState();
            }

            return failedScenePaths.ToArray();
        }

        private static string[] SaveMissingOpenScenesFromUnity()
        {
            List<string> failedScenePaths = new List<string>();
            bool hasRecordedSceneSnapshot = false;
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

                hasRecordedSceneSnapshot = RecordSceneSnapshotIfTrackable(scene) || hasRecordedSceneSnapshot;
            }

            if (hasRecordedSceneSnapshot)
            {
                SaveSceneSnapshotsToSessionState();
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

        private static ExternalSceneChangeResolver CreateSceneChangeResolver()
        {
            return new ExternalSceneChangeResolver(
                SceneSnapshots,
                GetOpenSceneStates,
                ReadAssetFileFingerprint,
                SaveDirtyOpenScenesBeforeReload,
                ReloadOpenSceneSetup);
        }

        private static bool IsAutoRefreshHeld()
        {
            return SessionState.GetBool(AutoRefreshHeldSessionStateKey, false);
        }

        private static void SetAutoRefreshHeld(bool isHeld)
        {
            SessionState.SetBool(AutoRefreshHeldSessionStateKey, isHeld);
        }

        private static void RestoreSnapshotsFromSessionState()
        {
            ExternalAssetSnapshotSessionStore.RestoreSnapshots(
                SceneSnapshots,
                SessionState.GetString(SceneSnapshotsSessionStateKey, ""));
            ExternalAssetSnapshotSessionStore.RestoreSnapshots(
                PrefabStageSnapshots,
                SessionState.GetString(PrefabStageSnapshotsSessionStateKey, ""));
        }

        private static void SaveSceneSnapshotsToSessionState()
        {
            SessionState.SetString(
                SceneSnapshotsSessionStateKey,
                ExternalAssetSnapshotSessionStore.SerializeSnapshots(SceneSnapshots));
        }

        private static void SavePrefabStageSnapshotsToSessionState()
        {
            SessionState.SetString(
                PrefabStageSnapshotsSessionStateKey,
                ExternalAssetSnapshotSessionStore.SerializeSnapshots(PrefabStageSnapshots));
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
    }
}
