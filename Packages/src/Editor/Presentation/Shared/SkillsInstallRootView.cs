using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Display names for the skill install location picker; each member maps to one SkillInstallRoot.
    /// </summary>
    internal enum SkillsInstallRootChoice
    {
        [InspectorName("Unity Project Root")]
        UnityProjectRoot,
        [InspectorName("Git Repository Root")]
        GitRepositoryRoot
    }

    /// <summary>
    /// Skill install location picker and the resolved directory it currently points at.
    /// </summary>
    internal sealed class SkillsInstallRootView
    {
        private const string UnavailableClassName = "skills-install-root-location--unavailable";

        private readonly EnumField _installRootField;
        private readonly Label _locationLabel;
        private bool _isFieldInitialized;

        internal event Action<SkillInstallRoot> OnInstallRootChanged;

        internal SkillsInstallRootView(VisualElement panelRoot)
        {
            Debug.Assert(panelRoot != null, "panelRoot must not be null");

            EnumField installRootField = panelRoot.Q<EnumField>("skills-install-root-field");
            Label locationLabel = panelRoot.Q<Label>("skills-install-root-location");
            Debug.Assert(installRootField != null, "skills-install-root-field must not be null");
            Debug.Assert(locationLabel != null, "skills-install-root-location must not be null");

            _installRootField = installRootField
                ?? throw new ArgumentNullException(nameof(_installRootField));
            _locationLabel = locationLabel
                ?? throw new ArgumentNullException(nameof(_locationLabel));
        }

        internal void Update(SkillInstallRootSelection selection, bool canEdit)
        {
            // The default selection means the presenter has not read the saved choice yet.
            if (string.IsNullOrEmpty(selection.UnityProjectRootPath))
            {
                _installRootField.SetEnabled(false);
                _locationLabel.text = string.Empty;
                return;
            }

            InitializeFieldIfNeeded(selection.SelectedRoot);
            _installRootField.SetEnabled(SkillsInstallRootText.IsPickerEnabled(selection, canEdit));
            _locationLabel.text = SkillsInstallRootText.BuildLocationText(selection);
            ViewDataBinder.ToggleClass(_locationLabel, UnavailableClassName, !selection.IsSelectedRootAvailable);
        }

        private void InitializeFieldIfNeeded(SkillInstallRoot currentRoot)
        {
            if (_isFieldInitialized)
            {
                ViewDataBinder.UpdateEnumField(_installRootField, ToChoice(currentRoot));
                return;
            }

            _installRootField.Init(ToChoice(currentRoot));
            _installRootField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillsInstallRootChoice newValue)
                {
                    OnInstallRootChanged?.Invoke(ToRoot(newValue));
                }
            });
            _isFieldInitialized = true;
        }

        private static SkillsInstallRootChoice ToChoice(SkillInstallRoot root)
        {
            return root switch
            {
                SkillInstallRoot.UnityProjectRoot => SkillsInstallRootChoice.UnityProjectRoot,
                SkillInstallRoot.GitRepositoryRoot => SkillsInstallRootChoice.GitRepositoryRoot,
                _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown skill install root")
            };
        }

        private static SkillInstallRoot ToRoot(SkillsInstallRootChoice choice)
        {
            return choice switch
            {
                SkillsInstallRootChoice.UnityProjectRoot => SkillInstallRoot.UnityProjectRoot,
                SkillsInstallRootChoice.GitRepositoryRoot => SkillInstallRoot.GitRepositoryRoot,
                _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unknown skill install root choice")
            };
        }
    }
}
