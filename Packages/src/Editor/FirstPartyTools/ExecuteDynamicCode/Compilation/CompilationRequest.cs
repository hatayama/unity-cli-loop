using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation Request
    /// 
    /// Related Class: RoslynCompiler
    /// </summary>
    public class CompilationRequest
    {
        /// <summary>Source code to compile</summary>
        public string Code { get; set; }

        /// <summary>Name of the class</summary>
        public string ClassName { get; set; }

        /// <summary>Namespace of the class</summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Additional references (external DLL file paths)
        /// Specify external libraries (such as NuGet package DLLs) that are not loaded in the AppDomain
        /// by providing their file paths to be added as references during compilation
        /// Example: ["C:/MyLibrary/CustomLibrary.dll", "D:/ThirdParty/SomePackage.dll"]
        /// </summary>
        public List<string> AdditionalReferences { get; set; } = new();
        
        /// <summary>
        /// Legacy assembly loading mode hint.
        /// AssemblyBuilder always compiles against the full reference set, so this value is ignored.
        /// </summary>
        public AssemblyLoadingMode AssemblyMode { get; set; } = AssemblyLoadingMode.AllAssemblies;
    }

    /// <summary>
    /// Defines the mode for loading assemblies during compilation
    /// </summary>
    public enum AssemblyLoadingMode
    {
        /// <summary>Reference only selected assemblies</summary>
        SelectiveReference,
        
        /// <summary>Add all assemblies</summary>
        AllAssemblies
    }
}
