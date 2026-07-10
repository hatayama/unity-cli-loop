namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A method parameter, in declaration order (excludes the implicit "this").
    /// </summary>
    internal sealed class SourcePausePointParameter
    {
        public string Name { get; }
        public int Index { get; }
        public string TypeName { get; }

        public SourcePausePointParameter(string name, int index, string typeName)
        {
            Name = name;
            Index = index;
            TypeName = typeName;
        }
    }
}
