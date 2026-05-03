using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    public static class SystemReflectionMetadataPreloadValidatorRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            PreloadAssemblySecurityValidatorRegistry.RegisterValidator(new SystemReflectionMetadataPreloadValidator());
        }
    }
}
