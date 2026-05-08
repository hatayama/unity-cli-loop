using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Gives compile UI flows a shared gate for the dynamic-code post-compile warmup.
    public static class ExecuteDynamicCodeWarmup
    {
        public static Task WarmAfterCompileAsync(CancellationToken ct)
        {
            return DynamicCodeServices.WarmAfterCompileAsync(ct);
        }
    }
}
