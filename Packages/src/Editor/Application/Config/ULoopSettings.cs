using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Port for persisted Unity CLI Loop permission settings.
    /// <summary>
    /// Defines the ULoop settings boundary required by the application layer.
    /// </summary>
    public interface IULoopSettingsPort
    {
        ULoopSettingsData GetSettings();
        void SaveSettings(ULoopSettingsData settings);
        void UpdateSettings(Func<ULoopSettingsData, ULoopSettingsData> transform);
        DynamicCodeSecurityLevel GetDynamicCodeSecurityLevel();
        void SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level);
        void InvalidateCache();
    }

    // Static facade retained for Unity callbacks and legacy call sites outside constructor control.
    /// <summary>
    /// Holds U Loop settings used by Unity CLI Loop.
    /// </summary>
    public static class ULoopSettings
    {
        private static IULoopSettingsPort ServiceValue;

        internal static void RegisterService(IULoopSettingsPort service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static ULoopSettingsData GetSettings()
        {
            return Service.GetSettings();
        }

        public static void SaveSettings(ULoopSettingsData settings)
        {
            Service.SaveSettings(settings);
        }

        public static void UpdateSettings(Func<ULoopSettingsData, ULoopSettingsData> transform)
        {
            Service.UpdateSettings(transform);
        }

        public static DynamicCodeSecurityLevel GetDynamicCodeSecurityLevel()
        {
            return Service.GetDynamicCodeSecurityLevel();
        }

        public static void SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            Service.SetDynamicCodeSecurityLevel(level);
        }

        internal static void InvalidateCache()
        {
            Service.InvalidateCache();
        }

        private static IULoopSettingsPort Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop permission settings service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
