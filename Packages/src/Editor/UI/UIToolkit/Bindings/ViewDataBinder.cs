using System;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Utility for binding UI Toolkit elements to data without triggering change events.
    /// SetValueWithoutNotify prevents infinite loops when updating UI from model changes.
    /// </summary>
    public static class ViewDataBinder
    {
        public static void BindToggle(Toggle toggle, Func<bool> getter, Action<bool> onChanged)
        {
            toggle.SetValueWithoutNotify(getter());
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        }

        public static void BindIntegerField(IntegerField field, Func<int> getter, Action<int> onChanged)
        {
            field.SetValueWithoutNotify(getter());
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        }

        public static void BindEnumField<T>(EnumField field, Func<T> getter, Action<T> onChanged) where T : Enum
        {
            field.Init(getter());
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is T newValue)
                {
                    onChanged(newValue);
                }
            });
        }

        public static void BindFoldout(Foldout foldout, Func<bool> getter, Action<bool> onChanged)
        {
            foldout.SetValueWithoutNotify(getter());
            foldout.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        }

        public static void BindButton(Button button, Action onClick)
        {
            button.clicked += onClick;
        }

        public static void BindLabel(Label label, Action<Label> onClick)
        {
            label.RegisterCallback<ClickEvent>(evt => onClick(label));
        }

        public static void UpdateToggle(Toggle toggle, bool value)
        {
            toggle.SetValueWithoutNotify(value);
        }

        public static void UpdateIntegerField(IntegerField field, int value)
        {
            field.SetValueWithoutNotify(value);
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

        public static void SetEnabled(VisualElement element, bool enabled)
        {
            element.SetEnabled(enabled);
        }

        public static void AddModifierClass(VisualElement element, string baseClass, string modifier, bool condition)
        {
            string modifierClass = $"{baseClass}--{modifier}";
            if (condition)
            {
                element.AddToClassList(modifierClass);
            }
            else
            {
                element.RemoveFromClassList(modifierClass);
            }
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
