using UnityEngine;
using System.IO;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop
{
    public class ForDynamicAssemblyTest
    {
        public string HelloWorldInAnotherDLL()
        {
            return "Hello World";
        }

        public string TestForbiddenOperationsInAnotherDLL()
        {
            // Forbidden file operations
            File.WriteAllText("/tmp/malicious.txt", "malicious content");
            
            // Forbidden process execution
            Process.Start("notepad.exe");
            
            return "Forbidden operations executed";
        }
    }
}