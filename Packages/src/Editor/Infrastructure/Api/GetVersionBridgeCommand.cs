using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides the CLI readiness version payload without registering an extension-facing tool.
    /// </summary>
    internal static class GetVersionBridgeCommand
    {
        public static GetVersionResponse Execute()
        {
            GetVersionResponse response = new()            {
                UnityVersion = UnityEngine.Application.unityVersion,
                Platform = UnityEngine.Application.platform.ToString(),
                DataPath = UnityEngine.Application.dataPath,
                PersistentDataPath = UnityEngine.Application.persistentDataPath,
                TemporaryCachePath = UnityEngine.Application.temporaryCachePath,
                IsEditor = UnityEngine.Application.isEditor,
                ProductName = UnityEngine.Application.productName,
                CompanyName = UnityEngine.Application.companyName
            };

            Debug.Assert(!string.IsNullOrWhiteSpace(response.UnityVersion), "Unity version must be available.");
            return response;
        }
    }
}
