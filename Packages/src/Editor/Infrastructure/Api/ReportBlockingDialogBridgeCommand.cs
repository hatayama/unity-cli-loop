using System;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Runs the report-blocking-dialog command the CLI sends when it finds a modal dialog while a
    /// request it sent has not been answered: a request still waiting for the Editor main thread is
    /// then failed as blocked without the full wait. A request whose tool code has started is left alone.
    /// </summary>
    internal static class ReportBlockingDialogBridgeCommand
    {
        // Why no main-thread switch: the dialog holds the main thread, so this must answer without it.
        internal static ReportBlockingDialogResponse Execute(UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            if (toolRegistrarService == null)
            {
                throw new ArgumentNullException(nameof(toolRegistrarService));
            }

            toolRegistrarService.ReportBlockingDialog();
            return new ReportBlockingDialogResponse();
        }
    }
}
