using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class PreparedDynamicCode
    {
        public string PreparedSource { get; }
        public bool IsScriptMode { get; }
        public List<HoistedLiteralBinding> HoistedLiteralBindings { get; }

        public PreparedDynamicCode(
            string preparedSource,
            bool isScriptMode,
            List<HoistedLiteralBinding> hoistedLiteralBindings)
        {
            PreparedSource = preparedSource;
            IsScriptMode = isScriptMode;
            HoistedLiteralBindings = hoistedLiteralBindings ?? new List<HoistedLiteralBinding>();
        }
    }
}
