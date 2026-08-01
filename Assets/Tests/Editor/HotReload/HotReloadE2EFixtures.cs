using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Base type used by the e2e fixture to exercise the worker's base-call skip path.
    /// </summary>
    public class HotReloadE2EBase
    {
        protected int BaseSeed()
        {
            return 1;
        }
    }

    /// <summary>
    /// Sibling type in the same namespace — bare references in edited bodies prove shims are
    /// emitted inside the original namespace (not the global namespace).
    /// </summary>
    public class HotReloadE2ESibling
    {
        public int Value;
    }

    public interface IHotReloadE2EMarker
    {
        int ExplicitPing();
    }

    /// <summary>
    /// Compiled fixture whose on-disk source path is passed as <c>files[]</c> to the
    /// orchestrator. Edited copies for worker input live under
    /// <c>Library/UloopHotReload/TestSources/</c> (never under Assets).
    /// </summary>
    public class HotReloadE2EFixture : HotReloadE2EBase, IHotReloadE2EMarker
    {
        private int _secret = 10;

        public int SecretForAssert => _secret;

        public int Counter;

        private int this[int index] => _secret + index;

        // Sentinel body: hot reload must replace this with a private-touching shim that returns
        // _secret + delta + 100.
        public int ComputeWithPrivate(int delta)
        {
            return _secret + delta;
        }

        // Contains base. — worker must skip with an explicit reason (not an error).
        public int CallsBase()
        {
            return base.BaseSeed() + 1;
        }

        // Edited copy will call a non-existent helper so shim compile fails with a new-member hint.
        public int CallsMissingHelper(int value)
        {
            return value;
        }

        // Explicit interface implementation — must be Skipped so its dotted metadata name cannot
        // poison shim compilation for the rest of the file.
        int IHotReloadE2EMarker.ExplicitPing()
        {
            return _secret;
        }

        // Sync method + query syntax referencing a private field — worker emits a delegation
        // entry (accessor rewrite); orchestrator leaves it unpatched until the delegation pass.
        public int QueryPrivate()
        {
            int[] values = { 1, 2, 3 };
            return (from value in values where value < _secret select value).Count();
        }

        // Async body reading a private indexer — still Skipped at the worker (no rewrite shape).
        public async Task<int> AsyncReadPrivateIndexer()
        {
            await Task.Yield();
            return this[0];
        }

        // Multidimensional array parameter — exercises Cecil FullName `[0...,0...]` matching.
        public int SumGrid(int[,] grid)
        {
            return -1;
        }

        // Nested constructed generic parameter — worker manifest must emit Cecil's FullName shape.
        public int CountEnumerator(List<int>.Enumerator enumerator)
        {
            return 0;
        }
    }
}
