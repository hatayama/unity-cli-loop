using System;
using System.IO;

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
                if (!channel.TryQuitGracefully(GracefulQuitWaitMilliseconds))
                {
                    KillQuietly(channel);
                }
            }
            catch (IOException)
            {
                // The pipe is already gone; the process is exiting or exited.
                KillQuietly(channel);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the liveness check and the write.
            }
            finally
            {
                channel.Dispose();
            }
        }

        // Why swallow: the process can exit between the liveness check and the kill, and a race
        // the host lost still leaves it with the outcome it asked for - a dead worker.
        private void KillQuietly(ITransformWorkerChannel channel)
        {
            try
            {
                channel.Kill(KillWaitMilliseconds);
            }
            catch (InvalidOperationException)
            {
                // The race was lost to the exit itself, which is the outcome the host wanted.
                // A live process that still refused the kill is a real problem.
                if (!channel.HasExited)
                {
                    throw;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Same race, reported by the OS instead of the runtime.
                if (!channel.HasExited)
                {
                    throw;
                }
            }
        }
    }
}
