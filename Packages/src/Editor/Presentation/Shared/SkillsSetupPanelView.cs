using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shared skills setup panel view used by Settings and Setup Wizard.
    /// </summary>
    internal sealed class SkillsSetupPanelView
    {
        private readonly VisualElement _skillTargetStatusList;
        private readonly VisualElement _skillTargetStatusDivider;
        private readonly Label _skillTargetStatusSummary;
        private readonly Label _skillsNoTargetsMessage;
        private readonly Button _installAllSkillsButton;
        private readonly Foldout _installSpecificTargetFoldout;
        private readonly VisualElement _groupSkillsRow;
        private readonly Toggle _groupSkillsToggle;
        private readonly EnumField _skillsTargetField;
        private readonly Button _refreshSkillsStateButton;
        private readonly Button _installSelectedSkillsButton;

        private bool _isTargetFieldInitialized;
        private bool? _lastAppliedFoldoutDefault;

        internal event System.Action OnInstallAllClicked;
        internal event System.Action OnInstallSelectedClicked;
        internal event System.Action OnRefreshClicked;
        internal event System.Action<SkillsTarget> OnTargetChanged;
        internal event System.Action<bool> OnGroupSkillsChanged;

        internal SkillsSetupPanelView(VisualElement panelRoot, Button refreshSkillsStateButton)
        {
            Debug.Assert(panelRoot != null, "panelRoot must not be null");
            Debug.Assert(refreshSkillsStateButton != null, "refreshSkillsStateButton must not be null");
            VisualElement root = panelRoot ?? throw new System.ArgumentNullException(nameof(panelRoot));
            _refreshSkillsStateButton = refreshSkillsStateButton
                ?? throw new System.ArgumentNullException(nameof(refreshSkillsStateButton));

            RequiredSkillsPanelElements elements = QueryRequiredElements(root);
            _skillTargetStatusList = elements.StatusList;
            _skillTargetStatusDivider = elements.StatusDivider;
            _skillTargetStatusSummary = elements.StatusSummary;
            _skillsNoTargetsMessage = elements.NoTargetsMessage;
            _installAllSkillsButton = elements.InstallAllButton;
            _installSpecificTargetFoldout = elements.InstallSpecificTargetFoldout;
            _groupSkillsRow = elements.GroupSkillsRow;
            _groupSkillsToggle = elements.GroupSkillsToggle;
            _skillsTargetField = elements.SkillsTargetField;
            _installSelectedSkillsButton = elements.InstallSelectedButton;

            _installSpecificTargetFoldout.SetValueWithoutNotify(false);
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            ViewDataBinder.SetVisible(_skillsNoTargetsMessage, false);
            WireEventHandlers();
        }

        // Why a helper: the ctor's Q/assert/throw fan-out is one query step, and leaving it
        // inline kept the constructor over CA1502.
        private static RequiredSkillsPanelElements QueryRequiredElements(VisualElement root)
        {
            return new RequiredSkillsPanelElements(
                RequireNamedElement(
                    root.Q<VisualElement>("skill-target-status-list"),
                    "skill-target-status-list",
                    "_skillTargetStatusList"),
                RequireNamedElement(
                    root.Q<VisualElement>("skill-target-status-divider"),
                    "skill-target-status-divider",
                    "_skillTargetStatusDivider"),
                RequireNamedElement(
                    root.Q<Label>("skill-target-status-summary"),
                    "skill-target-status-summary",
                    "_skillTargetStatusSummary"),
                RequireNamedElement(
                    root.Q<Label>("skills-no-targets-message"),
                    "skills-no-targets-message",
                    "_skillsNoTargetsMessage"),
                RequireNamedElement(
                    root.Q<Button>("install-all-skills-button"),
                    "install-all-skills-button",
                    "_installAllSkillsButton"),
                RequireNamedElement(
                    root.Q<Foldout>("install-specific-target-foldout"),
                    "install-specific-target-foldout",
                    "_installSpecificTargetFoldout"),
                RequireNamedElement(
                    root.Q<VisualElement>("group-skills-row"),
                    "group-skills-row",
                    "_groupSkillsRow"),
                RequireNamedElement(
                    root.Q<Toggle>("group-skills-toggle"),
                    "group-skills-toggle",
                    "_groupSkillsToggle"),
                RequireNamedElement(
                    root.Q<EnumField>("skills-target-field"),
                    "skills-target-field",
                    "_skillsTargetField"),
                RequireNamedElement(
                    root.Q<Button>("install-selected-skills-button"),
                    "install-selected-skills-button",
                    "_installSelectedSkillsButton"));
        }

        // Why two names: Debug.Assert used the UXML name; ArgumentNullException used the field
        // nameof. Combining them into one string changed the exception ParamName.
        private static T RequireNamedElement<T>(T value, string assertName, string exceptionParamName)
            where T : VisualElement
        {
            Debug.Assert(value != null, assertName + " must not be null");
            return value ?? throw new System.ArgumentNullException(exceptionParamName);
        }

        private void WireEventHandlers()
        {
            _installAllSkillsButton.clicked += () => OnInstallAllClicked?.Invoke();
            _installSelectedSkillsButton.clicked += () => OnInstallSelectedClicked?.Invoke();
            _refreshSkillsStateButton.clicked += () => OnRefreshClicked?.Invoke();
            _groupSkillsToggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                OnGroupSkillsChanged?.Invoke(evt.newValue);
            });
            _groupSkillsRow.RegisterCallback<ClickEvent>(HandleGroupSkillsRowClicked);
        }

        private readonly struct RequiredSkillsPanelElements
        {
            internal readonly VisualElement StatusList;
            internal readonly VisualElement StatusDivider;
            internal readonly Label StatusSummary;
            internal readonly Label NoTargetsMessage;
            internal readonly Button InstallAllButton;
            internal readonly Foldout InstallSpecificTargetFoldout;
            internal readonly VisualElement GroupSkillsRow;
            internal readonly Toggle GroupSkillsToggle;
            internal readonly EnumField SkillsTargetField;
            internal readonly Button InstallSelectedButton;

            internal RequiredSkillsPanelElements(
                VisualElement statusList,
                VisualElement statusDivider,
                Label statusSummary,
                Label noTargetsMessage,
                Button installAllButton,
                Foldout installSpecificTargetFoldout,
                VisualElement groupSkillsRow,
                Toggle groupSkillsToggle,
                EnumField skillsTargetField,
                Button installSelectedButton)
            {
                StatusList = statusList;
                StatusDivider = statusDivider;
                StatusSummary = statusSummary;
                NoTargetsMessage = noTargetsMessage;
                InstallAllButton = installAllButton;
                InstallSpecificTargetFoldout = installSpecificTargetFoldout;
                GroupSkillsRow = groupSkillsRow;
                GroupSkillsToggle = groupSkillsToggle;
                SkillsTargetField = skillsTargetField;
                InstallSelectedButton = installSelectedButton;
            }
        }

        internal void ShowChecking()
        {
            _skillTargetStatusList.Clear();
            UpdateSkillsStatusLabel("Checking installed skills...");
            ViewDataBinder.SetVisible(_skillsNoTargetsMessage, false);
            _installAllSkillsButton.SetEnabled(false);
            _installAllSkillsButton.text = "Checking...";
            _installSelectedSkillsButton.SetEnabled(false);
            _installSelectedSkillsButton.text = "Checking...";
            _refreshSkillsStateButton.SetEnabled(false);
            _skillsTargetField.SetEnabled(false);
            _groupSkillsToggle.SetEnabled(false);
        }

        internal void UpdateStatusPanel(
            bool canManageSkills,
            List<SkillSetupTargetInfo> installableTargets,
            bool groupSkillsUnderUnityCliLoop,
            bool isInstallingSkills)
        {
            Debug.Assert(installableTargets != null, "installableTargets must not be null");

            _skillTargetStatusList.Clear();
            foreach (SkillSetupTargetInfo target in installableTargets)
            {
                VisualElement item = new();
                item.AddToClassList("skill-target-item");

                Label nameLabel = new($"{target.DisplayName} ({target.DirName}/)");
                nameLabel.AddToClassList("skill-target-item__label");
                item.Add(nameLabel);

                Label statusLabel = new(GetSkillInstallStatusText(
                    target.InstallState,
                    target.HasDifferentLayoutSkills,
                    groupSkillsUnderUnityCliLoop));
                statusLabel.AddToClassList("skill-target-item__status");
                statusLabel.AddToClassList(GetSkillInstallStatusClass(
                    target.InstallState,
                    target.HasDifferentLayoutSkills));
                item.Add(statusLabel);

                _skillTargetStatusList.Add(item);
            }

            bool listVisible = canManageSkills && installableTargets.Count > 0;
            ViewDataBinder.SetVisible(_skillTargetStatusList, listVisible);

            bool isCheckingSkills = installableTargets.Any(
                target => target.InstallState == SkillInstallState.Checking);
            if (!isCheckingSkills)
            {
                bool foldoutDefault = ShouldExpandSpecificTargetFoldout(installableTargets);
                if (_lastAppliedFoldoutDefault != foldoutDefault)
                {
                    _installSpecificTargetFoldout.SetValueWithoutNotify(foldoutDefault);
                    _lastAppliedFoldoutDefault = foldoutDefault;
                }
            }

            ViewDataBinder.SetVisible(_installAllSkillsButton, installableTargets.Count > 0);
            ViewDataBinder.SetVisible(_skillsNoTargetsMessage, !isCheckingSkills && installableTargets.Count == 0);
            if (isCheckingSkills)
            {
                UpdateSkillsStatusLabel("Checking installed skills...");
                _installAllSkillsButton.SetEnabled(false);
                _installAllSkillsButton.text = "Checking...";
                return;
            }

            bool allSkillsInstalled = installableTargets.Count > 0
                && installableTargets.All(target => target.InstallState == SkillInstallState.Installed);
            if (allSkillsInstalled)
            {
                UpdateSkillsStatusLabel(BuildInstalledSummaryText(installableTargets.Count));
                _installAllSkillsButton.SetEnabled(false);
                _installAllSkillsButton.text = "Installed";
                return;
            }

            bool hasOutdatedSkills = installableTargets.Any(
                target => target.InstallState == SkillInstallState.Outdated);
            UpdateSkillsStatusLabel(string.Empty);
            _installAllSkillsButton.SetEnabled(
                canManageSkills && !isInstallingSkills && installableTargets.Count > 0);
            _installAllSkillsButton.text = GetBulkInstallButtonText(
                canManageSkills,
                isInstallingSkills,
                hasOutdatedSkills);
        }

        internal void UpdateSelectedTargetInstall(
            SkillsTarget selectedTarget,
            SkillInstallState installState,
            bool isCliInstalled,
            bool isInstallingSkills)
        {
            InitializeTargetFieldIfNeeded(selectedTarget);
            _installSelectedSkillsButton.text = GetInstallSkillsButtonText(
                isCliInstalled,
                isInstallingSkills,
                installState);
            _installSelectedSkillsButton.SetEnabled(IsInstallSkillsButtonEnabled(
                isCliInstalled,
                isInstallingSkills,
                installState));
            _refreshSkillsStateButton.SetEnabled(!isInstallingSkills);
            _skillsTargetField.SetEnabled(
                isCliInstalled && installState != SkillInstallState.Checking);
        }

        internal void UpdateGroupSkillsToggle(bool groupSkillsUnderUnityCliLoop, bool isEnabled)
        {
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            ViewDataBinder.UpdateToggle(_groupSkillsToggle, groupSkillsUnderUnityCliLoop);
            _groupSkillsToggle.SetEnabled(isEnabled);
        }

        internal static List<SkillSetupTargetInfo> FilterInstallableSkillTargets(
            IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets
                .Where(target => target.HasSkillsDirectory)
                .ToList();
        }

        internal static SkillSetupTargetInfo CreateFirstInstallSkillTarget(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            return new(
                selection.DisplayName,
                selection.DirectoryName,
                selection.InstallFlag,
                hasSkillsDirectory: false,
                hasExistingSkills: false,
                hasDifferentLayoutSkills: false,
                SkillInstallState.Missing);
        }

        internal static SkillSetupTargetInfo GetSelectedSkillTargetInfo(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(targets != null, "targets must not be null");

            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            SkillSetupTargetInfo selectedTargetInfo = targets
                .FirstOrDefault(info => info.DirName == selection.DirectoryName);
            return string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? CreateFirstInstallSkillTarget(target, groupSkillsUnderUnityCliLoop)
                : selectedTargetInfo;
        }

        internal static List<SkillSetupTargetInfo> BuildSingleTargetInstallList(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillSetupTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                targets,
                target,
                groupSkillsUnderUnityCliLoop);
            return selectedTargetInfo.InstallState == SkillInstallState.Installed
                   || selectedTargetInfo.InstallState == SkillInstallState.Checking
                ? new List<SkillSetupTargetInfo>()
                : new List<SkillSetupTargetInfo> { selectedTargetInfo };
        }

        internal static string GetBulkInstallButtonText(
            bool canManageSkills,
            bool isInstallingSkills,
            bool hasOutdatedSkills)
        {
            if (!canManageSkills)
            {
                return "Install Skills";
            }

            if (isInstallingSkills)
            {
                return "Installing...";
            }

            return hasOutdatedSkills ? "Update Skills" : "Install Skills";
        }

        internal static string GetInstallSkillsButtonText(
            bool isCliInstalled,
            bool isInstallingSkills,
            SkillInstallState installState)
        {
            if (isInstallingSkills)
            {
                return "Installing...";
            }

            if (!isCliInstalled)
            {
                return "Install Skills";
            }

            return installState switch
            {
                SkillInstallState.Checking => "Checking...",
                SkillInstallState.Installed => "Installed",
                SkillInstallState.Outdated => "Update Skills",
                _ => "Install Skills"
            };
        }

        internal static bool IsInstallSkillsButtonEnabled(
            bool isCliInstalled,
            bool isInstallingSkills,
            SkillInstallState installState)
        {
            if (!isCliInstalled || isInstallingSkills)
            {
                return false;
            }

            return installState switch
            {
                SkillInstallState.Checking => false,
                SkillInstallState.Installed => false,
                _ => true
            };
        }

        internal static string GetSkillInstallStatusText(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "Checking...";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "Installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "Outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "Missing";
            }

            return groupSkillsUnderUnityCliLoop ? "Not grouped" : "Grouped";
        }

        internal static string GetSkillInstallStatusClass(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "skill-target-item__status--checking";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "skill-target-item__status--installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "skill-target-item__status--outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "skill-target-item__status--missing";
            }

            return "skill-target-item__status--different-layout";
        }

        internal static string BuildInstalledSummaryText(int installedTargetCount)
        {
            Debug.Assert(installedTargetCount >= 0, "installedTargetCount must not be negative");
            return $"Installed for {installedTargetCount} targets";
        }

        internal static bool ShouldExpandSpecificTargetFoldout(
            List<SkillSetupTargetInfo> installableTargets)
        {
            Debug.Assert(installableTargets != null, "installableTargets must not be null");
            Debug.Assert(
                installableTargets.All(target => target.InstallState != SkillInstallState.Checking),
                "installableTargets must not include Checking; caller must guard before applying foldout defaults");

            if (installableTargets.Count == 0)
            {
                return true;
            }

            return installableTargets.Any(target => target.InstallState == SkillInstallState.Missing);
        }

        private void InitializeTargetFieldIfNeeded(SkillsTarget currentTarget)
        {
            if (!_isTargetFieldInitialized)
            {
                _skillsTargetField.Init(currentTarget);
                _skillsTargetField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is SkillsTarget newValue)
                    {
                        OnTargetChanged?.Invoke(newValue);
                    }
                });
                _isTargetFieldInitialized = true;
                return;
            }

            ViewDataBinder.UpdateEnumField(_skillsTargetField, currentTarget);
        }

        private void UpdateSkillsStatusLabel(string text)
        {
            _skillTargetStatusSummary.text = text;
            bool isVisible = !string.IsNullOrEmpty(text);
            ViewDataBinder.SetVisible(_skillTargetStatusDivider, isVisible);
            ViewDataBinder.SetVisible(_skillTargetStatusSummary, isVisible);
        }

        private void HandleGroupSkillsRowClicked(ClickEvent evt)
        {
            evt.StopPropagation();
            if (!_groupSkillsToggle.enabledSelf)
            {
                return;
            }

            if (evt.target is VisualElement targetElement && _groupSkillsToggle.Contains(targetElement))
            {
                return;
            }

            bool newValue = !_groupSkillsToggle.value;
            _groupSkillsToggle.SetValueWithoutNotify(newValue);
            OnGroupSkillsChanged?.Invoke(newValue);
        }
    }
}
