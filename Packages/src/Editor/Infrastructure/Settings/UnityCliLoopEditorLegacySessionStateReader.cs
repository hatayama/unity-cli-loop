using System.IO;
using System.Security;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal interface IUnityCliLoopEditorLegacySessionStateReader
    {
        UnityCliLoopEditorLegacySessionState Read();
        void Clear();
    }

    internal readonly struct UnityCliLoopEditorLegacySessionState
    {
        internal UnityCliLoopEditorLegacySessionState(
            bool isServerRunning,
            bool isAfterCompile,
            bool isDomainReloadInProgress,
            bool isReconnecting,
            bool showReconnectingUI,
            bool showPostCompileReconnectingUI)
        {
            IsServerRunning = isServerRunning;
            IsAfterCompile = isAfterCompile;
            IsDomainReloadInProgress = isDomainReloadInProgress;
            IsReconnecting = isReconnecting;
            ShowReconnectingUI = showReconnectingUI;
            ShowPostCompileReconnectingUI = showPostCompileReconnectingUI;
        }

        internal bool HasDomainReloadRecoveryState => IsAfterCompile || IsDomainReloadInProgress;
        internal bool IsServerRunning { get; }
        internal bool IsAfterCompile { get; }
        internal bool IsDomainReloadInProgress { get; }
        internal bool IsReconnecting { get; }
        internal bool ShowReconnectingUI { get; }
        internal bool ShowPostCompileReconnectingUI { get; }
    }

    /// <summary>
    /// Reads legacy JSON runtime flags only for the first reload that crosses the SessionState migration.
    /// </summary>
    internal sealed class UnityCliLoopEditorLegacySessionStateReader : IUnityCliLoopEditorLegacySessionStateReader
    {
        private static readonly string[] LegacySessionStateKeys =
        {
            "isServerRunning",
            "isAfterCompile",
            "isDomainReloadInProgress",
            "isReconnecting",
            "showReconnectingUI",
            "showPostCompileReconnectingUI"
        };

        private string SettingsFilePath => Path.Combine(
            UnityCliLoopConstants.USER_SETTINGS_FOLDER,
            UnityCliLoopConstants.SETTINGS_FILE_NAME);

        public UnityCliLoopEditorLegacySessionState Read()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new UnityCliLoopEditorLegacySessionState();
            }

            FileInfo fileInfo = new(SettingsFilePath);
            if (fileInfo.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Settings file exceeds size limit");
            }

            string json = File.ReadAllText(SettingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new UnityCliLoopEditorLegacySessionState();
            }

            JToken settingsToken = JToken.Parse(json);
            if (settingsToken is not JObject settingsObject)
            {
                return new UnityCliLoopEditorLegacySessionState();
            }

            return new UnityCliLoopEditorLegacySessionState(
                ReadBool(settingsObject, "isServerRunning"),
                ReadBool(settingsObject, "isAfterCompile"),
                ReadBool(settingsObject, "isDomainReloadInProgress"),
                ReadBool(settingsObject, "isReconnecting"),
                ReadBool(settingsObject, "showReconnectingUI"),
                ReadBool(settingsObject, "showPostCompileReconnectingUI"));
        }

        public void Clear()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            FileInfo fileInfo = new(SettingsFilePath);
            if (fileInfo.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Settings file exceeds size limit");
            }

            JToken settingsToken;
            using (StreamReader reader = File.OpenText(SettingsFilePath))
            {
                settingsToken = JToken.ReadFrom(new JsonTextReader(reader));
            }

            if (settingsToken is not JObject settingsObject)
            {
                return;
            }

            bool removed = false;
            foreach (string legacyKey in LegacySessionStateKeys)
            {
                removed |= settingsObject.Remove(legacyKey);
            }

            if (!removed)
            {
                return;
            }

            AtomicFileWriter.Write(SettingsFilePath, settingsToken.ToString(Formatting.Indented));
        }

        private static bool ReadBool(JObject settingsObject, string propertyName)
        {
            JToken propertyValue = settingsObject[propertyName];
            if (propertyValue == null)
            {
                return false;
            }

            if (propertyValue.Type != JTokenType.Boolean)
            {
                return false;
            }

            return propertyValue.Value<bool>();
        }
    }
}
