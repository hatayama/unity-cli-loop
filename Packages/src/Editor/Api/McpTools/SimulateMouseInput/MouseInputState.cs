#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.uLoopMCP
{
    // Tool instances are created fresh per invocation, so held-button state
    // between Click/LongPress must be held statically.
    [InitializeOnLoad]
    internal static class MouseInputState
    {
        private static readonly HashSet<MouseButton> _heldButtons = new();

        // Pending reset callbacks for per-frame values (delta, scroll).
        // Tracked so we can unsubscribe on PlayMode exit to prevent leaks.
        private static Action? _pendingDeltaReset;
        private static Action? _pendingScrollReset;

        public static bool IsButtonHeld(MouseButton button) => _heldButtons.Contains(button);

        static MouseInputState()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void SetButtonDown(MouseButton button)
        {
            _heldButtons.Add(button);
        }

        public static void SetButtonUp(MouseButton button)
        {
            _heldButtons.Remove(button);
        }

        // StateEvent.From captures the full mouse state snapshot.
        // All currently held buttons must be written into every event
        // to avoid accidentally releasing them.
        public static void SetButtonState(Mouse mouse, MouseButton button, bool pressed)
        {
            Debug.Assert(mouse != null, "mouse must not be null");
            ApplyStateEvent(mouse!, eventPtr =>
            {
                GetButtonControl(mouse!, button).WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
            });
        }

        public static void SetPositionState(Mouse mouse, Vector2 position)
        {
            Debug.Assert(mouse != null, "mouse must not be null");
            ApplyStateEvent(mouse!, eventPtr =>
            {
                mouse!.position.WriteValueIntoEvent(position, eventPtr);
            });
        }

        public static void SetDeltaState(Mouse mouse, Vector2 delta)
        {
            InjectDelta(mouse, delta);
            SchedulePerFrameReset(mouse!, mouse!.delta, isDelta: true);
        }

        // Inject delta without scheduling a reset. Used by SmoothDelta which
        // manages its own lifecycle — resetting only after the final frame.
        public static void InjectDelta(Mouse mouse, Vector2 delta)
        {
            Debug.Assert(mouse != null, "mouse must not be null");
            ApplyStateEvent(mouse!, eventPtr =>
            {
                mouse!.delta.WriteValueIntoEvent(delta, eventPtr);
            });
        }

        public static void SetScrollState(Mouse mouse, Vector2 scroll)
        {
            Debug.Assert(mouse != null, "mouse must not be null");
            ApplyStateEvent(mouse!, eventPtr =>
            {
                mouse!.scroll.WriteValueIntoEvent(scroll, eventPtr);
            });

            SchedulePerFrameReset(mouse!, mouse!.scroll, isDelta: false);
        }

        public static void ReleaseAllButtons()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                _heldButtons.Clear();
                ClearPendingResets();
                return;
            }

            using (StateEvent.From(mouse, out InputEventPtr eventPtr))
            {
                foreach (MouseButton button in _heldButtons)
                {
                    GetButtonControl(mouse, button).WriteValueIntoEvent(0f, eventPtr);
                }

                InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
                InputState.Change(mouse, eventPtr, updateType);
            }

            _heldButtons.Clear();
            ClearPendingResets();
        }

        private static void ApplyStateEvent(Mouse mouse, Action<InputEventPtr> writePayload)
        {
            InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
            using (StateEvent.From(mouse, out InputEventPtr eventPtr))
            {
                foreach (MouseButton heldButton in _heldButtons)
                {
                    GetButtonControl(mouse, heldButton).WriteValueIntoEvent(1f, eventPtr);
                }

                writePayload(eventPtr);
                InputState.Change(mouse, eventPtr, updateType);
            }
        }

        // delta and scroll are per-frame values; leaving them non-zero causes
        // accumulation across frames. Reset on the next input update.
        private static void SchedulePerFrameReset(
            Mouse mouse,
            InputControl<Vector2> control,
            bool isDelta)
        {
            // Remove previous pending reset to avoid stacking callbacks
            Action? previousReset = isDelta ? _pendingDeltaReset : _pendingScrollReset;
            if (previousReset != null)
            {
                InputSystem.onBeforeUpdate -= previousReset;
            }

            // Capture the target update type so the reset only fires in the same
            // Input System update phase that gameplay code reads, preventing an
            // Editor or BeforeRender update from clearing the value prematurely.
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();

            Action? resetCallback = null;
            resetCallback = () =>
            {
                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                Debug.Assert(resetCallback != null, "resetCallback must be assigned before subscription");
                InputSystem.onBeforeUpdate -= resetCallback;

                if (isDelta && _pendingDeltaReset == resetCallback)
                {
                    _pendingDeltaReset = null;
                }
                else if (!isDelta && _pendingScrollReset == resetCallback)
                {
                    _pendingScrollReset = null;
                }

                if (Mouse.current != mouse)
                {
                    return;
                }

                ApplyStateEvent(mouse, eventPtr =>
                {
                    control.WriteValueIntoEvent(Vector2.zero, eventPtr);
                });
            };

            if (isDelta)
            {
                _pendingDeltaReset = resetCallback;
            }
            else
            {
                _pendingScrollReset = resetCallback;
            }

            InputSystem.onBeforeUpdate += resetCallback;
        }

        private static void ClearPendingResets()
        {
            if (_pendingDeltaReset != null)
            {
                InputSystem.onBeforeUpdate -= _pendingDeltaReset;
                _pendingDeltaReset = null;
            }

            if (_pendingScrollReset != null)
            {
                InputSystem.onBeforeUpdate -= _pendingScrollReset;
                _pendingScrollReset = null;
            }
        }

        internal static ButtonControl GetButtonControl(Mouse mouse, MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right:
                    return mouse.rightButton;
                case MouseButton.Middle:
                    return mouse.middleButton;
                default:
                    Debug.Assert(button == MouseButton.Left, $"Unexpected MouseButton value: {button}");
                    return mouse.leftButton;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseAllButtons();
                SimulateMouseInputOverlayState.Clear();
            }
        }
    }
}
#endif
