using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Extracts the semver string from a dispatcher release tag such as "dispatcher-v3.4.0".
    /// </summary>
    public static class DispatcherReleaseTagVersion
    {
        public static bool TryParse(string dispatcherReleaseTag, out string version)
        {
            version = null;
            if (string.IsNullOrEmpty(dispatcherReleaseTag))
            {
                return false;
            }

            if (!dispatcherReleaseTag.StartsWith(CliConstants.DISPATCHER_RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return false;
            }

            // The pin reader already validates the character set of the tag, so the remainder is not re-checked here.
            string remainder = dispatcherReleaseTag.Substring(CliConstants.DISPATCHER_RELEASE_TAG_PREFIX.Length);
            if (string.IsNullOrEmpty(remainder))
            {
                return false;
            }

            version = remainder;
            return true;
        }
    }
}
