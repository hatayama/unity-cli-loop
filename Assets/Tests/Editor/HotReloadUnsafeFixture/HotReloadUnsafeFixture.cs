using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadUnsafeFixture
{
    // Fixture for hot reload of members that need an unsafe context. One small type per shape;
    // every body holds a unique number the tests edit, so one edit touches exactly one member.

    public class UnsafeFixtureSafeHost
    {
        public int Read()
        {
            return 1000;
        }
    }

    public class UnsafeFixtureAddedMethodHost
    {
        public int Read()
        {
            return 1010;
        }
    }

    public class UnsafeFixtureBlockHost
    {
        public int Read()
        {
            int x = 1;
            unsafe
            {
                int* p = &x;
                *p += 1100;
            }

            return x;
        }
    }

    public class UnsafeFixtureModifierHost
    {
        public unsafe int Read()
        {
            int x = 2;
            int* p = &x;
            return *p + 1200;
        }
    }

    public unsafe class UnsafeFixtureClassPointerHost
    {
        public int Read()
        {
            int x = 3;
            int* p = &x;
            return *p + 1300;
        }
    }

    public unsafe class UnsafeFixtureClassSafeHost
    {
        public int Read()
        {
            return 1350;
        }
    }

    public class UnsafeFixturePointerParameterHost
    {
        public static unsafe int Read(int* p)
        {
            return *p + 1400;
        }
    }

    public class UnsafeFixturePointerReturnHost
    {
        public static unsafe int* Offset(int* p)
        {
            return p + (1450 - 1450);
        }
    }

    public class UnsafeFixturePointerGetterHost
    {
        public unsafe int Value
        {
            get
            {
                int x = 5;
                int* p = &x;
                return *p + 1500;
            }
        }
    }

    public class UnsafeFixturePointerSetterHost
    {
        public int Stored;

        public unsafe int Value
        {
            set
            {
                int v = value;
                int* p = &v;
                Stored = *p + 1550;
            }
        }
    }

    public class UnsafeFixturePointerIndexerHost
    {
        public unsafe int this[int i]
        {
            get
            {
                int* p = &i;
                return *p + 1600;
            }
        }
    }

    public class UnsafeFixturePointerTypedPropertyHost
    {
        public unsafe int* Pointer
        {
            get
            {
                return (int*)(IntPtr)1650;
            }
        }
    }

    public class UnsafeFixtureLambdaHost
    {
        public int Read()
        {
            Func<int> read = () =>
            {
                int x = 6;
                unsafe
                {
                    int* p = &x;
                    return *p + 1700;
                }
            };
            return read();
        }
    }

    public class UnsafeFixtureLocalFunctionHost
    {
        public int Read()
        {
            unsafe int Local()
            {
                int x = 6;
                int* p = &x;
                return *p + 1750;
            }

            return Local();
        }
    }

    public class UnsafeFixtureFixedHost
    {
        public unsafe int Read()
        {
            int[] values = { 7 };
            fixed (int* p = values)
            {
                return *p + 1800;
            }
        }
    }

    public class UnsafeFixtureFunctionPointerHost
    {
        public unsafe int Read()
        {
            delegate*<int, int> twice = &Twice;
            return twice(1850);
        }

        private static int Twice(int x)
        {
            return x * 2;
        }
    }

    public static unsafe class UnsafeFixturePointerSource
    {
        private static int _value = 9;

        public static int* Get()
        {
            fixed (int* p = &_value)
            {
                return p;
            }
        }

        public static int Read(int* p)
        {
            return *p;
        }
    }

    public class UnsafeFixtureImplicitPointerHost
    {
        public unsafe int Read()
        {
            // Why var: the shape under test is a body that handles a pointer with no pointer syntax.
            var p = UnsafeFixturePointerSource.Get();
            return UnsafeFixturePointerSource.Read(p) + 1900;
        }
    }

    public struct UnsafeFixtureStruct
    {
        public int A;
        public int B;
    }

    public class UnsafeFixtureSizeOfStructHost
    {
        public unsafe int Read()
        {
            return sizeof(UnsafeFixtureStruct) + 1950;
        }
    }

    public class UnsafeFixtureStackallocSpanHost
    {
        public int Read()
        {
            Span<int> values = stackalloc int[4];
            values[0] = 2000;
            return values[0];
        }
    }

    public class UnsafeFixtureSizeOfIntHost
    {
        public int Read()
        {
            return sizeof(int) + 2050;
        }
    }

    public class UnsafeFixtureModifierWithoutPointerHost
    {
        public unsafe int Read()
        {
            return 2100;
        }
    }

    public class UnsafeFixtureBitwiseHost
    {
        public int Read()
        {
            int a = 6;
            int b = 3;
            return (a & b) + (a * b) + 2150;
        }
    }

    public class UnsafeFixtureTypeOfPointerHost
    {
        public string Read()
        {
            return typeof(int*).Name + typeof(int*[]).Name + typeof(delegate*<int, void>).Name + typeof(delegate*<int*, void>).Name + 2200;
        }
    }

    public class UnsafeFixtureBlockWithoutPointerHost
    {
        public int Read()
        {
            int counter = 2350;
            unsafe
            {
                counter++;
            }

            return counter;
        }
    }

    public class UnsafeFixtureLocalFunctionWithoutPointerHost
    {
        public int Read()
        {
            unsafe int Local()
            {
                return 2400;
            }

            return Local();
        }
    }

    public class UnsafeFixtureImplicitPointerOnlyHost
    {
        public unsafe int Read()
        {
            // Why var: the shape under test handles a pointer with no pointer syntax and no call
            // that takes a pointer parameter.
            var p = UnsafeFixturePointerSource.Get();
            return p == null ? 2450 : 2451;
        }
    }

    public static class UnsafeFixtureOptionalPointerApi
    {
        public static unsafe int Count(int* values = null)
        {
            return values == null ? 0 : *values;
        }
    }

    public class UnsafeFixtureOptionalPointerCallHost
    {
        public unsafe int Read()
        {
            return UnsafeFixtureOptionalPointerApi.Count() + 2300;
        }
    }

    public class UnsafeFixtureOptionalPointerBag : System.Collections.IEnumerable
    {
        public int Total;

        public unsafe void Add(int value, int* extra = null)
        {
            Total += value;
        }

        public System.Collections.IEnumerator GetEnumerator()
        {
            return null;
        }
    }

    public class UnsafeFixtureCollectionInitializerHost
    {
        public unsafe int Read()
        {
            UnsafeFixtureOptionalPointerBag bag = new UnsafeFixtureOptionalPointerBag { 2500 };
            return bag.Total;
        }
    }

    public class UnsafeFixturePointerTypeArgumentHost
    {
        public int Read()
        {
            List<int*[]> values = new List<int*[]>();
            return values.Count + 2250;
        }
    }
}
