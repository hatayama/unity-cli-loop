namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents information about a Unity component
    /// </summary>
    public class ComponentInfo
    {
        public string Type { get; set; }

        public string FullTypeName { get; set; }

        public ComponentPropertyInfo[] Properties { get; set; }
    }
    
    /// <summary>
    /// Represents a property of a Unity component
    /// </summary>
    public class ComponentPropertyInfo
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public object Value { get; set; }
    }
}
