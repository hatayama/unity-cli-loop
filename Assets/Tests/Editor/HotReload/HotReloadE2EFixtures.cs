using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using UnityEngine;

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
    /// Const drift e2e enum: the test template pairs this with an injectable declaration so a
    /// test can change only a member value.
    /// </summary>
    public enum HotReloadE2EMode
    {
        Idle = 0,
        Active = 1
    }

    /// <summary>
    /// Compiled fixture whose on-disk source path is passed as <c>files[]</c> to the
    /// orchestrator. Edited copies for worker input live under
    /// <c>Library/UloopHotReload/TestSources/</c> (never under Assets).
    /// </summary>
    public class HotReloadE2EFixture : HotReloadE2EBase, IHotReloadE2EMarker
    {
        private int _secret = 10;
        // Const drift e2e: the test template pairs this with an injectable declaration so a
        // test can change only the value.
        private const int TuningConst = 3;
        private Action _callback;
        private int? Score { get; set; }
        private int Value { get; set; }

        public int SecretForAssert => _secret;

        // Explicit-body getter — worker must report get_ExplicitBodyGetter as Skipped (not silent).
        public int ExplicitBodyGetter
        {
            get { return _secret; }
        }

        // Explicit-body setter — worker must report set_ExplicitBodySetter as Skipped (not silent).
        public int ExplicitBodySetter
        {
            set { _secret = value; }
        }

        public int Counter;

        public HotReloadE2EFixture Next;

        // Property getter receiver — compound writes must not double-evaluate this.
        public HotReloadE2EFixture Current
        {
            get { return this; }
        }

        private int this[int index] => _secret + index;

        public int VisibleSibling()
        {
            return 1;
        }

        public static int VisibleStaticSibling()
        {
            return 2;
        }

        // Why NoInlining on every patch target below: these fixtures verify detour mechanics,
        // and without the attribute the x64 Mono JIT can inline the tiny original bodies into
        // a test method that was JIT-compiled before the patch was applied, so the assertions
        // would measure JIT inlining instead of patching.

        // Sentinel body: hot reload must replace this with a private-touching shim that returns
        // _secret + delta + 100.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ComputeWithPrivate(int delta)
        {
            return _secret + delta;
        }

        // Interface-typed local read once — Release shim optimization can drop `cells` from PDB
        // locals while Debug keeps it for pause-point capture (FB round-2 severity 6).
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SummarizeCells()
        {
            IReadOnlyList<int> cells = BuildCells();
            int total = cells.Count;
            return total;
        }

        // Why AggressiveInlining: Debug still treats these as at-risk so orchestrator aggregation
        // / dedupe E2E can run without requiring Release code optimization.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int InlineRiskAlpha()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int InlineRiskBeta()
        {
            return 2;
        }

        private List<int> BuildCells()
        {
            return new List<int> { 1, 2, 3 };
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
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SumGrid(int[,] grid)
        {
            return -1;
        }

        // Patch target for the struct-return control-flow e2e (foreign-label regression).
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Vector3 CenterOfCell(Vector3Int cell)
        {
            return Vector3.zero;
        }

        // Nested constructed generic parameter — worker manifest must emit Cecil's FullName shape.
        public int CountEnumerator(List<int>.Enumerator enumerator)
        {
            return 0;
        }

        // Bare instance/static sibling calls — the transplant shim must qualify receivers.
        public int CallsBareSiblings()
        {
            return VisibleSibling() + VisibleStaticSibling();
        }

        // Private field read plus bare visible sibling — delegation entry with qualified sibling.
        public async Task<int> AsyncPrivateAndBareSibling()
        {
            await Task.Yield();
            return _secret + VisibleSibling();
        }

        // ?. over an inaccessible member — worker must skip.
        public async Task<int> AsyncConditionalPrivateField()
        {
            await Task.Yield();
            return Next?._secret ?? 0;
        }

        // Private delegate field invoke — delegation with FieldRef read then invoke.
        public async Task AsyncInvokePrivateDelegate()
        {
            await Task.Yield();
            _callback();
        }

        // Receiver-qualified private delegate invoke (`this._callback()`) — same FieldRef shape.
        public async Task AsyncInvokePrivateDelegateOnThis()
        {
            await Task.Yield();
            this._callback();
        }

        // Parameter-receiver private delegate invoke (`other._callback()`) — FieldRef on other.
        public async Task AsyncInvokePrivateDelegateOnOther(HotReloadE2EFixture other)
        {
            await Task.Yield();
            other._callback();
        }

        // Private property ??= — worker must skip (no conditional-write rewrite shape).
        public async Task AsyncNullCoalesceAssignPrivateProperty()
        {
            await Task.Yield();
            Score ??= 5;
        }

        // Compound write through a property getter receiver — worker must skip (double-eval).
        public async Task AsyncCompoundWriteViaPropertyReceiver()
        {
            await Task.Yield();
            Current.Value += 1;
        }

        private void BumpSecretBy(int amount)
        {
            _secret += amount;
        }

        private int HiddenScore { get; set; } = 3;

        // v2 e2e (1): async body with private field write + private method call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncPrivateFieldAndMethod(int delta)
        {
            await Task.Yield();
            return _secret + delta;
        }

        // v2 e2e (2): iterator body with the same private accesses.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerator IteratePrivate(int delta)
        {
            yield return _secret + delta;
        }

        // v2 e2e (3): lambda capture reading a private field.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int LambdaPrivate(int threshold)
        {
            Func<int, bool> pred = v => v < _secret;
            return pred(threshold) ? 1 : 0;
        }

        // v2 e2e (4): private property read/write round-trip.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PropertyPrivateRoundTrip(int value)
        {
            HiddenScore = value;
            return HiddenScore;
        }

        // v2 e2e (5): async body that names an internal type — must stay Skipped (condition c).
        public async Task<int> AsyncUsesInternalType()
        {
            await Task.Yield();
            HotReloadE2EInternalToken token = new HotReloadE2EInternalToken { N = 1 };
            return token.N;
        }
    }

    /// <summary>
    /// Internal type used only by <see cref="HotReloadE2EFixture.AsyncUsesInternalType"/> to
    /// pin the v2 "type as type" skip (accessor delegates cannot rescue type mentions).
    /// </summary>
    internal class HotReloadE2EInternalToken
    {
        public int N;
    }
}
