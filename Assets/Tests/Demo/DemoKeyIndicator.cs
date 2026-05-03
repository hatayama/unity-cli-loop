#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    public class DemoKeyIndicator : MonoBehaviour
    {
        [SerializeField] private Key targetKey;

        private static readonly Color NORMAL_COLOR = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color PRESSED_COLOR = new Color(0.3f, 0.8f, 1f, 1f);

        private Image _image = null!;

        private void Awake()
        {
            _image = GetComponent<Image>();

            // Scene file bakes modifier labels for one platform; fix at runtime
            string rawName = targetKey.ToString();
            string symbol = KeySymbolMap.GetSymbol(rawName);
            if (rawName != symbol)
            {
                Text text = GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = symbol;
                }
            }
        }

        public void Init(Key key, string label)
        {
            targetKey = key;
            _image = GetComponent<Image>();
            _image.color = NORMAL_COLOR;

            Text text = GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            bool pressed = keyboard[targetKey].isPressed;
            _image.color = pressed ? PRESSED_COLOR : NORMAL_COLOR;
            transform.localScale = pressed ? Vector3.one * 1.05f : Vector3.one;
        }
    }
}
#endif
