using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>Interface with a generic method, explicitly implemented by the fixture below.</summary>
    public interface IHotReloadTypeParameterRenameEcho
    {
        int Echo<T>(T value);
    }

    /// <summary>Generic interface whose property a generic host implements explicitly.</summary>
    public interface IHotReloadTypeParameterRenameBox<T>
    {
        T Value { get; }
    }

    /// <summary>
    /// Compiled generic methods for worker tests that rename or swap a type parameter in the edited
    /// source: a swap, a type parameter only in the return type, an overload set, an explicit
    /// implementation, and a caller of a nested generic type's method.
    /// </summary>
    public sealed class HotReloadTypeParameterRenameWorkerMethods : IHotReloadTypeParameterRenameEcho
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Swap<T, U>(T first, U second)
        {
            return 1100;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public T Pick<T>(int index)
        {
            return (T)(object)(index + 1200);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Overload<T>(T value)
        {
            return 1300;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Overload(int value)
        {
            return 1400;
        }

        int IHotReloadTypeParameterRenameEcho.Echo<T>(T value)
        {
            return 1500;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadStored()
        {
            return new HotReloadTypeParameterRenameFieldHost<int>().Stored + 1750;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallNested()
        {
            return new HotReloadTypeParameterRenameOuter<int>.Inner<int>().Read(1, 2) + 1600;
        }
    }

    /// <summary>Generic host with a field of its type parameter's type.</summary>
    public sealed class HotReloadTypeParameterRenameFieldHost<T>
    {
        public T Stored;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return 1700;
        }
    }

    /// <summary>Generic host with a property of its type parameter's type.</summary>
    public sealed class HotReloadTypeParameterRenamePropertyHost<T>
    {
        private T _stored;

        public T Stored
        {
            get
            {
                int marker = 1800;
                return marker > 0 ? _stored : default(T);
            }
            set { _stored = value; }
        }
    }

    /// <summary>Generic host with an indexer whose index is its type parameter.</summary>
    public sealed class HotReloadTypeParameterRenameIndexerHost<T>
    {
        public int this[T key]
        {
            get { return 1900; }
            set { }
        }
    }

    /// <summary>Generic host that explicitly implements a generic interface's property.</summary>
    public sealed class HotReloadTypeParameterRenameExplicitHost<T> : IHotReloadTypeParameterRenameBox<T>
    {
        private T _stored;

        T IHotReloadTypeParameterRenameBox<T>.Value
        {
            get
            {
                int marker = 2000;
                return marker > 0 ? _stored : default(T);
            }
        }
    }

    /// <summary>Generic type nested in a generic type, whose method names both type parameters.</summary>
    public sealed class HotReloadTypeParameterRenameOuter<T>
    {
        public sealed class Inner<U>
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Read(T first, U second)
            {
                return 2100;
            }
        }
    }

    /// <summary>
    /// Non-generic type with a nested class named T, an ordinary method over that class, and a
    /// generic overload whose own type parameter is also named T.
    /// </summary>
    public sealed class HotReloadTypeParameterRenameNestedNameHost
    {
        public sealed class T
        {
            public int Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Run(T value)
        {
            return 2200;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Run<T>(T value)
        {
            return 2300;
        }
    }

    /// <summary>Generic interface whose method a generic host implements explicitly.</summary>
    public interface IHotReloadTypeParameterRenameSink<T>
    {
        int Take(T value);
    }

    /// <summary>Generic host that explicitly implements a generic interface's method.</summary>
    public sealed class HotReloadTypeParameterRenameExplicitMethodHost<T> : IHotReloadTypeParameterRenameSink<T>
    {
        int IHotReloadTypeParameterRenameSink<T>.Take(T value)
        {
            return 2500;
        }
    }

    /// <summary>Generic host with a conversion operator to its type parameter.</summary>
    public sealed class HotReloadTypeParameterRenameConversionHost<T>
    {
        public T Stored;

        public static implicit operator T(HotReloadTypeParameterRenameConversionHost<T> host)
        {
            return host.Stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return 2600;
        }
    }

    /// <summary>Generic host whose method has a comment between its parameter type and name.</summary>
    public sealed class HotReloadTypeParameterRenameCommentHost<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Take(T /* first */ value)
        {
            return 2700;
        }
    }
}
