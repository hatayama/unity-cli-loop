using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Value object containing GameObject search results
    /// </summary>
    public class GameObjectDetails
    {
        public bool Found { get; set; }
        public string ErrorMessage { get; set; }
        public GameObject GameObject { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsActive { get; set; }
    }
}