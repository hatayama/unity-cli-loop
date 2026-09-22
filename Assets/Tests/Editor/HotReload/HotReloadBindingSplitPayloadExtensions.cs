namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A compiled extension on the payload type, so a split can hide in the receiver of a reduced
    /// extension call, which is not one of the call's parameters.
    /// </summary>
    public static class HotReloadBindingSplitPayloadExtensions
    {
        public static int Doubled(this HotReloadBindingSplitPayload payload)
        {
            return payload.Value * 2;
        }
    }
}
