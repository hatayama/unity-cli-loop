using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the registered Preload Assembly Security Validator entries for lookup by the owning module.
    /// </summary>
    public static class PreloadAssemblySecurityValidatorRegistry
    {
        private static readonly object SyncRoot = new object();
        private static IPreloadAssemblySecurityValidator _validator;

        public static void RegisterValidator(IPreloadAssemblySecurityValidator validator)
        {
            Debug.Assert(validator != null, "validator must not be null");

            lock (SyncRoot)
            {
                _validator = validator;
            }
        }

        public static bool TryGetValidator(out IPreloadAssemblySecurityValidator validator)
        {
            lock (SyncRoot)
            {
                validator = _validator;
            }

            return validator != null;
        }

        public static IPreloadAssemblySecurityValidator SwapValidatorForTests(IPreloadAssemblySecurityValidator validator)
        {
            lock (SyncRoot)
            {
                IPreloadAssemblySecurityValidator previousValidator = _validator;
                _validator = validator;
                return previousValidator;
            }
        }
    }
}
