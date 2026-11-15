using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A struct whose internal members a patched closure cannot reach from the shim assembly.
    /// </summary>
    public struct HotReloadStructMemberAccessorStruct
    {
        internal static int Count;

        internal int X;

        public int Open;

        internal int Value
        {
            get { return X; }
            set { X = value; }
        }

        public int Level
        {
            get { return X; }
            internal set { X = value; }
        }

        internal static int StaticCompute(int input)
        {
            return input * 2;
        }

        internal int Compute(int input)
        {
            return X + input;
        }

        public int OpenCompute(int input)
        {
            return Open + input;
        }

        internal void Bump()
        {
            X++;
        }

        internal event Action Fired;

        internal void Raise()
        {
            Fired?.Invoke();
        }

        /// <summary>
        /// A class nested in the struct that declares its own field-like event with the same name
        /// and type as the struct's, so a lookup on the edited type alone finds a backing field.
        /// </summary>
        public class TwinWatcher
        {
            internal event Action Fired;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Peek(HotReloadStructMemberAccessorStruct target)
            {
                Fired?.Invoke();
                Func<bool> unsubscribed = () => target.Fired == null;
                return unsubscribed() ? 9101 : 9102;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Clear(HotReloadStructMemberAccessorStruct target)
            {
                Func<int> clear = () =>
                {
                    target.Fired = null;
                    return 9201;
                };
                return clear();
            }
        }

        /// <summary>
        /// A class nested in the struct, which (unlike code outside the struct) may read the
        /// struct's field-like event as its backing field.
        /// </summary>
        public class Watcher
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Peek(HotReloadStructMemberAccessorStruct target)
            {
                Func<bool> unsubscribed = () => target.Fired == null;
                return unsubscribed() ? 9001 : 9002;
            }
        }
    }

    /// <summary>
    /// A class host that holds the struct in a private field and reaches each kind of struct
    /// member from inside a lambda (or an async body), one method per shape.
    /// </summary>
    public class HotReloadStructMemberAccessorHost
    {
        private HotReloadStructMemberAccessorStruct _s;
        private int _hostPrivate;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Ordinary()
        {
            return 8901;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadField()
        {
            Func<int> read = () => _s.X + 8101;
            return read();
        }

        public async Task<int> ReadFieldAsync()
        {
            await Task.Yield();
            return _s.X + 8151;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int WriteField()
        {
            Action write = () =>
            {
                _s.X = 8201;
                _s.X += 1;
                _s.X++;
            };
            write();
            return _s.X;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int WriteFieldOnly()
        {
            // Why no read back: the body must reach the member only through the write.
            Action write = () => { _s.X = 8221; };
            write();
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SetPropertyOnly()
        {
            // Why no read back: the body must reach the member only through the write.
            Action write = () => { _s.Value = 8521; };
            write();
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadPropertyOnly()
        {
            Func<int> read = () => _s.Value + 8541;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int MethodGroup()
        {
            Func<int> call = () =>
            {
                Func<int, int> compute = _s.Compute;
                return compute(8451);
            };
            return call();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DeconstructProperty()
        {
            Func<int> touch = () =>
            {
                int other;
                (_s.Value, other) = (8561, 1);
                return other;
            };
            return touch();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int IncrementProperty()
        {
            Func<int> touch = () =>
            {
                _s.Level++;
                return 8571;
            };
            return touch();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticField()
        {
            Func<int> touch = () =>
            {
                HotReloadStructMemberAccessorStruct.Count = 8301;
                return HotReloadStructMemberAccessorStruct.Count;
            };
            return touch();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int InstanceMethod()
        {
            Func<int> call = () => _s.Compute(8401);
            return call();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Property()
        {
            Func<int> touch = () =>
            {
                _s.Value = 8501;
                return _s.Value;
            };
            return touch();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticMethod()
        {
            Func<int> call = () => HotReloadStructMemberAccessorStruct.StaticCompute(8601);
            return call();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int MutatingMethod()
        {
            Func<int> call = () =>
            {
                _s.Bump();
                return _s.Compute(8801);
            };
            return call();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Control()
        {
            Func<int> touch = () =>
            {
                _s.Open = 8701;
                return _s.OpenCompute(_hostPrivate);
            };
            return touch();
        }
    }
}
