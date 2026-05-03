using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Responsible for temporal cohesion of GameObject search processing
    /// Processing sequence: 1. Search criteria validation, 2. GameObject search execution, 3. Result conversion and formatting
    /// Related classes: FindGameObjectsTool, GameObjectFinderService, ComponentSerializer
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class FindGameObjectsUseCase : AbstractUseCase<FindGameObjectsSchema, FindGameObjectsResponse>
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
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Search result</returns>
        public override Task<FindGameObjectsResponse> ExecuteAsync(FindGameObjectsSchema parameters, CancellationToken cancellationToken)
        {
            // Handle Selected mode separately
            if (parameters.SearchMode == SearchMode.Selected)
            {
                return Task.FromResult(ExecuteSelectedMode(parameters, cancellationToken));
            }

            // 1. Search criteria validation (skip for Selected mode)
            if (string.IsNullOrEmpty(parameters.NamePattern) &&
                (parameters.RequiredComponents == null || parameters.RequiredComponents.Length == 0) &&
                string.IsNullOrEmpty(parameters.Tag) &&
                !parameters.Layer.HasValue)
            {
                return Task.FromResult(new FindGameObjectsResponse
                {
                    results = new FindGameObjectResult[0],
                    totalFound = 0,
                    errorMessage = "At least one search criterion must be provided"
                });
            }

            // 2. GameObject search execution
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                GameObjectSearchOptions options = new GameObjectSearchOptions
                {
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
                cancellationToken.ThrowIfCancellationRequested();
                
                List<FindGameObjectResult> results = new List<FindGameObjectResult>();
                
                foreach (GameObjectDetails details in foundObjects)
                {
                    // Check cancellation less frequently for better performance
                    if (results.Count % 100 == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        FindGameObjectResult result = new FindGameObjectResult
                        {
                            name = details.Name,
                            path = details.Path,
                            isActive = details.IsActive,
                            tag = details.GameObject.tag,
                            layer = details.GameObject.layer,
                            components = _componentSerializer.SerializeComponents(details.GameObject)
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
                
                FindGameObjectsResponse response = new FindGameObjectsResponse
                {
                    results = results.ToArray(),
                    totalFound = results.Count
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
                    results = new FindGameObjectResult[0],
                    totalFound = 0,
                    errorMessage = "Search execution failed. Please check the logs for details."
                });
            }
        }

        /// <summary>
        /// Execute Selected mode: get currently selected GameObjects in Unity Editor
        /// Single selection returns JSON directly, multiple selection exports to file
        /// </summary>
        private FindGameObjectsResponse ExecuteSelectedMode(FindGameObjectsSchema parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameObjectDetails[] selectedObjects = _finderService.FindSelectedGameObjects(parameters.IncludeInactive);

            // No selection
            if (selectedObjects.Length == 0)
            {
                return new FindGameObjectsResponse
                {
                    results = new FindGameObjectResult[0],
                    totalFound = 0,
                    message = "No GameObjects are currently selected in Unity Editor."
                };
            }

            // Convert to FindGameObjectResult array
            List<FindGameObjectResult> results = new List<FindGameObjectResult>();
            List<ProcessingError> errors = new List<ProcessingError>();

            foreach (GameObjectDetails details in selectedObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    FindGameObjectResult result = new FindGameObjectResult
                    {
                        name = details.Name,
                        path = details.Path,
                        isActive = details.IsActive,
                        tag = details.GameObject.tag,
                        layer = details.GameObject.layer,
                        components = _componentSerializer.SerializeComponents(details.GameObject)
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
                        gameObjectName = details.Name,
                        gameObjectPath = details.Path,
                        error = ex.Message
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
                    results = resultArray,
                    totalFound = 1,
                    processingErrors = errorArray
                };
            }

            // No successful results
            if (resultArray.Length == 0)
            {
                return new FindGameObjectsResponse
                {
                    results = new FindGameObjectResult[0],
                    totalFound = 0,
                    processingErrors = errorArray,
                    message = "All selected GameObjects failed to process."
                };
            }

            // Multiple selection: export to file
            string filePath = FindGameObjectsResultExporter.ExportResults(resultArray);

            return new FindGameObjectsResponse
            {
                resultsFilePath = filePath,
                totalFound = resultArray.Length,
                message = $"Multiple objects selected ({resultArray.Length}). Results exported to file.",
                processingErrors = errorArray
            };
        }
    }
}