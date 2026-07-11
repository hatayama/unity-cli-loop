namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A named local variable in scope at a resolved instruction, keyed by its IL slot.
    /// </summary>
    internal sealed class SourcePausePointLocalVariable
    {
        public string Name { get; }
        public int SlotIndex { get; }
        public string TypeName { get; }
        public bool IsValueType { get; }

        public SourcePausePointLocalVariable(string name, int slotIndex, string typeName, bool isValueType)
        {
            Name = name;
            SlotIndex = slotIndex;
            TypeName = typeName;
            IsValueType = isValueType;
        }
    }
}
