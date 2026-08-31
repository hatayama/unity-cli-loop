using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tracks the current Prefab Stage asset snapshot so compile and focus-return can resolve
    /// external disk changes to the open Prefab, mirroring ExternalSceneChangeTracker's Scene tracking.
    /// </summary>
    internal static class ExternalPrefabStageChangeTracker
    {
        private const string PrefabStageSnapshotsSessionStateKey =
            "io.github.hatayama.UnityCliLoop.ExternalSceneChangeTracker.PrefabStageSnapshots";
        private static readonly Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> PrefabStageSnapshots =
            new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);

        internal static void RegisterEventHandlers()
        {
            PrefabStage.prefabStageOpened -= HandlePrefabStageOpened;
            PrefabStage.prefabStageOpened += HandlePrefabStageOpened;
            PrefabStage.prefabStageClosing -= HandlePrefabStageClosing;
            PrefabStage.prefabStageClosing += HandlePrefabStageClosing;
            PrefabStage.prefabSaved -= HandlePrefabSaved;
            PrefabStage.prefabSaved += HandlePrefabSaved;
        }

        internal static void RestoreFromSessionState()
        {
            ExternalAssetSnapshotSessionStore.RestoreSnapshots(
                PrefabStageSnapshots,
                SessionState.GetString(PrefabStageSnapshotsSessionStateKey, ""));
        }

        internal static void RecordCurrent()
        {
            Record(PrefabStageUtility.GetCurrentPrefabStage());
        }

        private static void HandlePrefabStageOpened(PrefabStage prefabStage)
        {
            Record(prefabStage);
        }

        private static void HandlePrefabStageClosing(PrefabStage prefabStage)
        {
            if (!IsTrackable(prefabStage))
            {
                return;
            }

            PrefabStageSnapshots.Remove(ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath));
            SaveToSessionState();
        }

        private static void HandlePrefabSaved(GameObject prefabRoot)
        {
            RecordCurrent();
        }

        private static void Record(PrefabStage prefabStage)
        {
            if (!IsTrackable(prefabStage))
            {
                return;
            }

            string assetPath = ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath);
            PrefabStageSnapshots[assetPath] = ExternalSceneChangeTracker.ReadAssetFileFingerprint(assetPath);
            SaveToSessionState();
        }

        private static bool IsTrackable(PrefabStage prefabStage)
        {
            return prefabStage != null &&
                   prefabStage.scene.IsValid() &&
                   !string.IsNullOrEmpty(prefabStage.assetPath) &&
                   prefabStage.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reopens the current Prefab Stage after Unity reimports its externally changed asset.
        /// </summary>
        internal static void ResolveExternalChangeForFocusReturn()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackable(prefabStage))
            {
                return;
            }

            string assetPath = ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath);
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                ExternalSceneChangeTracker.ReadAssetFileFingerprint(assetPath);
            if (!PrefabStageSnapshots.ContainsKey(assetPath))
            {
                PrefabStageSnapshots[assetPath] = currentFingerprint;
                SaveToSessionState();
                return;
            }

            if (ExternalAssetFileStateComparer.HasSameFileState(
                PrefabStageSnapshots[assetPath], currentFingerprint))
            {
                return;
            }

            if (!currentFingerprint.Exists)
            {
                string[] saveFailures = SaveMissingAsset();
                ExternalSceneChangeTracker.LogFocusReturnFailures(
                    "restore the missing Prefab asset from the Unity state", saveFailures);
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

            Record(reopenedStage);
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

        /// <summary>
        /// Saves the current dirty Prefab Stage only when its asset file changed or disappeared on disk.
        /// </summary>
        internal static string[] SaveDirtyIfChangedExternally()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackable(prefabStage) || !prefabStage.scene.isDirty)
            {
                return Array.Empty<string>();
            }

            string assetPath = ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath);
            string[] assetPathsToSave = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: assetPath, IsDirty: true) },
                PrefabStageSnapshots,
                ExternalSceneChangeTracker.ReadAssetFileFingerprint);
            if (assetPathsToSave.Length == 0)
            {
                return Array.Empty<string>();
            }

            if (TrySave(prefabStage))
            {
                return Array.Empty<string>();
            }

            return new[] { GetDisplayPath(prefabStage) };
        }

        internal static string[] SaveMissingAsset()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsTrackable(prefabStage))
            {
                return Array.Empty<string>();
            }

            string assetPath = ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath);
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint =
                ExternalSceneChangeTracker.ReadAssetFileFingerprint(assetPath);
            if (currentFingerprint.Exists)
            {
                return Array.Empty<string>();
            }

            if (TrySave(prefabStage))
            {
                return Array.Empty<string>();
            }

            return new[] { GetDisplayPath(prefabStage) };
        }

        private static bool TrySave(PrefabStage prefabStage)
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
            Record(prefabStage);
            return true;
        }

        private static void SaveToSessionState()
        {
            SessionState.SetString(
                PrefabStageSnapshotsSessionStateKey,
                ExternalAssetSnapshotSessionStore.SerializeSnapshots(PrefabStageSnapshots));
        }

        private static string GetDisplayPath(PrefabStage prefabStage)
        {
            Debug.Assert(prefabStage != null, "prefabStage must not be null");

            if (!string.IsNullOrEmpty(prefabStage.assetPath))
            {
                return ExternalSceneChangeTracker.NormalizeAssetPath(prefabStage.assetPath);
            }

            return ExternalSceneChangeTracker.GetSceneDisplayPath(prefabStage.scene);
        }
    }
}
