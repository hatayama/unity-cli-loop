using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Display names for the skill install location picker; the members mirror SkillInstallRoot.
    /// </summary>
    internal enum SkillsInstallRootChoice
    {
        [InspectorName("Unity Project Root")]
        UnityProjectRoot = SkillInstallRoot.UnityProjectRoot,
        [InspectorName("Git Repository Root")]
        GitRepositoryRoot = SkillInstallRoot.GitRepositoryRoot
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
                ViewDataBinder.UpdateEnumField(_installRootField, (SkillsInstallRootChoice)currentRoot);
                return;
            }

            _installRootField.Init((SkillsInstallRootChoice)currentRoot);
            _installRootField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillsInstallRootChoice newValue)
                {
                    OnInstallRootChanged?.Invoke((SkillInstallRoot)newValue);
                }
            });
            _isFieldInitialized = true;
        }
    }
}
