using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Handles temporal cohesion for log retrieval processing
    /// Processing sequence: 1. Log retrieval, 2. Filtering, 3. Limiting and formatting
    /// Related classes: GetLogsTool, LogRetrievalService, LogFilteringService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class GetLogsUseCase : AbstractUseCase<GetLogsSchema, GetLogsResponse>
    {
        private readonly LogRetrievalService _retrievalService;
        private readonly LogFilteringService _filteringService;

        public GetLogsUseCase(LogRetrievalService retrievalService, LogFilteringService filteringService)
        {
            _retrievalService = retrievalService ?? throw new System.ArgumentNullException(nameof(retrievalService));
            _filteringService = filteringService ?? throw new System.ArgumentNullException(nameof(filteringService));
        }
        /// <summary>
        /// Executes log retrieval processing
        /// </summary>
        /// <param name="parameters">Log retrieval parameters</param>
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Log retrieval result</returns>
        public override Task<GetLogsResponse> ExecuteAsync(GetLogsSchema parameters, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 1. Log retrieval
                LogDisplayDto logData;
                
                try
                {
                    if (string.IsNullOrEmpty(parameters.SearchText))
                    {
                        logData = _retrievalService.GetLogs(parameters.LogType);
                    }
                    else
                    {
                        logData = _retrievalService.GetLogsWithSearch(
                            parameters.LogType, 
                            parameters.SearchText, 
                            parameters.UseRegex, 
                            parameters.SearchInStackTrace);
                    }
                }
                catch (System.Exception ex)
                {
                    throw new System.InvalidOperationException($"Failed to retrieve logs: {ex.Message}", ex);
                }
                
                // 2. Filtering and limiting
                cancellationToken.ThrowIfCancellationRequested();
                
                LogEntry[] logs = _filteringService.FilterAndLimitLogs(
                    logData.LogEntries, 
                    parameters.MaxCount, 
                    parameters.IncludeStackTrace);
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // 3. Response creation
                GetLogsResponse response = new GetLogsResponse(
                    totalCount: logData.TotalCount,
                    displayedCount: logs.Length,
                    logType: parameters.LogType,
                    maxCount: parameters.MaxCount,
                    searchText: parameters.SearchText,
                    includeStackTrace: parameters.IncludeStackTrace,
                    logs: logs
                );
                
                return Task.FromResult(response);
            }
            catch (System.OperationCanceledException)
            {
                // Propagate cancellations
                throw;
            }
            catch (System.Exception ex)
            {
                // Wrap or log unexpected errors
                throw new System.InvalidOperationException($"Failed to retrieve logs: {ex.Message}", ex);
            }
        }
    }
}