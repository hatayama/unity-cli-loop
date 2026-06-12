using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Debug = System.Diagnostics.Debug;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Resets Setup Wizard state when Unity removes this package from the project.
    /// </summary>
    internal sealed class UnityCliLoopPackageRemovalSettingsResetter
    {
        private readonly UnityCliLoopEditorSettingsService _editorSettingsService;

        internal UnityCliLoopPackageRemovalSettingsResetter(UnityCliLoopEditorSettingsService editorSettingsService)
        {
            Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");

            _editorSettingsService = editorSettingsService
                ?? throw new ArgumentNullException(nameof(editorSettingsService));
        }

        internal void RegisterForEditorStartup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            if (UnityEngine.Application.isBatchMode)
            {
                return;
            }

            // Unsubscribe first so a repeated registration cannot stack duplicate handlers,
            // matching the guard pattern used by the other editor-lifetime subscriptions.
            Events.registeringPackages -= HandleRegisteringPackages;
            Events.registeringPackages += HandleRegisteringPackages;
        }

        internal static bool ShouldResetSetupWizardState(
            IEnumerable<string> removedPackageNames,
            string packageName)
        {
            Debug.Assert(removedPackageNames != null, "removedPackageNames must not be null");
            Debug.Assert(!string.IsNullOrEmpty(packageName), "packageName must not be null or empty");

            return removedPackageNames.Any(
                removedPackageName => string.Equals(removedPackageName, packageName, StringComparison.Ordinal));
        }

        internal static UnityCliLoopEditorSettingsData ResetSetupWizardState(UnityCliLoopEditorSettingsData settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            return settings with
            {
                lastSeenSetupWizardVersion = string.Empty,
                suppressSetupWizardAutoShow = false
            };
        }

        internal static void ResetSetupWizardStateIfPackageRemoved(
            UnityCliLoopEditorSettingsService editorSettingsService,
            IEnumerable<string> removedPackageNames,
            string packageName)
        {
            Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");
            Debug.Assert(removedPackageNames != null, "removedPackageNames must not be null");
            Debug.Assert(!string.IsNullOrEmpty(packageName), "packageName must not be null or empty");

            if (!ShouldResetSetupWizardState(removedPackageNames, packageName))
            {
                return;
            }

            editorSettingsService.UpdateSettings(ResetSetupWizardState);
        }

        private void HandleRegisteringPackages(PackageRegistrationEventArgs args)
        {
            Debug.Assert(args != null, "args must not be null");

            IEnumerable<string> removedPackageNames = GetRemovedPackageNames(args);
            ResetSetupWizardStateIfPackageRemoved(
                _editorSettingsService,
                removedPackageNames,
                UnityCliLoopConstants.PACKAGE_NAME);
        }

        private static IEnumerable<string> GetRemovedPackageNames(PackageRegistrationEventArgs args)
        {
            Debug.Assert(args != null, "args must not be null");

            return args.removed.Select(packageInfo => packageInfo.name);
        }
    }
}
