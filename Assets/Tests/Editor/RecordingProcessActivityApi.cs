using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test double for the operating-system activity calls. It records Begin and End calls,
    /// hands out tokens that count up from 1, and can be told to fail either call.
    /// </summary>
    internal sealed class RecordingProcessActivityApi : IProcessActivityApi
    {
        internal const string EndFailureMessage = "End failed in this test";

        private readonly HashSet<IntPtr> _liveTokens = new HashSet<IntPtr>();
        private readonly List<IntPtr> _endedTokens = new List<IntPtr>();
        private int _lastToken;

        internal int BeginCount { get; private set; }

        internal IReadOnlyList<IntPtr> EndedTokens => _endedTokens;

        internal int LiveCount => _liveTokens.Count;

        internal bool ReturnsNoToken { get; set; }

        internal Exception BeginException { get; set; }

        internal bool ThrowOnNextEnd { get; set; }

        public IntPtr Begin(string reason)
        {
            BeginCount++;
            if (BeginException != null)
            {
                throw BeginException;
            }

            if (ReturnsNoToken)
            {
                return IntPtr.Zero;
            }

            _lastToken++;
            IntPtr token = new IntPtr(_lastToken);
            _liveTokens.Add(token);
            return token;
        }

        public void End(IntPtr token)
        {
            _endedTokens.Add(token);
            if (ThrowOnNextEnd)
            {
                ThrowOnNextEnd = false;
                throw new InvalidOperationException(EndFailureMessage);
            }

            _liveTokens.Remove(token);
        }
    }
}
