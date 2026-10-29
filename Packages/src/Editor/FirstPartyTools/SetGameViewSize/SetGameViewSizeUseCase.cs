using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads, applies, or resets the Unity Game View custom rendering resolution, refusing views
    /// the custom resolution does not apply to.
    /// </summary>
    internal sealed class SetGameViewSizeUseCase
    {
        /// <summary>Base name of the Game View size entry this tool adds and later removes.</summary>
        internal const string OwnedSizeBaseName = "uloop";

        private const string ResetWithSizeMessage = "--reset cannot be combined with --width or --height.";
        private const string NotTogetherMessage = "Width and Height must be provided together.";
        private const string NotPositiveMessage = "Width and Height must be positive integers.";
        private const string ReadNoneMessage =
            "No Play Mode view is open, so there is no rendering resolution to read.";
        private const string SetSimulatorMessage =
            "The Device Simulator is showing, so the Game View rendering resolution was not changed; it does not apply there. "
            + "Run `uloop game-view-mode --to GameView` to show the Game View first, or pick a device with "
            + "`uloop game-view-mode --device <name>` to change the simulated screen size.";
        private const string SetOtherMessage =
            "A Play Mode view of another kind is showing; it does not support a custom resolution, so nothing was changed. "
            + "Run `uloop game-view-mode --to GameView` to show the Game View first.";
        private const string ResetSimulatorMessage =
            "The Device Simulator is showing, so the previous Game View selection cannot be restored; nothing was changed. "
            + "Run `uloop game-view-mode --to GameView` first, then run `uloop set-game-view-size --reset`.";
        private const string ResetOtherMessage =
            "A Play Mode view of another kind is showing, so the previous Game View selection cannot be restored; nothing was changed. "
            + "Run `uloop game-view-mode --to GameView` first, then run `uloop set-game-view-size --reset`.";
        private const string RemovedNoneMessage = "No 'uloop' size entry was found in the Game View size list.";
        private const string ResetNoneTail = "No Play Mode view is open, so there was no selection to restore.";

        private readonly ISetGameViewSizePort _port;

        public SetGameViewSizeUseCase(ISetGameViewSizePort port)
        {
            Debug.Assert(port != null, "port must not be null");
            _port = port;
        }

        /// <summary>
        /// Reports the rendering resolution without arguments, applies a paired width and height, or
        /// removes the owned entries with Reset. A failed request changes nothing.
        /// </summary>
        public SetGameViewSizeResponse Execute(SetGameViewSizeSchema parameters)
        {
            Debug.Assert(parameters != null, "parameters must not be null");

            bool hasWidth = parameters.Width.HasValue;
            bool hasHeight = parameters.Height.HasValue;
            // Reading the kind has no side effect, so it is read once and every branch agrees on it.
            PlayModeViewKind kind = _port.GetMainViewKind();

            // Checked before the pairing rule so --reset --width 10 names the real mistake.
            if (parameters.Reset && (hasWidth || hasHeight))
            {
                return Rejected(kind, ResetWithSizeMessage);
            }

            if (hasWidth != hasHeight)
            {
                return Rejected(kind, NotTogetherMessage);
            }

            if (parameters.Reset)
            {
                return Reset(kind);
            }

            if (!hasWidth)
            {
                return Read(kind);
            }

            if (parameters.Width.Value <= 0 || parameters.Height.Value <= 0)
            {
                return Rejected(kind, NotPositiveMessage);
            }

            return Apply(kind, (uint)parameters.Width.Value, (uint)parameters.Height.Value);
        }

        private (uint Width, uint Height) CurrentSizes(PlayModeViewKind kind)
        {
            // Reading the resolution with no view open makes Unity open a Game View.
            if (kind == PlayModeViewKind.None)
            {
                return (0, 0);
            }

            return _port.GetRenderingResolution();
        }

        private SetGameViewSizeResponse Rejected(PlayModeViewKind kind, string message)
        {
            (uint Width, uint Height) size = CurrentSizes(kind);
            return CreateResponse(false, kind, size, size, message);
        }

        private SetGameViewSizeResponse Read(PlayModeViewKind kind)
        {
            if (kind == PlayModeViewKind.None)
            {
                return CreateResponse(true, kind, (0, 0), (0, 0), ReadNoneMessage);
            }

            (uint Width, uint Height) size = _port.GetRenderingResolution();
            string sizeText = FormatSize(size);
            switch (kind)
            {
                case PlayModeViewKind.GameView:
                    SetGameViewSizeResponse response = CreateResponse(
                        true,
                        kind,
                        size,
                        size,
                        $"Current Game View rendering resolution is {sizeText}.");
                    response.SelectedSize = _port.GetSelectedSizeDisplayText();
                    return response;
                case PlayModeViewKind.Simulator:
                    return CreateResponse(
                        true,
                        kind,
                        size,
                        size,
                        $"The Device Simulator is showing and renders at {sizeText}. "
                        + "The Game View custom resolution does not apply while it is showing.");
                default:
                    return CreateResponse(
                        true,
                        kind,
                        size,
                        size,
                        $"A Play Mode view of another kind is showing and renders at {sizeText}. "
                        + "It does not support a custom resolution.");
            }
        }

        private SetGameViewSizeResponse Apply(PlayModeViewKind kind, uint width, uint height)
        {
            if (kind == PlayModeViewKind.Simulator)
            {
                return Rejected(kind, SetSimulatorMessage);
            }

            if (kind == PlayModeViewKind.Other)
            {
                return Rejected(kind, SetOtherMessage);
            }

            (uint Width, uint Height) previous = CurrentSizes(kind);
            // Remembered before applying, because afterwards the owned entry is selected. A second change
            // while the owned entry is selected must not overwrite the user's selection with our own.
            if (kind == PlayModeViewKind.GameView && _port.GetSelectedSizeBaseText() != OwnedSizeBaseName)
            {
                _port.RememberSelection(_port.GetSelectedSizeDisplayText());
            }

            _port.ApplyCustomResolution(width, height);
            (uint Width, uint Height) current = _port.GetRenderingResolution();
            string selected = _port.GetSelectedSizeDisplayText();
            string message = ApplyMessage(kind, previous, current);
            SetGameViewSizeResponse response = CreateResponse(true, PlayModeViewKind.GameView, previous, current, message);
            response.SelectedSize = selected;
            response.Warning =
                $"The Game View size list now has the entry '{selected}' selected. Unity saves the list to the user-wide "
                + "GameViewSizes.asset, so the entry stays after the Editor closes and appears in every project. "
                + "Run `uloop set-game-view-size --reset` to remove it and restore the previous selection.";
            return response;
        }

        private static string ApplyMessage(
            PlayModeViewKind kind,
            (uint Width, uint Height) previous,
            (uint Width, uint Height) current)
        {
            if (kind == PlayModeViewKind.None)
            {
                return $"No Play Mode view was open, so a Game View was opened with a rendering resolution of {FormatSize(current)}.";
            }

            if (previous != current)
            {
                return $"Game View rendering resolution changed from {FormatSize(previous)} to {FormatSize(current)}.";
            }

            return $"Game View rendering resolution is already {FormatSize(current)}.";
        }

        private SetGameViewSizeResponse Reset(PlayModeViewKind kind)
        {
            if (kind == PlayModeViewKind.Simulator)
            {
                return Rejected(kind, ResetSimulatorMessage);
            }

            if (kind == PlayModeViewKind.Other)
            {
                return Rejected(kind, ResetOtherMessage);
            }

            if (kind == PlayModeViewKind.None)
            {
                int removedWithoutView = _port.RemoveOwnedSizes();
                _port.ClearRememberedSelection();
                SetGameViewSizeResponse noneResponse = CreateResponse(
                    true,
                    kind,
                    (0, 0),
                    (0, 0),
                    RemovedPart(removedWithoutView) + ResetNoneTail);
                noneResponse.Removed = removedWithoutView;
                return noneResponse;
            }

            return ResetOnGameView();
        }

        private SetGameViewSizeResponse ResetOnGameView()
        {
            // Read before the removal, because removing entries shifts the selected index.
            (uint Width, uint Height) previous = _port.GetRenderingResolution();
            string selectedBefore = _port.GetSelectedSizeDisplayText();
            bool ownedSelected = _port.GetSelectedSizeBaseText() == OwnedSizeBaseName;
            int removed = _port.RemoveOwnedSizes();

            string selected;
            string tail;
            if (!ownedSelected)
            {
                // The user's own selection survives the removal, but its index may have shifted: re-select by name.
                if (removed > 0)
                {
                    _port.TrySelectSize(selectedBefore);
                }

                selected = selectedBefore;
                tail = $"The selected size '{selectedBefore}' is not one of uloop's, so it was kept.";
            }
            else
            {
                (selected, tail) = RestoreRememberedSelection();
            }

            _port.ClearRememberedSelection();
            (uint Width, uint Height) current = _port.GetRenderingResolution();
            SetGameViewSizeResponse response = CreateResponse(
                true,
                PlayModeViewKind.GameView,
                previous,
                current,
                RemovedPart(removed) + tail);
            response.Removed = removed;
            response.SelectedSize = selected;
            return response;
        }

        private (string Selected, string Tail) RestoreRememberedSelection()
        {
            string remembered = _port.ReadRememberedSelection();
            if (remembered != null && _port.TrySelectSize(remembered))
            {
                return (remembered, $"Restored the previous selection '{remembered}'.");
            }

            string first = _port.SelectFirstSize();
            if (remembered == null)
            {
                return (
                    first,
                    "The previous selection is unknown (it is remembered only within the Editor session that changed the size), "
                    + $"so '{first}' was selected.");
            }

            return (first, $"The previous selection '{remembered}' no longer exists, so '{first}' was selected.");
        }

        private static string RemovedPart(int removed)
        {
            if (removed > 0)
            {
                return $"Removed {removed} 'uloop' size entries from the Game View size list (every platform group). ";
            }

            return RemovedNoneMessage + " ";
        }

        private static string FormatSize((uint Width, uint Height) size)
        {
            return $"{size.Width}x{size.Height}";
        }

        private static SetGameViewSizeResponse CreateResponse(
            bool success,
            PlayModeViewKind view,
            (uint Width, uint Height) previous,
            (uint Width, uint Height) current,
            string message)
        {
            return new SetGameViewSizeResponse
            {
                Success = success,
                View = view.ToString(),
                Message = message,
                PreviousWidth = previous.Width,
                PreviousHeight = previous.Height,
                CurrentWidth = current.Width,
                CurrentHeight = current.Height,
                Changed = previous != current
            };
        }
    }
}
