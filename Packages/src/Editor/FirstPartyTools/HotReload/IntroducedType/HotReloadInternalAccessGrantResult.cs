using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether every artifact method received the grant or none was changed.
    /// </summary>
    internal sealed class HotReloadInternalAccessGrantResult
    {
        public bool Success { get; }
        public int MethodCount { get; }
        public string Reason { get; }

        private HotReloadInternalAccessGrantResult(bool success, int methodCount, string reason)
        {
            Success = success;
            MethodCount = methodCount;
            Reason = reason;
        }

        public static HotReloadInternalAccessGrantResult Granted(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return new HotReloadInternalAccessGrantResult(true, count, string.Empty);
        }

        public static HotReloadInternalAccessGrantResult Refused(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A refused grant must explain why no methods were changed.", nameof(reason));
            }

            return new HotReloadInternalAccessGrantResult(false, 0, reason);
        }
    }
}
