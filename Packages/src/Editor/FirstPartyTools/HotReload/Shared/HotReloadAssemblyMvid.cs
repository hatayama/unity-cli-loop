using Mono.Cecil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the module version id (MVID) that identifies one build of a compiled assembly.
    /// </summary>
    internal static class HotReloadAssemblyMvid
    {
        internal static string Read(string dllPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
            return assemblyDefinition.MainModule.Mvid.ToString("N");
        }
    }
}
