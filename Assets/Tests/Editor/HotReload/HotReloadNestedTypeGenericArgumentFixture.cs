using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Methods that name a type nested in this class as a generic type argument, by its simple name,
    /// in bodies, signatures, generic calls, type tests, accessors, and lambdas. The shim class sits
    /// outside this type, so each such name must be qualified there.
    /// </summary>
    public class HotReloadNestedTypeGenericArgumentFixture
    {
        public enum Mode
        {
            Off,
            On
        }

        public class Item
        {
            public int Value;
        }

        public class Box<T>
        {
            public T Content;
        }

        public static int LastCount;

        public List<Mode> ModeList
        {
            get { return new List<Mode> { Mode.On }; }
            set { LastCount = value.Count + 7500; }
        }

        public static T Pick<T>()
        {
            return default(T);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int BodyList()
        {
            List<Mode> list = new List<Mode>();
            list.Add(Mode.On);
            return list.Count + 7100;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Dictionary<string, Item> SignatureGeneric(List<Mode> modes)
        {
            return new Dictionary<string, Item> { { "k", new Item { Value = modes.Count + 7200 } } };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GenericMethodArgument()
        {
            Mode picked = Pick<Mode>();
            int empty = Enumerable.Empty<Item>().Count();
            return (int)picked + empty + 7300;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TypeTestsAndCast()
        {
            object boxed = new List<Mode> { Mode.On };
            Type listType = typeof(List<Mode>);
            List<Mode> cast = (List<Mode>)boxed;
            return (boxed is List<Mode> ? 1 : 0) + cast.Count + (listType.IsGenericType ? 1 : 0) + 7400;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int LambdaGeneric()
        {
            Func<List<Mode>> make = () => new List<Mode> { Mode.On };
            return make().Count + 7600;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Controls()
        {
            Mode[] array = { Mode.On };
            Mode? maybe = Mode.On;
            (Mode, int) pair = (Mode.On, 1);
            List<HotReloadNestedTypeGenericArgumentFixture.Mode> qualified =
                new List<HotReloadNestedTypeGenericArgumentFixture.Mode>();
            return array.Length + (maybe.HasValue ? 1 : 0) + pair.Item2 + qualified.Count + 7700;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int NestedGenericBox()
        {
            Box<Mode> box = new Box<Mode> { Content = Mode.On };
            return (int)box.Content + 7800;
        }
    }
}
