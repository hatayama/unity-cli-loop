using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unity's process-wide log stack trace settings, plus the copy that outlives a Domain Reload.
    /// </summary>
    internal interface IRunTestsLogStackTraceSettings
    {
        StackTraceLogType Get(LogType logType);
        void Set(LogType logType, StackTraceLogType stackTraceLogType);

        /// <summary>
        /// Returns the saved values, indexed by position in <see cref="LogType"/>, or an empty array.
        /// </summary>
        int[] LoadSaved();

        void Save(int[] savedStackTraceLogTypes);
        void ClearSaved();
    }

    /// <summary>
    /// Turns Unity log stack traces off for a test run and puts back what the project had.
    /// </summary>
    internal sealed class RunTestsLogStackTraceSuppressor
    {
        private static readonly LogType[] AllLogTypes = (LogType[])Enum.GetValues(typeof(LogType));

        private readonly IRunTestsLogStackTraceSettings _settings;

        internal RunTestsLogStackTraceSuppressor(IRunTestsLogStackTraceSettings settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            _settings = settings;
        }

        internal static RunTestsLogStackTraceSuppressor CreateDefault()
        {
            return new RunTestsLogStackTraceSuppressor(new RunTestsUnityLogStackTraceSettings());
        }

        internal void Suppress()
        {
            // Why restore first: if an earlier restore never ran, None is still in place, and saving that
            // would make None the project's own value for the rest of the session.
            RestoreIfSaved();

            int[] original = new int[AllLogTypes.Length];
            for (int index = 0; index < AllLogTypes.Length; index++)
            {
                original[index] = (int)_settings.Get(AllLogTypes[index]);
            }

            // Why save before changing: if this stops partway, the next restore still has the originals.
            _settings.Save(original);
            foreach (LogType logType in AllLogTypes)
            {
                _settings.Set(logType, StackTraceLogType.None);
            }
        }

        internal void RestoreIfSaved()
        {
            int[] saved = _settings.LoadSaved();
            if (saved.Length == 0)
            {
                return;
            }

            Debug.Assert(saved.Length == AllLogTypes.Length, "saved settings must have one value per log type");
            for (int index = 0; index < AllLogTypes.Length; index++)
            {
                _settings.Set(AllLogTypes[index], (StackTraceLogType)saved[index]);
            }

            _settings.ClearSaved();
        }
    }

    /// <summary>
    /// Reads and writes the real settings. SessionState fits the saved copy because both outlive a
    /// Domain Reload and both end with the Editor process.
    /// </summary>
    internal sealed class RunTestsUnityLogStackTraceSettings : IRunTestsLogStackTraceSettings
    {
        private const string SavedSettingsKey = "UnityCliLoop.RunTests.SavedLogStackTraceTypes";

        public StackTraceLogType Get(LogType logType)
        {
            return Application.GetStackTraceLogType(logType);
        }

        public void Set(LogType logType, StackTraceLogType stackTraceLogType)
        {
            Application.SetStackTraceLogType(logType, stackTraceLogType);
        }

        public int[] LoadSaved()
        {
            return SessionState.GetIntArray(SavedSettingsKey, Array.Empty<int>());
        }

        public void Save(int[] savedStackTraceLogTypes)
        {
            SessionState.SetIntArray(SavedSettingsKey, savedStackTraceLogTypes);
        }

        public void ClearSaved()
        {
            SessionState.EraseIntArray(SavedSettingsKey);
        }
    }
}
