using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IInternalToolNameProvider
    {
        HashSet<string> GetInternalToolNames(string projectRoot);
    }

    public sealed class EmptyInternalToolNameProvider : IInternalToolNameProvider
    {
        public HashSet<string> GetInternalToolNames(string projectRoot)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
