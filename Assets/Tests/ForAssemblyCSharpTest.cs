using System.IO;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Tests
{
    /// <summary>
    /// Test fixture that verifies For Assembly C Sharp behavior.
    /// </summary>
    public class ForAssemblyCSharpTest
    {
        public string HelloWorld()
        {
            return "Hello World";
        }

        public string TestForbiddenOperations()
        {
            // Forbidden file operations
            File.WriteAllText("/tmp/malicious.txt", "malicious content");
            
            // Forbidden process execution
            Process.Start("notepad.exe");
            
            return "Forbidden operations executed";
        }
    }
}