using System;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs the domain-reload re-arm on the first Editor update tick after a reload, and only that
    /// once. The Editor update subscription is injected so a test can fire the registered handler
    /// without an Editor tick.
    /// </summary>
    internal sealed class PausePointRearmScheduler
    {
        private readonly Action _rearm;
        private readonly Action<EditorApplication.CallbackFunction> _subscribeUpdate;
        private readonly Action<EditorApplication.CallbackFunction> _unsubscribeUpdate;
        private bool _hasRun;

        public PausePointRearmScheduler(
            Action rearm,
            Action<EditorApplication.CallbackFunction> subscribeUpdate,
            Action<EditorApplication.CallbackFunction> unsubscribeUpdate)
        {
            _rearm = rearm ?? throw new ArgumentNullException(nameof(rearm));
            _subscribeUpdate = subscribeUpdate ?? throw new ArgumentNullException(nameof(subscribeUpdate));
            _unsubscribeUpdate = unsubscribeUpdate ?? throw new ArgumentNullException(nameof(unsubscribeUpdate));
        }

        public void Schedule()
        {
            EditorApplication.CallbackFunction handler = null;
            handler = () =>
            {
                _unsubscribeUpdate(handler);


                // The unsubscribe above is what normally stops a second tick, but the re-arm
                // consumes the store, so a tick already queued when the handler was removed must
                // not replay an empty store over a report the first tick just published.
                if (_hasRun)
                {
                    return;
                }

                _hasRun = true;
                _rearm();
            };

            _subscribeUpdate(handler);
        }
    }
}
