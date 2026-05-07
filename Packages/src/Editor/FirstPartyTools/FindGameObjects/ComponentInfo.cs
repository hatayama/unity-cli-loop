
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents information about a Unity component
    /// </summary>
    public class ComponentInfo
    {
        public string type { get; set; }
        public string fullTypeName { get; set; }
        public ComponentPropertyInfo[] properties { get; set; }
    }
    
    /// <summary>
    /// Represents a property of a Unity component
    /// </summary>
    public class ComponentPropertyInfo
    {
        public string name { get; set; }
        public string type { get; set; }
        public object value { get; set; }
    }
}