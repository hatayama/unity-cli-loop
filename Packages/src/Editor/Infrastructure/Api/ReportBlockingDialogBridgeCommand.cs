using System;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Runs the report-blocking-dialog command the CLI sends when it finds a modal dialog while a
    /// request it sent has not been answered: a request still waiting for the Editor main thread is
    /// then failed as blocked without the full wait, when it is a request of the ToolName the CLI
    /// sent. A request whose tool code has started is left alone.
    /// </summary>
    internal static class ReportBlockingDialogBridgeCommand
    {
        internal const string ToolNameParam = "ToolName";

        // Why no main-thread switch: the dialog holds the main thread, so this must answer without it.
        internal static ReportBlockingDialogResponse Execute(
            UnityCliLoopToolRegistrarService toolRegistrarService,
            JToken paramsToken)
        {
            if (toolRegistrarService == null)
            {
                throw new ArgumentNullException(nameof(toolRegistrarService));
            }

            // Why a missing name is taken, not rejected: a report without a name fails no request, so it
            // is the same as no report, and an error answer would only make the CLI stop reporting.
            // Why the object check: indexing params that are an array or a scalar throws, which would turn
            // a malformed report into an error answer instead of a report without a name.
            JToken nameToken = paramsToken is JObject paramsObject ? paramsObject[ToolNameParam] : null;
            string toolName = nameToken?.Type == JTokenType.String ? nameToken.Value<string>() : string.Empty;
            toolRegistrarService.ReportBlockingDialog(toolName);
            return new ReportBlockingDialogResponse();
        }
    }
}
