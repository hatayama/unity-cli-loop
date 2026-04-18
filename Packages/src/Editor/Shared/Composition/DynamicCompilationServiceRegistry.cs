using System.Diagnostics;

namespace io.github.hatayama.uLoopMCP
{
    public static class DynamicCompilationServiceRegistry
    {
        private static readonly object SyncRoot = new object();
        private static IDynamicCompilationServiceFactory _factory;

        public static bool HasRegisteredFactory
        {
            get
            {
                lock (SyncRoot)
                {
                    return _factory != null;
                }
            }
        }

        public static void RegisterFactory(IDynamicCompilationServiceFactory factory)
        {
            Debug.Assert(factory != null, "factory must not be null");

            lock (SyncRoot)
            {
                _factory = factory;
            }
        }

        public static bool TryCreate(DynamicCodeSecurityLevel securityLevel, out IDynamicCompilationService service)
        {
            IDynamicCompilationServiceFactory factory;
            lock (SyncRoot)
            {
                factory = _factory;
            }

            if (factory == null)
            {
                service = null;
                return false;
            }

            service = factory.Create(securityLevel);
            return service != null;
        }

        public static IDynamicCompilationServiceFactory SwapFactoryForTests(IDynamicCompilationServiceFactory factory)
        {
            lock (SyncRoot)
            {
                IDynamicCompilationServiceFactory previousFactory = _factory;
                _factory = factory;
                return previousFactory;
            }
        }
    }
}
