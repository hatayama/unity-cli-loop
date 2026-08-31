
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Hoisted Literal Binding behavior for Unity CLI Loop.
    /// </summary>
    public sealed class HoistedLiteralBinding
    {
        public string ParameterName { get; }
        public string TypeName { get; }
        public object Value { get; }

        public HoistedLiteralBinding(string parameterName, string typeName, object value)
        {
            ParameterName = parameterName;
            TypeName = typeName;
            Value = value;
        }
    }
}
