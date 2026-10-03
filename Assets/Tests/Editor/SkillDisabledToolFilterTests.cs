using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how skills are matched against the disabled tool list.
    /// </summary>
    [TestFixture]
    public sealed class SkillDisabledToolFilterTests
    {
        /// <summary>
        /// Verifies that a skill whose tool name cannot be resolved is never treated as disabled.
        /// </summary>
        [Test]
        public void IsSkillDisabledByToolSettings_WhenToolNameCannotBeResolved_ReturnsFalse()
        {
            SkillInstallLayout.SkillSourceInfo skill = new(
                "custom-skill",
                null,
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["SKILL.md"] = new byte[] { 0x41 }
                });

            bool isDisabled = SkillDisabledToolFilter.IsSkillDisabledByToolSettings(skill, new[] { "compile" });

            Assert.That(isDisabled, Is.False);
        }
    }
}
