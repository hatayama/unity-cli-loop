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
    /// Tracks open Scene file snapshots so compile and focus return can resolve external disk changes without Unity's native reload dialog.
    /// </summary>
    internal static class ExternalSceneChangeTracker
    {
        private const string SceneSnapshotsSessionStateKey =
            "io.github.hatayama.UnityCliLoop.ExternalSceneChangeTracker.SceneSnapshots";
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> SceneSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
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

            bool restoredSceneSnapshots = !string.IsNullOrEmpty(SessionState.GetString(SceneSnapshotsSessionStateKey, ""));
            RestoreSnapshotsFromSessionState();

            _initialized = true;
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorSceneManager.sceneSaved -= HandleSceneSaved;
            EditorSceneManager.sceneSaved += HandleSceneSaved;
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
            EditorSceneManager.sceneClosed += HandleSceneClosed;
            ExternalPrefabStageChangeTracker.RegisterEventHandlers();
            EditorApplication.focusChanged -= HandleFocusChanged;
            EditorApplication.focusChanged += HandleFocusChanged;
            // Keep restored fingerprints only across a domain reload that happened while unfocused.
            // The next focus-return preflight compares against those fingerprints so it can detect
            // external changes that occurred before the reload. First launch still records a baseline
            // even when the Editor starts unfocused.
            if (EditorApplication.isFocused || !restoredSceneSnapshots)
            {
                RecordOpenSceneSnapshots();
                ExternalPrefabStageChangeTracker.RecordCurrent();
            }
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
            VibeLogger.LogInfo("external_scene_focus_changed", "Editor focus changed", new { isFocused }, includeStackTrace: false);
            if (!isFocused)
            {
                return;
            }
            ResolveForFocusReturn();
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

        private static void ResolveForFocusReturn()
        {
            // Focus return treats Unity's in-memory editor state as authoritative for assets whose file
            // was replaced while Unity was unfocused (source-control operations), because Unity would
            // otherwise raise a reload dialog for them. Dirty assets whose file is unchanged are left
            // unsaved: saving them would silently commit the user's in-progress edits on every focus switch.
            (string AssetPath, bool IsDirty)[] openScenesBefore = GetOpenSceneStates();
            object[] fingerprintDiffsBefore =
                BuildFingerprintDiffContexts(openScenesBefore, SceneSnapshots, ReadAssetFileFingerprint);
            VibeLogger.LogInfo(
                "external_scene_resolve_focus_return",
                "ResolveForFocusReturn started",
                new
                {
                    phase = "start",
                    scenes = openScenesBefore,
                    fingerprintDiffs = fingerprintDiffsBefore
                },
                includeStackTrace: false);

            string[] dirtySceneSaveFailures = SaveDirtyOpenScenesChangedExternally();
            LogFocusReturnFailures("save dirty Scene files that changed on disk", dirtySceneSaveFailures);

            string[] missingSceneSaveFailures = SaveMissingOpenScenesFromUnity();
            LogFocusReturnFailures("restore missing Scene files from the Unity state", missingSceneSaveFailures);

            string[] dirtyPrefabSaveFailures = ExternalPrefabStageChangeTracker.SaveDirtyIfChangedExternally();
            LogFocusReturnFailures("save the dirty Prefab Stage that changed on disk", dirtyPrefabSaveFailures);

            string[] missingPrefabSaveFailures = ExternalPrefabStageChangeTracker.SaveMissingAsset();
            LogFocusReturnFailures("restore the missing Prefab asset from the Unity state", missingPrefabSaveFailures);

            ResolveSceneExternalChangesForFocusReturn();
            // A dirty Prefab Stage that survived the save step is unchanged on disk, so the reload below
            // is a no-op for it; only a failed save leaves a real conflict that reload must not overwrite.
            if (dirtyPrefabSaveFailures.Length > 0 || missingPrefabSaveFailures.Length > 0)
            {
                Debug.LogWarning(
                    "Unity CLI Loop skipped Prefab Stage external-change reload because the current Prefab Stage could not be saved.");
                VibeLogger.LogInfo(
                    "external_scene_resolve_focus_return",
                    "ResolveForFocusReturn finished early (Prefab Stage could not be saved)",
                    new
                    {
                        phase = "end",
                        skippedPrefabReload = true,
                        dirtySceneSaveFailures,
                        missingSceneSaveFailures,
                        dirtyPrefabSaveFailures,
                        missingPrefabSaveFailures
                    },
                    includeStackTrace: false);
                return;
            }

            ExternalPrefabStageChangeTracker.ResolveExternalChangeForFocusReturn();

            (string AssetPath, bool IsDirty)[] openScenesAfter = GetOpenSceneStates();
            VibeLogger.LogInfo(
                "external_scene_resolve_focus_return",
                "ResolveForFocusReturn finished",
                new
                {
                    phase = "end",
                    scenes = openScenesAfter,
                    fingerprintDiffs = BuildFingerprintDiffContexts(openScenesAfter, SceneSnapshots, ReadAssetFileFingerprint),
                    dirtySceneSaveFailures,
                    missingSceneSaveFailures
                },
                includeStackTrace: false);
        }

        /// <summary>
        /// Builds a fingerprint diff snapshot for observability logging, given explicit snapshot state
        /// and a fingerprint reader so the comparison logic itself can be characterized without Unity APIs.
        /// </summary>
        internal static object[] BuildFingerprintDiffContexts(
            (string AssetPath, bool IsDirty)[] scenes,
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> readFingerprint)
        {
            Debug.Assert(scenes != null, "scenes must not be null");
            Debug.Assert(snapshots != null, "snapshots must not be null");
            Debug.Assert(readFingerprint != null, "readFingerprint must not be null");

            List<object> diffs = new List<object>(scenes.Length);
            for (int i = 0; i < scenes.Length; i++)
            {
                string assetPath = scenes[i].AssetPath;
                (bool Exists, DateTime LastWriteTimeUtc, long Length) current = readFingerprint(assetPath);
                bool hasSnapshot = snapshots.TryGetValue(
                    assetPath,
                    out (bool Exists, DateTime LastWriteTimeUtc, long Length) snapshot);
                bool changed = !hasSnapshot ||
                               !ExternalAssetFileStateComparer.HasSameFileState(snapshot, current);
                diffs.Add(new
                {
                    assetPath,
                    isDirty = scenes[i].IsDirty,
                    changed,
                    hasSnapshot,
                    snapshotExists = hasSnapshot && snapshot.Exists,
                    currentExists = current.Exists,
                    snapshotLength = hasSnapshot ? snapshot.Length : 0L,
                    currentLength = current.Length
                });
            }

            return diffs.ToArray();
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

        internal static (bool Exists, DateTime LastWriteTimeUtc, long Length) ReadAssetFileFingerprint(
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

        internal static string[] SaveDirtyOpenScenesChangedExternally()
        {
            string[] scenePathsToSave = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                GetOpenSceneStates(), SceneSnapshots, ReadAssetFileFingerprint);
            List<string> failedScenePaths = new List<string>();
            bool hasRecordedSceneSnapshot = false;
            for (int i = 0; i < scenePathsToSave.Length; i++)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePathsToSave[i]);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    failedScenePaths.Add(scenePathsToSave[i]);
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

        private static void RestoreSnapshotsFromSessionState()
        {
            ExternalAssetSnapshotSessionStore.RestoreSnapshots(
                SceneSnapshots,
                SessionState.GetString(SceneSnapshotsSessionStateKey, ""));
            ExternalPrefabStageChangeTracker.RestoreFromSessionState();
        }

        private static void SaveSceneSnapshotsToSessionState()
        {
            SessionState.SetString(
                SceneSnapshotsSessionStateKey,
                ExternalAssetSnapshotSessionStore.SerializeSnapshots(SceneSnapshots));
        }

        internal static string NormalizeAssetPath(string assetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty");
            return assetPath.Replace('\\', '/');
        }

        internal static string GetSceneDisplayPath(Scene scene)
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

        internal static void LogFocusReturnFailures(string action, string[] failedAssetPaths)
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
