using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity CLI Loop Game Object Search operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopGameObjectSearchService
    {
        Task<UnityCliLoopGameObjectSearchResult> FindGameObjectsAsync(
            UnityCliLoopGameObjectSearchRequest request,
            CancellationToken ct);
    }

    public enum SearchMode
    {
        Exact = 0,
        Path = 1,
        Regex = 2,
        Contains = 3,
        Selected = 4
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Game Object Search behavior.
    /// </summary>
    public sealed class UnityCliLoopGameObjectSearchRequest
    {
        public string NamePattern { get; set; } = "";
        public SearchMode SearchMode { get; set; } = SearchMode.Exact;
        public string[] RequiredComponents { get; set; } = new string[0];
        public string Tag { get; set; } = "";
        public int? Layer { get; set; }
        public bool IncludeInactive { get; set; }
        public int MaxResults { get; set; } = 20;
        public bool IncludeInheritedProperties { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Game Object Search behavior.
    /// </summary>
    public sealed class UnityCliLoopGameObjectSearchResult
    {
        public UnityCliLoopGameObjectResult[] Results { get; set; }
        public int TotalFound { get; set; }
        public string ErrorMessage { get; set; }
        public string ResultsFilePath { get; set; }
        public string Message { get; set; }
        public UnityCliLoopGameObjectProcessingError[] ProcessingErrors { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Game Object behavior.
    /// </summary>
    public sealed class UnityCliLoopGameObjectResult
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("layer")]
        public int Layer { get; set; }

        [JsonProperty("components")]
        public ComponentInfo[] Components { get; set; }
    }

    /// <summary>
    /// Provides Unity CLI Loop Game Object Processing Error behavior for Unity CLI Loop.
    /// </summary>
    public sealed class UnityCliLoopGameObjectProcessingError
    {
        [JsonProperty("gameObjectName")]
        public string GameObjectName { get; set; }

        [JsonProperty("gameObjectPath")]
        public string GameObjectPath { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
