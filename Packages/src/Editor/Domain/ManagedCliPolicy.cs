using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Resolves package-manager ownership and shared managed-install behavior.
    /// </summary>
    public static class ManagedCliPolicy
    {
        /// <summary>
        /// Resolves the package manager that owns an executable path.
        /// </summary>
        public static ManagedCliKind Resolve(string executablePath, Func<string, bool> directoryExists)
        {
            Debug.Assert(directoryExists != null, "directoryExists must not be null");

            if (HomebrewManagedCliPolicy.IsHomebrewManagedPath(executablePath, directoryExists))
            {
                return ManagedCliKind.Homebrew;
            }

            if (WingetManagedCliPolicy.IsWingetManagedPath(executablePath))
            {
                return ManagedCliKind.Winget;
            }

            return ManagedCliKind.None;
        }

        /// <summary>
        /// Reports whether an unusable package-manager-owned CLI needs manual upgrade guidance.
        /// </summary>
        public static bool ShouldShowUpgradeGuidance(ManagedCliKind kind, bool isCliUsable)
        {
            return kind != ManagedCliKind.None && !isCliUsable;
        }
    }
}
