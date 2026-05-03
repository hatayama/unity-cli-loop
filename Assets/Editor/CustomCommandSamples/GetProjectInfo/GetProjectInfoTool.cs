using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop;

namespace Samples
{
    /// <summary>
    /// Project information retrieval custom tool
    /// Example of retrieving detailed Unity project information
    /// </summary>
    [McpTool(Description = "Get detailed Unity project information")]
    public class GetProjectInfoTool : AbstractUnityTool<GetProjectInfoSchema, GetProjectInfoResponse>
    {
        public override string ToolName => "get-project-info";

        protected override Task<GetProjectInfoResponse> ExecuteAsync(GetProjectInfoSchema parameters, CancellationToken cancellationToken)
        {
            GetProjectInfoResponse response = new GetProjectInfoResponse
            {
                ProjectName = Application.productName,
                CompanyName = Application.companyName,
                Version = Application.version,
                UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(),
                DataPath = Application.dataPath,
                PersistentDataPath = Application.persistentDataPath,
                TemporaryCachePath = Application.temporaryCachePath,
                IsEditor = Application.isEditor,
                IsPlaying = Application.isPlaying,
                TargetFrameRate = Application.targetFrameRate,
                RunInBackground = Application.runInBackground,
                SystemLanguage = Application.systemLanguage.ToString(),
                InternetReachability = Application.internetReachability.ToString(),
                DeviceType = SystemInfo.deviceType.ToString(),
                DeviceModel = SystemInfo.deviceModel,
                OperatingSystem = SystemInfo.operatingSystem,
                ProcessorType = SystemInfo.processorType,
                ProcessorCount = SystemInfo.processorCount,
                SystemMemorySize = SystemInfo.systemMemorySize,
                GraphicsDeviceName = SystemInfo.graphicsDeviceName,
                Timestamp = System.DateTime.Now,
                ToolName = ToolName
            };
            
            return Task.FromResult(response);
        }
    }
} 