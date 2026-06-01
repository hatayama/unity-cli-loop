using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Restores Enter Play Mode settings when DomainReloadDisableScope was interrupted.
    /// </summary>
    internal static class DomainReloadDisableScopeRecovery
    {
        internal static string MarkerFilePathForTests => DomainReloadDisableScopeRecoveryConstants.MarkerFilePath;
        internal static string TempFilePathForTests => DomainReloadDisableScopeRecoveryConstants.TempFilePath;

        [InitializeOnLoadMethod]
        private static void RestorePendingSettingsOnEditorLoad()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            RestoreIfPending();
        }

        /// <summary>
        /// Saves the current Enter Play Mode settings before DomainReloadDisableScope changes them.
        /// </summary>
        internal static void SaveCurrentSettings()
        {
            Debug.Assert(
                !File.Exists(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath),
                "recovery marker must be restored before saving a new marker");

            DomainReloadDisableScopeRecoveryData markerData = new DomainReloadDisableScopeRecoveryData
            {
                originalOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                originalOptions = (int)EditorSettings.enterPlayModeOptions
            };

            SaveMarkerData(markerData);
        }

        /// <summary>
        /// Restores Enter Play Mode settings saved before DomainReloadDisableScope changed them.
        /// </summary>
        internal static void RestoreIfPending()
        {
            DeleteTempFileIfExists();
            if (!File.Exists(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath))
            {
                return;
            }

            DomainReloadDisableScopeRecoveryData markerData = ReadMarkerData();
            EditorSettings.enterPlayModeOptionsEnabled = markerData.originalOptionsEnabled;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)markerData.originalOptions;
            DeleteMarkerFileIfExists();
        }

        /// <summary>
        /// Deletes any saved Enter Play Mode settings pending restore.
        /// </summary>
        internal static void ClearPendingRestoreForTests()
        {
            DeleteTempFileIfExists();
            DeleteMarkerFileIfExists();
        }

        internal static bool HasPendingRestoreForTests()
        {
            return File.Exists(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath);
        }

        internal static DomainReloadDisableScopeRecoveryData ReadMarkerDataForTests()
        {
            return ReadMarkerData();
        }

        private static void SaveMarkerData(DomainReloadDisableScopeRecoveryData markerData)
        {
            Debug.Assert(markerData != null, "markerData must not be null");

            string directoryPath = DomainReloadDisableScopeRecoveryConstants.DirectoryPath;
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            DeleteTempFileIfExists();
            string json = JsonUtility.ToJson(markerData, true);
            File.WriteAllText(DomainReloadDisableScopeRecoveryConstants.TempFilePath, json);
            DeleteMarkerFileIfExists();
            File.Move(
                DomainReloadDisableScopeRecoveryConstants.TempFilePath,
                DomainReloadDisableScopeRecoveryConstants.MarkerFilePath);

            Debug.Assert(
                File.Exists(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath),
                "marker file must exist after save");
        }

        private static DomainReloadDisableScopeRecoveryData ReadMarkerData()
        {
            string json = File.ReadAllText(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath);
            DomainReloadDisableScopeRecoveryData markerData =
                JsonUtility.FromJson<DomainReloadDisableScopeRecoveryData>(json);
            if (markerData == null)
            {
                throw new InvalidOperationException("Domain reload recovery marker must contain valid JSON.");
            }

            return markerData;
        }

        private static void DeleteMarkerFileIfExists()
        {
            if (File.Exists(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath))
            {
                File.Delete(DomainReloadDisableScopeRecoveryConstants.MarkerFilePath);
            }
        }

        private static void DeleteTempFileIfExists()
        {
            if (File.Exists(DomainReloadDisableScopeRecoveryConstants.TempFilePath))
            {
                File.Delete(DomainReloadDisableScopeRecoveryConstants.TempFilePath);
            }
        }
    }

    /// <summary>
    /// Stores the original Enter Play Mode settings needed for interrupted scope recovery.
    /// </summary>
    [Serializable]
    internal sealed class DomainReloadDisableScopeRecoveryData
    {
        public bool originalOptionsEnabled;
        public int originalOptions;
    }

    /// <summary>
    /// Centralizes DomainReloadDisableScope recovery marker paths.
    /// </summary>
    internal static class DomainReloadDisableScopeRecoveryConstants
    {
        internal const string DirectoryPath = "UserSettings";
        internal const string MarkerFileName = "uloop-domain-reload-recovery.json";
        internal const string TempFileName = MarkerFileName + ".tmp";
        internal static readonly string MarkerFilePath = Path.Combine(DirectoryPath, MarkerFileName);
        internal static readonly string TempFilePath = Path.Combine(DirectoryPath, TempFileName);
    }
}
