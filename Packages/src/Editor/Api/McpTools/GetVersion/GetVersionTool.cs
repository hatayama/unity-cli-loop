using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    [McpTool(
        DisplayDevelopmentOnly = true,
        Description = "Get Unity version and project information"
    )]
    public class GetVersionTool : AbstractUnityTool<GetVersionSchema, GetVersionResponse>
    {
        public override string ToolName => "get-version";

        protected override Task<GetVersionResponse> ExecuteAsync(
            GetVersionSchema parameters, CancellationToken ct)
        {
            GetVersionResponse response = new GetVersionResponse
            {
                UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(),
                DataPath = Application.dataPath,
                PersistentDataPath = Application.persistentDataPath,
                TemporaryCachePath = Application.temporaryCachePath,
                IsEditor = Application.isEditor,
                ProductName = Application.productName,
                CompanyName = Application.companyName
            };

            return Task.FromResult(response);
        }
    }
}
