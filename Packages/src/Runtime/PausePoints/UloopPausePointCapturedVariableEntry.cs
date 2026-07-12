#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// One demangled captured variable before string formatting for CLI responses.
    /// </summary>
    internal sealed class UloopPausePointCapturedVariableEntry
    {
        public UloopPausePointCapturedVariableEntry(string name, string scope, object value)
        {
            Name = name;
            Scope = scope;
            Value = value;
        }

        public string Name { get; }
        public string Scope { get; }
        public object Value { get; }
    }
}
#endif
