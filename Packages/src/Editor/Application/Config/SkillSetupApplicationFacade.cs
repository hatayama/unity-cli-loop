using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Keeps Unity Editor callback code on a stable application entry point while skill setup policy lives in the domain layer.
    /// </summary>
    public static class SkillSetupApplicationFacade
    {
        private static SkillSetupService ServiceValue;

        internal static void RegisterService(SkillSetupService service)
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

        public static List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return Service.DetectSkillTargetsForLayoutAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public static List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return Service.DetectSkillTargetsForLayoutFastAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public static Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");

            return Service.InstallSkillFilesAsync(
                targets,
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

        private static SkillSetupService Service
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
    }
}
