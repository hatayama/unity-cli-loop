using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A compiled host in an assembly that references the object-creation fixtures, so an added
    /// field's initializer can create a type of a referenced project assembly instead of a type of
    /// the assembly being edited.
    /// </summary>
    public class HotReloadObjectCreationCrossAssemblyHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return -1;
        }
    }
}
