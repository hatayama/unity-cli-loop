using System;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Utility for binding UI Toolkit elements to data without triggering change events.
    /// SetValueWithoutNotify prevents infinite loops when updating UI from model changes.
    /// </summary>
    public static class ViewDataBinder
    {
        public static void UpdateToggle(Toggle toggle, bool value)
        {
            toggle.SetValueWithoutNotify(value);
        }

        public static void UpdateEnumField<T>(EnumField field, T value) where T : Enum
        {
            field.SetValueWithoutNotify(value);
        }

        public static void UpdateFoldout(Foldout foldout, bool value)
        {
            foldout.SetValueWithoutNotify(value);
        }

        public static void SetVisible(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static void ToggleClass(VisualElement element, string className, bool condition)
        {
            if (condition)
            {
                element.AddToClassList(className);
            }
            else
            {
                element.RemoveFromClassList(className);
            }
        }
    }
}
