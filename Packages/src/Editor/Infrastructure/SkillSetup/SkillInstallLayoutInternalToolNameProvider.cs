using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
