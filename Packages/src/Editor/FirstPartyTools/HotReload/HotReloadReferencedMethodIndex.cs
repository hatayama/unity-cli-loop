using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Red stub: answers true for every dll and never reads one.
    /// </summary>
    internal sealed class HotReloadReferencedMethodIndex
    {
        public static HotReloadReferencedMethodIndex Shared { get; } = new HotReloadReferencedMethodIndex();

        internal int LoadCount
        {
            get { return 0; }
        }

        internal static string BuildKey(string assemblyName, string openDeclaringTypeFullName, string methodName)
        {
            return assemblyName + "|" + openDeclaringTypeFullName + "::" + methodName;
        }

        internal bool MentionsAny(string dllPath, IReadOnlyCollection<string> keys)
        {
            return true;
        }
    }
}
