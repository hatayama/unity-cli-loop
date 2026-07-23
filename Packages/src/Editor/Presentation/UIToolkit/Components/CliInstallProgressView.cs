using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shows animated install status on the install button and the latest
    /// installer output line while the global CLI install is running.
    /// </summary>
    internal sealed class CliInstallProgressView
    {
        // Why 300ms: slow enough to read as a busy indicator without looking frantic.
        private const long TICK_INTERVAL_MS = 300;

        private readonly VisualElement _container;
        private readonly Button _installButton;
        private readonly Label _detailLabel;

        private IVisualElementScheduledItem _tick;
        private DateTime _startedAtUtc;
        private int _tickCount;

        internal CliInstallProgressView(
            VisualElement container,
            Button installButton,
            Label detailLabel)
        {
            Debug.Assert(container != null, "container must not be null");
            Debug.Assert(installButton != null, "installButton must not be null");
            Debug.Assert(detailLabel != null, "detailLabel must not be null");

            _container = container;
            _installButton = installButton;
            _detailLabel = detailLabel;
            // Why disable rich text: installer stdout/stderr is untrusted process output.
            // Leaving enableRichText on would interpret accidental <...> fragments as markup.
            _detailLabel.enableRichText = false;
        }

        internal void Show()
        {
            _startedAtUtc = DateTime.UtcNow;
            _tickCount = 0;
            _installButton.text = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.Zero, _tickCount);
            _detailLabel.text = string.Empty;
            _container.style.display = DisplayStyle.Flex;

            if (_tick == null)
            {
                _tick = _installButton.schedule.Execute(AdvanceTick).Every(TICK_INTERVAL_MS);
                return;
            }

            _tick.Resume();
        }

        // Why no visibility assert: Progress<T>.Report posts asynchronously, so a
        // late line can arrive after Hide(); writing to a hidden label is harmless.
        internal void SetDetailLine(string rawLine)
        {
            string formatted = CliInstallProgressFormatting.FormatDetailLine(rawLine);
            if (formatted.Length == 0)
            {
                return;
            }

            _detailLabel.text = formatted;
        }

        // Why leave button text alone: post-install RefreshSection / _refreshUi
        // restores the correct static label after the install workflow finishes.
        internal void Hide()
        {
            _tick?.Pause();
            _container.style.display = DisplayStyle.None;
        }

        private void AdvanceTick()
        {
            _tickCount++;
            _installButton.text = CliInstallProgressFormatting.FormatStatusLine(
                DateTime.UtcNow - _startedAtUtc,
                _tickCount);
        }
    }
}
