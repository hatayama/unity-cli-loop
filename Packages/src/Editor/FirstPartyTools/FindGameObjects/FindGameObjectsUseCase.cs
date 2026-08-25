using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Responsible for temporal cohesion of GameObject search processing
    /// Processing sequence: 1. Search criteria validation, 2. GameObject search execution, 3. Result conversion and formatting
    /// Related classes: FindGameObjectsTool, GameObjectFinderService, ComponentSerializer
    /// </summary>
    public class FindGameObjectsUseCase
    {
        private readonly GameObjectFinderService _finderService;
        private readonly ComponentSerializer _componentSerializer;

        public FindGameObjectsUseCase(GameObjectFinderService finderService, ComponentSerializer componentSerializer)
        {
            _finderService = finderService ?? throw new System.ArgumentNullException(nameof(finderService));
            _componentSerializer = componentSerializer ?? throw new System.ArgumentNullException(nameof(componentSerializer));
        }
        /// <summary>
        /// Execute GameObject search processing
        /// </summary>
        /// <param name="parameters">Search parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Search result</returns>
        public Task<FindGameObjectsResponse> ExecuteAsync(
            FindGameObjectsSchema parameters,
            CancellationToken ct)
        {
            // Handle Selected mode separately
            if (parameters.SearchMode == SearchMode.Selected)
            {
                return Task.FromResult(ExecuteSelectedMode(parameters, ct));
            }

            // 1. Search criteria validation (skip for Selected mode)
            if (string.IsNullOrEmpty(parameters.NamePattern) &&
                (parameters.RequiredComponents == null || parameters.RequiredComponents.Length == 0) &&
                string.IsNullOrEmpty(parameters.Tag) &&
                !parameters.Layer.HasValue)
            {
                return Task.FromResult(new FindGameObjectsResponse
                {
                    Results = new FindGameObjectResult[0],
                    TotalFound = 0,
                    ErrorMessage = "At least one search criterion must be provided"
                });
            }

            // 2. GameObject search execution
            ct.ThrowIfCancellationRequested();
            
            try
            {
                GameObjectSearchOptions options = new()                {
                    NamePattern = parameters.NamePattern,
                    SearchMode = parameters.SearchMode,
                    RequiredComponents = parameters.RequiredComponents,
                    Tag = parameters.Tag,
                    Layer = parameters.Layer,
                    IncludeInactive = parameters.IncludeInactive,
                    MaxCount = parameters.MaxCount
                };
                
                GameObjectDetails[] foundObjects = _finderService.FindGameObjectsAdvanced(options);
            
                // 3. Result conversion and formatting
                ct.ThrowIfCancellationRequested();
                
                List<FindGameObjectResult> results = new();
                
                foreach (GameObjectDetails details in foundObjects)
                {
                    // Check cancellation less frequently for better performance
                    if (results.Count % 100 == 0)
                        ct.ThrowIfCancellationRequested();
                    
                    try
                    {
                        FindGameObjectResult result = new()                        {
                            Name = details.Name,
                            Path = details.Path,
                            IsActive = details.IsActive,
                            Tag = details.GameObject.tag,
                            Layer = details.GameObject.layer,
                            Components = _componentSerializer.SerializeComponents(details.GameObject)
                        };
                        
                        results.Add(result);
                    }
                    catch (System.Exception ex)
                    {
                        // Log error but continue processing other GameObjects
                        UnityEngine.Debug.LogWarning($"Failed to process GameObject '{details.Name}': {ex.Message}");
                        VibeLogger.LogWarning(
                            "gameobject_processing_failed", 
                            $"Failed to process GameObject: {details.Name}", 
                            new { gameObjectName = details.Name, gameObjectPath = details.Path, error = ex.Message }
                        );
                        continue;
                    }
                }
                
                FindGameObjectsResponse response = new()                {
                    Results = results.ToArray(),
                    TotalFound = results.Count,
                    Message = BuildZeroHitExactModeHint(parameters, results.Count)
                };

                // Underlying services are synchronous; wrapping in Task.FromResult for API consistency.
                return Task.FromResult(response);
            }
            catch (System.Exception ex)
            {
                // Log full exception details for debugging
                UnityEngine.Debug.LogError($"GameObject search failed: {ex}");
                VibeLogger.LogError(
                    "gameobject_search_failed", 
                    "GameObject search execution failed", 
                    new { searchParameters = parameters, error = ex.Message }
                );
                
                return Task.FromResult(new FindGameObjectsResponse
                {
                    Results = new FindGameObjectResult[0],
                    TotalFound = 0,
                    ErrorMessage = "Search execution failed. Please check the logs for details."
                });
            }
        }

        /// <summary>
        /// Builds a hint pointing agents at partial-matching search modes when an Exact name-pattern
        /// search finds nothing, since Exact is the tool's default and a literal-looking pattern
        /// (e.g. "Camera") silently misses partial matches (e.g. "Main Camera") without this hint.
        /// </summary>
        private static string BuildZeroHitExactModeHint(FindGameObjectsSchema parameters, int totalFound)
        {
            if (totalFound != 0 ||
                parameters.SearchMode != SearchMode.Exact ||
                string.IsNullOrEmpty(parameters.NamePattern))
            {
                return null;
            }

            return $"Exact match found nothing for name pattern '{parameters.NamePattern}'. " +
                   "Try --search-mode Contains or Regex for partial matching.";
        }

        /// <summary>
        /// Execute Selected mode: get currently selected GameObjects in Unity Editor
        /// Single selection returns JSON directly, multiple selection exports to file
        /// </summary>
        private FindGameObjectsResponse ExecuteSelectedMode(
            FindGameObjectsSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            GameObjectDetails[] selectedObjects = _finderService.FindSelectedGameObjects(parameters.IncludeInactive);

            // No selection
            if (selectedObjects.Length == 0)
            {
                return new FindGameObjectsResponse
                {
                    Results = new FindGameObjectResult[0],
                    TotalFound = 0,
                    Message = "No GameObjects are currently selected in Unity Editor."
                };
            }

            // Convert to FindGameObjectResult array
            List<FindGameObjectResult> results = new();
            List<ProcessingError> errors = new();

            foreach (GameObjectDetails details in selectedObjects)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    FindGameObjectResult result = new()                    {
                        Name = details.Name,
                        Path = details.Path,
                        IsActive = details.IsActive,
                        Tag = details.GameObject.tag,
                        Layer = details.GameObject.layer,
                        Components = _componentSerializer.SerializeComponents(details.GameObject)
                    };

                    results.Add(result);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Failed to process selected GameObject '{details.Name}': {ex.Message}");
                    VibeLogger.LogWarning(
                        "selected_gameobject_processing_failed",
                        $"Failed to process selected GameObject: {details.Name}",
                        new { gameObjectName = details.Name, gameObjectPath = details.Path, error = ex.Message }
                    );
                    errors.Add(new ProcessingError
                    {
                        GameObjectName = details.Name,
                        GameObjectPath = details.Path,
                        Error = ex.Message
                    });
                }
            }

            FindGameObjectResult[] resultArray = results.ToArray();
            ProcessingError[] errorArray = errors.Count > 0 ? errors.ToArray() : null;

            // Single selection: return JSON directly
            if (resultArray.Length == 1)
            {
                return new FindGameObjectsResponse
                {
                    Results = resultArray,
                    TotalFound = 1,
                    ProcessingErrors = errorArray
                };
            }

            // No successful results
            if (resultArray.Length == 0)
            {
                return new FindGameObjectsResponse
                {
                    Results = new FindGameObjectResult[0],
                    TotalFound = 0,
                    ProcessingErrors = errorArray,
                    Message = "All selected GameObjects failed to process."
                };
            }

            // Multiple selection: export to file
            string filePath = FindGameObjectsResultExporter.ExportResults(resultArray);

            return new FindGameObjectsResponse
            {
                ResultsFilePath = filePath,
                TotalFound = resultArray.Length,
                Message = $"Multiple objects selected ({resultArray.Length}). Results exported to file.",
                ProcessingErrors = errorArray
            };
        }
    }
}
