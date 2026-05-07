using System;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Presenter class for LogGetterEditorWindow.
    /// </summary>
    public class LogGetterPresenter : IDisposable
    {
        public event Action<LogDisplayDto> OnLogDataUpdated;

        public void GetLogs()
        {
            LogDisplayDto displayData = LogGetter.GetAllConsoleLogs();
            OnLogDataUpdated?.Invoke(displayData);
        }

        public void GetLogs(string logType)
        {
            LogDisplayDto displayData;
            
            if (logType == UnityCliLoopLogType.All)
            {
                displayData = LogGetter.GetAllConsoleLogs();
            }
            else
            {
                displayData = LogGetter.GetConsoleLogsByType(logType);
            }
            
            OnLogDataUpdated?.Invoke(displayData);
        }

        public void ClearLogs()
        {
            LogDisplayDto displayData = new(new LogEntryDto[0], 0);
            OnLogDataUpdated?.Invoke(displayData);
        }

        public void Dispose()
        {
            OnLogDataUpdated = null;
        }
    }
} 