namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Resolves CLI setup primary button actions without UI label or enabled-state concerns.
    /// </summary>
    public enum CliSetupPrimaryAction
    {
        None,
        InstallOrUpdate,
        RepairPath,
        Uninstall
    }

    /// <summary>
    /// Resolves pure CLI setup primary action decisions.
    /// </summary>
    public static class CliSetupPrimaryActionPolicy
    {
        public static bool ShouldRepairCliPath(bool needsCliPathSetup, bool needsUpdate)
        {
            return needsCliPathSetup && !needsUpdate;
        }

        public static bool ShouldUninstallCli(
            bool isCliInstalled,
            bool needsUpdate,
            bool canUninstallCli)
        {
            return canUninstallCli && isCliInstalled && !needsUpdate;
        }

        /// <remarks>
        /// Why the managed branch lives here and not only in the button state: the primary action is
        /// resolved again after a forced refresh, so a managed install that appears between click and
        /// action would otherwise still run an install and leave a second binary beside its owner.
        /// Why PATH repair survives it: repair neither writes nor removes a binary, it only makes an
        /// already installed package-owned CLI reachable, so it is the one action that stays safe.
        /// </remarks>
        public static CliSetupPrimaryAction ResolveSettingsPrimaryAction(
            bool needsCliPathSetup,
            bool needsUpdate,
            bool isCliInstalled,
            bool canUninstallCli,
            ManagedCliKind managedCliKind)
        {
            if (managedCliKind != ManagedCliKind.None)
            {
                return needsCliPathSetup ? CliSetupPrimaryAction.RepairPath : CliSetupPrimaryAction.None;
            }

            if (ShouldRepairCliPath(needsCliPathSetup, needsUpdate))
            {
                return CliSetupPrimaryAction.RepairPath;
            }

            if (ShouldUninstallCli(isCliInstalled, needsUpdate, canUninstallCli))
            {
                return CliSetupPrimaryAction.Uninstall;
            }

            return CliSetupPrimaryAction.InstallOrUpdate;
        }

        public static CliSetupPrimaryAction ResolveExecutableSettingsAction(
            CliSetupPrimaryAction clickedAction,
            CliSetupPrimaryAction refreshedAction)
        {
            if (clickedAction == refreshedAction)
            {
                return clickedAction;
            }

            if (clickedAction == CliSetupPrimaryAction.InstallOrUpdate
                && refreshedAction == CliSetupPrimaryAction.RepairPath)
            {
                return CliSetupPrimaryAction.RepairPath;
            }

            return CliSetupPrimaryAction.None;
        }
    }
}
