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
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class FindGameObjectsUseCase : IUnityCliLoopGameObjectSearchService
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
        public Task<UnityCliLoopGameObjectSearchResult> ExecuteAsync(
            UnityCliLoopGameObjectSearchRequest parameters,
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
                return Task.FromResult(new UnityCliLoopGameObjectSearchResult
                {
                    Results = new UnityCliLoopGameObjectResult[0],
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
                    MaxResults = parameters.MaxResults
                };
                
                GameObjectDetails[] foundObjects = _finderService.FindGameObjectsAdvanced(options);
            
                // 3. Result conversion and formatting
                ct.ThrowIfCancellationRequested();
                
                List<UnityCliLoopGameObjectResult> results = new();
                
                foreach (GameObjectDetails details in foundObjects)
                {
                    // Check cancellation less frequently for better performance
                    if (results.Count % 100 == 0)
                        ct.ThrowIfCancellationRequested();
                    
                    try
                    {
                        UnityCliLoopGameObjectResult result = new()                        {
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
                
                UnityCliLoopGameObjectSearchResult response = new()                {
                    Results = results.ToArray(),
                    TotalFound = results.Count
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
                
                return Task.FromResult(new UnityCliLoopGameObjectSearchResult
                {
                    Results = new UnityCliLoopGameObjectResult[0],
                    TotalFound = 0,
                    ErrorMessage = "Search execution failed. Please check the logs for details."
                });
            }
        }

        /// <summary>
        /// Execute Selected mode: get currently selected GameObjects in Unity Editor
        /// Single selection returns JSON directly, multiple selection exports to file
        /// </summary>
        private UnityCliLoopGameObjectSearchResult ExecuteSelectedMode(
            UnityCliLoopGameObjectSearchRequest parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            GameObjectDetails[] selectedObjects = _finderService.FindSelectedGameObjects(parameters.IncludeInactive);

            // No selection
            if (selectedObjects.Length == 0)
            {
                return new UnityCliLoopGameObjectSearchResult
                {
                    Results = new UnityCliLoopGameObjectResult[0],
                    TotalFound = 0,
                    Message = "No GameObjects are currently selected in Unity Editor."
                };
            }

            // Convert to FindGameObjectResult array
            List<UnityCliLoopGameObjectResult> results = new();
            List<UnityCliLoopGameObjectProcessingError> errors = new();

            foreach (GameObjectDetails details in selectedObjects)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    UnityCliLoopGameObjectResult result = new()                    {
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
                    errors.Add(new UnityCliLoopGameObjectProcessingError
                    {
                        GameObjectName = details.Name,
                        GameObjectPath = details.Path,
                        Error = ex.Message
                    });
                }
            }

            UnityCliLoopGameObjectResult[] resultArray = results.ToArray();
            UnityCliLoopGameObjectProcessingError[] errorArray = errors.Count > 0 ? errors.ToArray() : null;

            // Single selection: return JSON directly
            if (resultArray.Length == 1)
            {
                return new UnityCliLoopGameObjectSearchResult
                {
                    Results = resultArray,
                    TotalFound = 1,
                    ProcessingErrors = errorArray
                };
            }

            // No successful results
            if (resultArray.Length == 0)
            {
                return new UnityCliLoopGameObjectSearchResult
                {
                    Results = new UnityCliLoopGameObjectResult[0],
                    TotalFound = 0,
                    ProcessingErrors = errorArray,
                    Message = "All selected GameObjects failed to process."
                };
            }

            // Multiple selection: export to file
            string filePath = FindGameObjectsResultExporter.ExportResults(resultArray);

            return new UnityCliLoopGameObjectSearchResult
            {
                ResultsFilePath = filePath,
                TotalFound = resultArray.Length,
                Message = $"Multiple objects selected ({resultArray.Length}). Results exported to file.",
                ProcessingErrors = errorArray
            };
        }

        public Task<UnityCliLoopGameObjectSearchResult> FindGameObjectsAsync(
            UnityCliLoopGameObjectSearchRequest request,
            CancellationToken ct)
        {
            return ExecuteAsync(request, ct);
        }
    }
}
