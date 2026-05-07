using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure adapter that resolves hidden tool names from installed skill metadata.
    /// <summary>
    /// Provides Skill Install Layout Internal Tool Name dependencies to callers without exposing construction details.
    /// </summary>
    public sealed class SkillInstallLayoutInternalToolNameProvider : IInternalToolNameProvider
    {
        public HashSet<string> GetInternalToolNames(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return SkillInstallLayout.GetInternalSkillToolNames(projectRoot);
        }
    }
}
