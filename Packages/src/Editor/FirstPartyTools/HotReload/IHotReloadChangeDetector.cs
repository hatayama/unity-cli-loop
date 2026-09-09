namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports which sources changed since the last compile, which is what the tool selects from
    /// when the caller omits --files.
    /// </summary>
    internal interface IHotReloadChangeDetector
    {
        HotReloadChangedFileAggregationResult Detect();
    }

    /// <summary>
    /// The production detector: reads the compile snapshots the aggregator keeps.
    /// </summary>
    internal sealed class HotReloadChangeDetector : IHotReloadChangeDetector
    {
        public HotReloadChangedFileAggregationResult Detect()
        {
            return HotReloadChangedFileAggregator.Detect();
        }
    }
}
