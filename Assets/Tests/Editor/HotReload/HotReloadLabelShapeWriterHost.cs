using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose writers take a constructed generic and a multidimensional array, the
    /// parameter shapes reflection and metadata spell differently, and a reader the hot reload
    /// tests edit to read what a writer assigns.
    /// </summary>
    public class HotReloadLabelShapeWriterHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int WriteFromList(List<int> values)
        {
            return values.Count;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int WriteFromGrid(int[,] values)
        {
            return values.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(int value)
        {
            return value;
        }
    }
}
