using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Application-facing data for skill setup targets detected by Infrastructure.
    public readonly struct SkillSetupTargetInfo
    {
        public readonly string DisplayName;
        public readonly string DirName;
        public readonly string InstallFlag;
        public readonly bool HasSkillsDirectory;
        public readonly bool HasExistingSkills;
        public readonly bool HasDifferentLayoutSkills;
        public readonly SkillInstallState InstallState;

        public SkillSetupTargetInfo(
            string displayName,
            string dirName,
            string installFlag,
            bool hasSkillsDirectory,
            bool hasExistingSkills,
            bool hasDifferentLayoutSkills,
            SkillInstallState installState)
        {
            DisplayName = displayName;
            DirName = dirName;
            InstallFlag = installFlag;
            HasSkillsDirectory = hasSkillsDirectory;
            HasExistingSkills = hasExistingSkills;
            HasDifferentLayoutSkills = hasDifferentLayoutSkills;
            InstallState = installState;
        }
    }

    // Port for skill setup work that touches the project file system.
    /// <summary>
    /// Defines the Skill Setup boundary required by the application layer.
    /// </summary>
    public interface ISkillSetupPort
    {
        void RemoveSkillFiles(string toolName);
        bool IsSkillInstalled(string toolName);
        List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop);
        List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop);
        Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
        Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
    }

    // Application service that keeps Presentation dependent on a stable use-case boundary.
    /// <summary>
    /// Provides Skill Setup Application operations for its owning module.
    /// </summary>
    public sealed class SkillSetupApplicationService
    {
        private readonly ISkillSetupPort _skillSetupPort;

        public SkillSetupApplicationService(ISkillSetupPort skillSetupPort)
        {
            Debug.Assert(skillSetupPort != null, "skillSetupPort must not be null");

            _skillSetupPort = skillSetupPort ?? throw new ArgumentNullException(nameof(skillSetupPort));
        }

        public void RemoveSkillFiles(string toolName)
        {
            _skillSetupPort.RemoveSkillFiles(toolName);
        }

        public bool IsSkillInstalled(string toolName)
        {
            return _skillSetupPort.IsSkillInstalled(toolName);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupPort.DetectSkillTargetsForLayoutAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupPort.DetectSkillTargetsForLayoutFastAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.InstallSkillFilesAsync(
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.InstallSkillFilesForToolAsync(
                toolName,
                groupSkillsUnderUnityCliLoop,
                ct);
        }
    }

    // Static facade retained for Unity Editor callbacks that are not constructed by the composition root.
    /// <summary>
    /// Provides a narrow facade over Skill Setup Application behavior.
    /// </summary>
    public static class SkillSetupApplicationFacade
    {
        private static SkillSetupApplicationService ServiceValue;

        public readonly struct SkillTargetInfo
        {
            public readonly string DisplayName;
            public readonly string DirName;
            public readonly string InstallFlag;
            public readonly bool HasSkillsDirectory;
            public readonly bool HasExistingSkills;
            public readonly bool HasDifferentLayoutSkills;
            public readonly SkillInstallState InstallState;

            public SkillTargetInfo(
                string displayName,
                string dirName,
                string installFlag,
                bool hasSkillsDirectory,
                bool hasExistingSkills,
                bool hasDifferentLayoutSkills = false,
                SkillInstallState installState = SkillInstallState.Missing)
            {
                DisplayName = displayName;
                DirName = dirName;
                InstallFlag = installFlag;
                HasSkillsDirectory = hasSkillsDirectory;
                HasExistingSkills = hasExistingSkills;
                HasDifferentLayoutSkills = hasDifferentLayoutSkills;
                InstallState = installState;
            }
        }

        internal static void RegisterService(SkillSetupApplicationService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static void RemoveSkillFiles(string toolName)
        {
            Service.RemoveSkillFiles(toolName);
        }

        public static bool IsSkillInstalled(string toolName)
        {
            return Service.IsSkillInstalled(toolName);
        }

        public static List<SkillTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return Service.DetectSkillTargetsForLayoutAtProjectRoot(
                    projectRoot,
                    groupSkillsUnderUnityCliLoop)
                .Select(ToFacadeInfo)
                .ToList();
        }

        public static List<SkillTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return Service.DetectSkillTargetsForLayoutFastAtProjectRoot(
                    projectRoot,
                    groupSkillsUnderUnityCliLoop)
                .Select(ToFacadeInfo)
                .ToList();
        }

        public static Task InstallSkillFilesAsync(
            List<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");

            List<SkillSetupTargetInfo> applicationTargets = targets
                .Select(ToApplicationInfo)
                .ToList();
            return Service.InstallSkillFilesAsync(
                applicationTargets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public static Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            return Service.InstallSkillFilesForToolAsync(
                toolName,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        private static SkillSetupApplicationService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop skill setup application service is not registered.");
                }

                return ServiceValue;
            }
        }

        private static SkillTargetInfo ToFacadeInfo(SkillSetupTargetInfo target)
        {
            return new SkillTargetInfo(
                target.DisplayName,
                target.DirName,
                target.InstallFlag,
                target.HasSkillsDirectory,
                target.HasExistingSkills,
                target.HasDifferentLayoutSkills,
                target.InstallState);
        }

        private static SkillSetupTargetInfo ToApplicationInfo(SkillTargetInfo target)
        {
            return new SkillSetupTargetInfo(
                target.DisplayName,
                target.DirName,
                target.InstallFlag,
                target.HasSkillsDirectory,
                target.HasExistingSkills,
                target.HasDifferentLayoutSkills,
                target.InstallState);
        }
    }
}
