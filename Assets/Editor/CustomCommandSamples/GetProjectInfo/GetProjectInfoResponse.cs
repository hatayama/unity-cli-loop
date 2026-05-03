using System;
using io.github.hatayama.UnityCliLoop;

namespace Samples
{
    /// <summary>
    /// Response schema for GetProjectInfo tool
    /// Provides detailed Unity project information
    /// </summary>
    public class GetProjectInfoResponse : BaseToolResponse
    {
        public string ProjectName { get; set; }
        public string CompanyName { get; set; }
        public string Version { get; set; }
        public string UnityVersion { get; set; }
        public string Platform { get; set; }
        public string DataPath { get; set; }
        public string PersistentDataPath { get; set; }
        public string TemporaryCachePath { get; set; }
        public bool IsEditor { get; set; }
        public bool IsPlaying { get; set; }
        public int TargetFrameRate { get; set; }
        public bool RunInBackground { get; set; }
        public string SystemLanguage { get; set; }
        public string InternetReachability { get; set; }
        public string DeviceType { get; set; }
        public string DeviceModel { get; set; }
        public string OperatingSystem { get; set; }
        public string ProcessorType { get; set; }
        public int ProcessorCount { get; set; }
        public int SystemMemorySize { get; set; }
        public string GraphicsDeviceName { get; set; }
        public DateTime Timestamp { get; set; }
        public string ToolName { get; set; }

        public GetProjectInfoResponse()
        {
        }
    }
}