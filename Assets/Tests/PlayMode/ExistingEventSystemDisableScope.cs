using UnityEngine;
using UnityEngine.EventSystems;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Disables existing scene EventSystems while PlayMode tests create an isolated EventSystem.
    /// </summary>
    internal sealed class ExistingEventSystemDisableScope
    {
        private readonly EventSystem[] eventSystems;
        private readonly bool[] originalEnabledStates;

        public ExistingEventSystemDisableScope()
        {
            eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            originalEnabledStates = new bool[eventSystems.Length];

            for (int i = 0; i < eventSystems.Length; i++)
            {
                EventSystem eventSystem = eventSystems[i];
                originalEnabledStates[i] = eventSystem.enabled;
                if (eventSystem.isActiveAndEnabled)
                {
                    eventSystem.enabled = false;
                }
            }
        }

        public void Restore()
        {
            for (int i = 0; i < eventSystems.Length; i++)
            {
                EventSystem eventSystem = eventSystems[i];
                if (eventSystem == null)
                {
                    continue;
                }
                eventSystem.enabled = originalEnabledStates[i];
            }
        }
    }
}
