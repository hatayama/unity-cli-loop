using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Responsible for temporal cohesion of Unity Hierarchy retrieval processing
    /// Processing sequence: 1. Hierarchy information retrieval, 2. Data conversion, 3. Response size determination and file output
    /// Related classes: GetHierarchyTool, HierarchyService, HierarchySerializer, HierarchyResultExporter
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class GetHierarchyUseCase : AbstractUseCase<GetHierarchySchema, GetHierarchyResponse>
    {
        private readonly HierarchyService _hierarchyService;
        private readonly HierarchySerializer _hierarchySerializer;

        public GetHierarchyUseCase(HierarchyService hierarchyService, HierarchySerializer hierarchySerializer)
        {
            _hierarchyService = hierarchyService ?? throw new System.ArgumentNullException(nameof(hierarchyService));
            _hierarchySerializer = hierarchySerializer ?? throw new System.ArgumentNullException(nameof(hierarchySerializer));
        }
        /// <summary>
        /// Execute Unity Hierarchy retrieval processing
        /// </summary>
        /// <param name="parameters">Hierarchy retrieval parameters</param>
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Hierarchy retrieval result</returns>
        public override Task<GetHierarchyResponse> ExecuteAsync(GetHierarchySchema parameters, CancellationToken cancellationToken)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            try
            {
                // 1. Hierarchy information retrieval
                HierarchyOptions options = new HierarchyOptions
                {
                    IncludeInactive = parameters.IncludeInactive,
                    MaxDepth = parameters.MaxDepth,
                    RootPath = parameters.RootPath,
                    IncludeComponents = parameters.IncludeComponents,
                    UseSelection = parameters.UseSelection
                };
                
                cancellationToken.ThrowIfCancellationRequested();
                
                var nodes = _hierarchyService.GetHierarchyNodes(options);
                HierarchyContext context = _hierarchyService.GetCurrentContext() ?? new HierarchyContext("editor", string.Empty, 0, 0);

                // 2. Data conversion to scene-grouped structure
                cancellationToken.ThrowIfCancellationRequested();
                HierarchySerializationOptions serOptions = new HierarchySerializationOptions
                {
                    IncludePaths = parameters.IncludePaths,
                    UseComponentsLut = parameters.UseComponentsLut
                };
                HierarchySerializationResult result = _hierarchySerializer.BuildGroups(nodes, context, serOptions);

                // 3. Always export to JSON
                cancellationToken.ThrowIfCancellationRequested();
                string filePath = HierarchyResultExporter.ExportHierarchyResults(result.Groups, result.Context);
                string message = "Hierarchy data saved below. Open the JSON to read 'Context' and 'Hierarchy'.";
                return Task.FromResult(new GetHierarchyResponse(filePath, message));
            }
            catch (System.OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (System.Exception ex)
            {
                // Log the error and re-throw
                VibeLogger.LogError("get_hierarchy_failed", $"Failed to get hierarchy: {ex.Message}", ex);
                throw new System.InvalidOperationException($"Failed to retrieve hierarchy: {ex.Message}", ex);
            }
        }
    }
}