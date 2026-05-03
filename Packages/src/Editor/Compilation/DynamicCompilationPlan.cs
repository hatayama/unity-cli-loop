namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class DynamicCompilationPlan
    {
        public CompilationRequest OriginalRequest { get; }

        public CompilationRequest NormalizedRequest { get; }

        public PreparedDynamicCode PreparedCode { get; }

        public string ClassName { get; }

        public string NamespaceName { get; }

        public DynamicCompilationPlan(
            CompilationRequest originalRequest,
            CompilationRequest normalizedRequest,
            PreparedDynamicCode preparedCode,
            string className,
            string namespaceName)
        {
            OriginalRequest = originalRequest;
            NormalizedRequest = normalizedRequest;
            PreparedCode = preparedCode;
            ClassName = className;
            NamespaceName = namespaceName;
        }
    }
}
