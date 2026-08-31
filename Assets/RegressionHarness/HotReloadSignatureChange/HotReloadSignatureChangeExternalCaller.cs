namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Compiled caller in a second file so a SharedValue return-type change has an
    // uncovered call site. The driver never edits this file.
    // Why a real call (not a method group): the scanner must see Call/Callvirt.
    public static class HotReloadSignatureChangeExternalCaller
    {
        public static int ReadShared(HotReloadSignatureChangeTarget target)
        {
            return target.SharedValue();
        }
    }
}
