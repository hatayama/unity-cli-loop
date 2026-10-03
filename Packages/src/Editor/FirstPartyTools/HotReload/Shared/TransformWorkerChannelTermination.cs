using System;
using System.IO;

using UnityCliLoopDebug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Ends one worker channel: asks the process to quit, kills it when it will not, and disposes
    /// the channel either way.
    /// </summary>
    internal sealed class TransformWorkerChannelTermination
    {
        private const int GracefulQuitWaitMilliseconds = 500;
        private const int KillWaitMilliseconds = 2000;

        internal void Terminate(ITransformWorkerChannel channel)
        {
            try
            {
                if (!TryQuit(channel))
                {
                    KillQuietly(channel);
                }
            }
            finally
            {
                channel.Dispose();
            }
        }

        // True when the process has left or is leaving on its own; false when it has to be killed.
        // Why the handlers cover only the quit: a kill failure of a live process must reach the
        // caller, and a handler around the kill as well would drop it.
        private static bool TryQuit(ITransformWorkerChannel channel)
        {
            try
            {
                return channel.TryQuitGracefully(GracefulQuitWaitMilliseconds);
            }
            catch (IOException)
            {
                // The pipe is already gone; the process is exiting or exited.
                return false;
            }
            catch (InvalidOperationException)
            {
                // The process exited between the liveness check and the write.
                return true;
            }
        }

        // Why log instead of throw: Shutdown runs inside beforeAssemblyReload and quitting, which
        // Unity raises without a per-handler guard, so an exception here would skip every cleanup
        // registered after it. The host has already detached the channel, so a later request starts
        // a fresh worker either way.
        private static void KillQuietly(ITransformWorkerChannel channel)
        {
            try
            {
                channel.Kill(KillWaitMilliseconds);
            }
            catch (InvalidOperationException ex)
            {
                ReportKillFailureOfLiveProcess(channel, ex);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ReportKillFailureOfLiveProcess(channel, ex);
            }
        }

        // A kill that lost the race to the exit itself is the outcome the host wanted. A live
        // process that still refused the kill is a real problem, so it is reported as an error.
        private static void ReportKillFailureOfLiveProcess(ITransformWorkerChannel channel, Exception killFailure)
        {
            if (channel.HasExited)
            {
                return;
            }

            UnityCliLoopDebug.LogError(
                "Transform worker process " + channel.Id + " is still running after a failed kill: "
                + killFailure.Message);
        }
    }
}
