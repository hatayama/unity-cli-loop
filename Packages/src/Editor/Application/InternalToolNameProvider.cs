using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines access to Internal Tool Name dependencies without exposing their implementation.
    /// </summary>
    public interface IInternalToolNameProvider
    {
        HashSet<string> GetInternalToolNames(string projectRoot);
    }

    /// <summary>
    /// Provides Empty Internal Tool Name dependencies to callers without exposing construction details.
    /// </summary>
    public sealed class EmptyInternalToolNameProvider : IInternalToolNameProvider
    {
        public HashSet<string> GetInternalToolNames(string projectRoot)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
