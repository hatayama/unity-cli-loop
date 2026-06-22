using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for GameObject search.
    /// </summary>
    [UnityCliLoopTool]
    public class FindGameObjectsTool : UnityCliLoopTool<FindGameObjectsSchema, FindGameObjectsResponse>
    {
        public override string ToolName => "find-game-objects";

        protected override async Task<FindGameObjectsResponse> ExecuteAsync(FindGameObjectsSchema parameters, CancellationToken ct)
        {
            FindGameObjectsUseCase useCase = new(new GameObjectFinderService(), new ComponentSerializer());
            UnityCliLoopGameObjectSearchResult result = await useCase.FindGameObjectsAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopGameObjectSearchRequest ToRequest(FindGameObjectsSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopGameObjectSearchRequest
            {
                NamePattern = parameters.NamePattern,
                SearchMode = parameters.SearchMode,
                RequiredComponents = parameters.RequiredComponents,
                Tag = parameters.Tag,
                Layer = parameters.Layer,
                IncludeInactive = parameters.IncludeInactive,
                MaxResults = parameters.MaxResults,
                IncludeInheritedProperties = parameters.IncludeInheritedProperties,
            };
        }

        private static FindGameObjectsResponse ToResponse(UnityCliLoopGameObjectSearchResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new FindGameObjectsResponse
            {
                Results = ToResults(result.Results),
                TotalFound = result.TotalFound,
                ErrorMessage = result.ErrorMessage,
                ResultsFilePath = result.ResultsFilePath,
                Message = result.Message,
                ProcessingErrors = ToProcessingErrors(result.ProcessingErrors),
            };
        }

        private static FindGameObjectResult[] ToResults(UnityCliLoopGameObjectResult[] results)
        {
            if (results == null)
            {
                return null;
            }

            FindGameObjectResult[] mappedResults = new FindGameObjectResult[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                UnityCliLoopGameObjectResult result = results[i];
                mappedResults[i] = new FindGameObjectResult
                {
                    Name = result.Name,
                    Path = result.Path,
                    IsActive = result.IsActive,
                    Tag = result.Tag,
                    Layer = result.Layer,
                    Components = result.Components,
                };
            }

            return mappedResults;
        }

        private static ProcessingError[] ToProcessingErrors(UnityCliLoopGameObjectProcessingError[] errors)
        {
            if (errors == null)
            {
                return null;
            }

            ProcessingError[] mappedErrors = new ProcessingError[errors.Length];
            for (int i = 0; i < errors.Length; i++)
            {
                UnityCliLoopGameObjectProcessingError error = errors[i];
                mappedErrors[i] = new ProcessingError
                {
                    GameObjectName = error.GameObjectName,
                    GameObjectPath = error.GameObjectPath,
                    Error = error.Error,
                };
            }

            return mappedErrors;
        }
    }
}
