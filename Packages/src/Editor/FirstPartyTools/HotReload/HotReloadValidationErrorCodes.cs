namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines machine-readable codes for hot-reload argument validation failures.
    /// </summary>
    internal static class HotReloadValidationErrorCodes
    {
        internal const string FilesRequired = "HOT_RELOAD_FILES_REQUIRED";
        internal const string InvalidFiles = "HOT_RELOAD_INVALID_FILES";
        internal const string StatusConflict = "HOT_RELOAD_STATUS_CONFLICT";
        internal const string NoChangedFiles = "HOT_RELOAD_NO_CHANGED_FILES";
    }
}
