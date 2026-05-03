using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateKeyboardSchema : BaseToolSchema
    {
        [Description("Keyboard action: Press(0) - one-shot key tap (Down then Up), KeyDown(1) - hold key down, KeyUp(2) - release held key")]
        public KeyboardAction Action { get; set; } = KeyboardAction.Press;

        [Description("Key name matching the Input System Key enum (e.g. \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\"). Case-insensitive.")]
        public string Key { get; set; } = "";

        [Description("Hold duration in seconds for Press action (default: 0 = one-shot tap). Ignored by KeyDown/KeyUp.")]
        public float Duration { get; set; } = 0f;
    }
}
