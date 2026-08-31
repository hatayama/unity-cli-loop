using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Samples
{
    /// <summary>
    /// Project information retrieval custom tool
    /// Example of retrieving detailed Unity project information
    /// </summary>
    [UnityCliLoopTool]
    public class GetProjectInfoTool : UnityCliLoopTool<GetProjectInfoSchema, GetProjectInfoResponse>
    {
        public override string ToolName => "get-project-info";

        protected override Task<GetProjectInfoResponse> ExecuteAsync(GetProjectInfoSchema parameters, CancellationToken cancellationToken)
        {
            GetProjectInfoResponse response = new()            {
                ProjectName = UnityEngine.Application.productName,
                CompanyName = UnityEngine.Application.companyName,
                Version = UnityEngine.Application.version,
                UnityVersion = UnityEngine.Application.unityVersion,
                Platform = UnityEngine.Application.platform.ToString(),
                DataPath = UnityEngine.Application.dataPath,
                PersistentDataPath = UnityEngine.Application.persistentDataPath,
                TemporaryCachePath = UnityEngine.Application.temporaryCachePath,
                IsEditor = UnityEngine.Application.isEditor,
                IsPlaying = UnityEngine.Application.isPlaying,
                TargetFrameRate = UnityEngine.Application.targetFrameRate,
                RunInBackground = UnityEngine.Application.runInBackground,
                SystemLanguage = UnityEngine.Application.systemLanguage.ToString(),
                InternetReachability = UnityEngine.Application.internetReachability.ToString(),
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