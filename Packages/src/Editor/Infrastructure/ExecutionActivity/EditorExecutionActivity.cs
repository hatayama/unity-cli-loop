using System;
using System.Collections.Generic;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps the operating system from throttling the Editor while it runs a command.
    /// Each hold starts one activity, and that activity ends exactly once: when the hold is disposed,
    /// or when the registry closes before a domain reload, whichever comes first.
    /// </summary>
    internal sealed class EditorExecutionActivity
    {
        private const string ActivityReason = "Unity CLI Loop is executing a command";

        private readonly IProcessActivityApi _api;
        private readonly object _gate = new object();

        // Why one set of live tokens: it is the single record of which token is still owed an End, so a
        // second Dispose, or a Dispose after closing, finds nothing and cannot end a token twice. Ending a
        // token twice touches released native memory and brings the Editor down.
        private readonly HashSet<IntPtr> _liveTokens = new HashSet<IntPtr>();
        private bool _closed;
        private bool _unavailable;

        internal EditorExecutionActivity(IProcessActivityApi api)
        {
            System.Diagnostics.Debug.Assert(api != null, "api must not be null");

            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        /// <summary>
        /// Creates the registry for this Editor domain. It closes itself before the next domain reload,
        /// because a reload abandons awaiting commands without disposing their holds, and the native
        /// activity would outlive the domain that started it.
        /// </summary>
        internal static EditorExecutionActivity CreateForEditor()
        {
            IProcessActivityApi api = new InertProcessActivityApi();
            EditorExecutionActivity activity = new EditorExecutionActivity(api);
            AssemblyReloadEvents.beforeAssemblyReload += activity.ReleaseAllAndClose;
            return activity;
        }

        /// <summary>
        /// Starts holding an activity for one command and returns the handle that ends it.
        /// Any thread may call it. The handle holds nothing when the registry is closed or the platform
        /// starts no activity; disposing it is still required and still safe.
        /// </summary>
        internal IDisposable Hold()
        {
            // Why Begin runs inside the lock: a close that races with this call must either see the new
            // token in the set and end it, or have closed before Begin, so no token escapes the set.
            lock (_gate)
            {
                if (_closed || _unavailable)
                {
                    return new InertHold();
                }

                IntPtr token;
                try
                {
                    token = _api.Begin(ActivityReason);
                }
                catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
                {
                    // Why only these two are caught: they mean the platform lacks the native entry points,
                    // and the activity only keeps a command fast, so a missing API must cost speed, never the
                    // command. Any other exception is a bug in the adapter and reaches the caller.
                    _unavailable = true;
                    UnityEngine.Debug.LogWarning(
                        $"[{UnityCliLoopConstants.PROJECT_NAME}] Commands may run slower while the Editor is in the background until the next domain reload: the activity that stops the operating system from throttling the Editor is unavailable ({exception.Message}).");
                    return new InertHold();
                }

                if (token == IntPtr.Zero)
                {
                    return new InertHold();
                }

                _liveTokens.Add(token);
                return new LiveHold(this, token);
            }
        }

        /// <summary>
        /// Ends every live activity and stops starting new ones. It runs before a domain reload;
        /// calling it again does nothing.
        /// </summary>
        internal void ReleaseAllAndClose()
        {
            lock (_gate)
            {
                _closed = true;
                IntPtr[] tokens = new IntPtr[_liveTokens.Count];
                _liveTokens.CopyTo(tokens);
                // Why clear before ending: every token handed to End has already left the set, so a hold
                // disposed later finds nothing and cannot end it a second time.
                _liveTokens.Clear();
                foreach (IntPtr token in tokens)
                {
                    // Why a failure is logged instead of thrown: Unity runs every beforeAssemblyReload
                    // subscriber from one loop with no isolation, so an exception here would skip the
                    // subscribers after this one, including the one that stops the server before the
                    // domain is unloaded. The domain is about to be discarded, so the log is the only
                    // place left to report it, and the remaining tokens are still ended.
                    try
                    {
                        _api.End(token);
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogException(exception);
                    }
                }
            }
        }

        private void Release(IntPtr token)
        {
            lock (_gate)
            {
                // Why remove before End: the token must leave the set even when End throws, so neither a
                // second Dispose nor a later close can end it again.
                if (!_liveTokens.Remove(token))
                {
                    return;
                }

                _api.End(token);
            }
        }

        /// <summary>
        /// Handle for an activity this registry started; disposing it ends the activity once.
        /// </summary>
        private sealed class LiveHold : IDisposable
        {
            private readonly EditorExecutionActivity _owner;
            private readonly IntPtr _token;

            internal LiveHold(EditorExecutionActivity owner, IntPtr token)
            {
                _owner = owner;
                _token = token;
            }

            public void Dispose()
            {
                _owner.Release(_token);
            }
        }

        /// <summary>
        /// Handle returned when no activity was started; disposing it does nothing.
        /// </summary>
        private sealed class InertHold : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
