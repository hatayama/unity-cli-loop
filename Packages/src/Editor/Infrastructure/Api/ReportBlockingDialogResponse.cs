using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Payload of the report-blocking-dialog command. It only says the report was taken: whether a
    /// waiting request is failed is decided afterwards, and that request's own answer says so.
    /// </summary>
    public class ReportBlockingDialogResponse : UnityCliLoopToolResponse
    {
    }
}
