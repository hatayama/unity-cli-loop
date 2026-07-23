using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Pure state machine that decides whether the editor SignalTick pump should keep running.
    /// Why: scopes cover in-flight CLI work; a trailing window covers teardown and inter-command gaps
    /// without leaving the pump on permanently (which would burn CPU while idle and unfocused).
    /// </summary>
    internal sealed class AutoTickPumpController
    {
        private readonly double _trailingWindowSeconds;
        private readonly object _gate = new object();
        private int _activeScopeCount;
        private double _lastActivitySeconds;
        private bool _hasActivity;

        public AutoTickPumpController(double trailingWindowSeconds)
        {
            Debug.Assert(trailingWindowSeconds > 0, "trailingWindowSeconds must be greater than 0");
            _trailingWindowSeconds = trailingWindowSeconds;
        }

        public void NotifyScopeStarted()
        {
            lock (_gate)
            {
                _activeScopeCount++;
            }
        }

        public void NotifyScopeEnded(double nowSeconds)
        {
            lock (_gate)
            {
                Debug.Assert(_activeScopeCount > 0, "NotifyScopeEnded called with no active scope");
                _activeScopeCount--;
                _lastActivitySeconds = nowSeconds;
                _hasActivity = true;
            }
        }

        public void NotifyStartupCompleted(double nowSeconds)
        {
            lock (_gate)
            {
                _lastActivitySeconds = nowSeconds;
                _hasActivity = true;
            }
        }

        public bool ShouldPump(double nowSeconds)
        {
            lock (_gate)
            {
                if (_activeScopeCount > 0)
                {
                    return true;
                }

                if (!_hasActivity)
                {
                    return false;
                }

                return (nowSeconds - _lastActivitySeconds) < _trailingWindowSeconds;
            }
        }
    }
}
