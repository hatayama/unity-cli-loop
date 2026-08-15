using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Other file of <see cref="HotReloadAddedMemberPartialHost"/> so the compiled type
    /// has members this file does not declare.
    /// </summary>
    public partial class HotReloadAddedMemberPartialHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PartialOtherFile()
        {
            return 3;
        }
    }
}
