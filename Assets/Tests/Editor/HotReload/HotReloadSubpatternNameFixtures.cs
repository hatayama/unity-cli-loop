using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Bodies whose property patterns name members of the matched value. The matched value is always
    /// another instance whose state differs from this one, so a pattern that read this instance
    /// instead would not match. Each body holds a 4-digit token unique in this file, which a test
    /// edits to make that one method changed.
    /// </summary>
    public sealed class HotReloadSubpatternNameHost
    {
        private int _secret;

        public HotReloadSubpatternNameHost()
            : this(7)
        {
        }

        public HotReloadSubpatternNameHost(int secret)
        {
            _secret = secret;
        }

        public int Visible => _secret;

        public int VisibleWithPrivateGetter
        {
            private get => _secret;
            set => _secret = value;
        }

        public HotReloadSubpatternNameHost Me => this;

        private int Secret => _secret;

        private HotReloadSubpatternNameHost Self => this;

        // Bodies whose own statements run as IL copied into the patched method.

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivatePatternInOwnStatements()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            return other is { Secret: 3 } ? 8011 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PublicNestedPatternInOwnStatements()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            return other is { Me: { Visible: 3 } } ? 8021 : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateNestedPatternInOwnStatements()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            return other is { Self: { Secret: 3 } } ? 8031 : 0;
        }

        // Bodies the shim assembly compiles as code of its own.

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PublicPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Visible: 3 } ? 8041 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PublicNestedPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Me: { Visible: 3 } } ? 8051 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateFieldPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { _secret: 3 } ? 8061 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivatePropertyPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Secret: 3 } ? 8071 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateGetterPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { VisibleWithPrivateGetter: 3 } ? 8081 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateNestedPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Self: { _secret: 3 } } ? 8091 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateHeadPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Self: { Visible: 3 } } ? 8101 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivateTailPatternInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other is { Me: { Secret: 3 } } ? 8111 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivatePatternOnThisInLambda()
        {
            Func<int> check = () => this is { _secret: 7 } ? 8121 : 0;
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrivatePatternInSwitchArmInLambda()
        {
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            Func<int> check = () => other switch
            {
                { Secret: 3 } => 8131,
                _ => 0,
            };
            return check();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> PrivatePatternInAsync()
        {
            await Task.Yield();
            HotReloadSubpatternNameHost other = new HotReloadSubpatternNameHost(3);
            return other is { Secret: 3 } ? 8141 : 0;
        }
    }
}
