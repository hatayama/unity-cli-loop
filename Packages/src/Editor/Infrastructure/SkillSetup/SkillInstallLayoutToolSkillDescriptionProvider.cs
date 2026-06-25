using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides tool skill descriptions from the installed skill source layout.
    /// </summary>
    internal sealed class SkillInstallLayoutToolSkillDescriptionProvider : IToolSkillDescriptionProvider
    {
        public IReadOnlyDictionary<string, string> GetSkillDescriptionsByToolName()
        {
            return SkillInstallLayout.GetToolDescriptionsByToolName(UnityCliLoopPathResolver.GetProjectRoot());
        }
    }
}
