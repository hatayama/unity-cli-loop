namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class HoistedLiteralBinding
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
