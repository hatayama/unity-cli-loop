using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates retrieval, filtering, limiting, and response creation for the bundled get-logs tool.
    /// </summary>
    public class GetLogsUseCase
    {
        private readonly IUnityCliLoopConsoleLogService _consoleLogs;
        private readonly LogFilteringService _filteringService;

        public GetLogsUseCase(IUnityCliLoopConsoleLogService consoleLogs, LogFilteringService filteringService)
        {
            _consoleLogs = consoleLogs ?? throw new ArgumentNullException(nameof(consoleLogs));
            _filteringService = filteringService ?? throw new ArgumentNullException(nameof(filteringService));
        }

        public Task<GetLogsResponse> ExecuteAsync(GetLogsSchema parameters, CancellationToken ct)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            ct.ThrowIfCancellationRequested();

            UnityCliLoopConsoleLogResult logData = GetLogData(parameters);
            ct.ThrowIfCancellationRequested();

            LogEntry[] logs = _filteringService.FilterAndLimitLogs(
                logData.LogEntries,
                parameters.MaxCount,
                parameters.IncludeStackTrace);
            ct.ThrowIfCancellationRequested();

            GetLogsResponse response = new(
                totalCount: logData.TotalCount,
                displayedCount: logs.Length,
                logType: parameters.LogType,
                maxCount: parameters.MaxCount,
                searchText: parameters.SearchText,
                includeStackTrace: parameters.IncludeStackTrace,
                logs: logs);

            return Task.FromResult(response);
        }

        private UnityCliLoopConsoleLogResult GetLogData(GetLogsSchema parameters)
        {
            if (string.IsNullOrEmpty(parameters.SearchText))
            {
                return _consoleLogs.GetLogs(parameters.LogType);
            }

            return _consoleLogs.GetLogsWithSearch(
                parameters.LogType,
                parameters.SearchText,
                parameters.UseRegex,
                parameters.SearchInStackTrace);
        }
    }
}
