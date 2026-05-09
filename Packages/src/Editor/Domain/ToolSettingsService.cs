using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IToolSettingsPort
    {
        bool IsToolEnabled(string toolName);
        void SetToolEnabled(string toolName, bool enabled);
        string[] GetDisabledTools();
        void InvalidateCache();
    }

    public sealed class ToolSettingsService
    {
        private readonly IToolSettingsPort _toolSettingsPort;

        public ToolSettingsService(IToolSettingsPort toolSettingsPort)
        {
            Debug.Assert(toolSettingsPort != null, "toolSettingsPort must not be null");

            _toolSettingsPort = toolSettingsPort ?? throw new ArgumentNullException(nameof(toolSettingsPort));
        }

        public bool IsToolEnabled(string toolName)
        {
            return _toolSettingsPort.IsToolEnabled(toolName);
        }

        public void SetToolEnabled(string toolName, bool enabled)
        {
            _toolSettingsPort.SetToolEnabled(toolName, enabled);
        }

        public string[] GetDisabledTools()
        {
            return _toolSettingsPort.GetDisabledTools();
        }

        public void InvalidateCache()
        {
            _toolSettingsPort.InvalidateCache();
        }
    }
}
