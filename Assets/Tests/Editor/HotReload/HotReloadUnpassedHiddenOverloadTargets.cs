using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Public class whose public overload takes any value while a private one takes an ArraySegment and
    /// a second argument, so a call with one ArraySegment argument can never bind to the private one.
    /// The unpassed hidden overload tests never give the reload this file.
    /// </summary>
    public static class HotReloadUnpassedHiddenArityOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(ArraySegment<int> segment, int extra)
        {
            return segment.Count + extra;
        }
    }

    /// <summary>
    /// Public class whose private overloads take a params array after an ArraySegment, or an optional
    /// second parameter, so one ArraySegment argument fits each of them and fits them better than the
    /// public overload.
    /// </summary>
    public static class HotReloadUnpassedHiddenParamsOptionalOverloads
    {
        public static int Expand(object value)
        {
            return 1;
        }

        private static int Expand(ArraySegment<int> segment, params int[] more)
        {
            return segment.Count + more.Length;
        }

        public static int Default(object value)
        {
            return 1;
        }

        private static int Default(ArraySegment<int> segment, int extra = 0)
        {
            return segment.Count + extra;
        }
    }

    /// <summary>
    /// Public class whose private overloads take a public type nested in it, a constructed generic
    /// List, or the host the tests edit, which the reload is given while this file is not.
    /// </summary>
    public static class HotReloadUnpassedHiddenMappedOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(Key key)
        {
            return 2;
        }

        private static int Pick(List<int> values)
        {
            return values.Count;
        }

        private static int Pick(HotReloadUnpassedHiddenOverloadHost host)
        {
            return 2;
        }

        /// <summary>
        /// Public type nested in the class, which only its private overload takes.
        /// </summary>
        public sealed class Key
        {
        }
    }

    /// <summary>
    /// Public class whose private instance method fits an ArraySegment argument better than the public
    /// extension method of the same name that code outside the class calls on it.
    /// </summary>
    public sealed class HotReloadUnpassedHiddenInstanceTarget
    {
        private int Halve(ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Public extension class whose public extension method is the one code outside
    /// HotReloadUnpassedHiddenInstanceTarget calls, while that class's private instance method fits
    /// the argument better.
    /// </summary>
    public static class HotReloadUnpassedHiddenInstanceTargetExtensions
    {
        public static int Halve(this HotReloadUnpassedHiddenInstanceTarget target, object value)
        {
            return 1;
        }
    }

    /// <summary>
    /// Public extension class with a public extension method taking any value. Another class in the
    /// namespace declares a private extension method of the same name that fits an ArraySegment better.
    /// </summary>
    public static class HotReloadUnpassedPublicMarkExtensions
    {
        public static int Mark(this int value, object other)
        {
            return 1;
        }
    }

    /// <summary>
    /// Public extension class whose only extension method is private and takes an ArraySegment.
    /// </summary>
    public static class HotReloadUnpassedPrivateMarkExtensions
    {
        private static int Mark(this int value, ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Public base class whose public static overload takes any value while a private one takes an
    /// ArraySegment. A type nested in a derived class reaches the base's members through its outer
    /// type, where only the public one is reachable.
    /// </summary>
    public class HotReloadUnpassedHiddenOuterBase
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(ArraySegment<int> segment)
        {
            return segment.Count;
        }

        private static int Pick(HotReloadUnpassedHiddenOverloadHost host)
        {
            return 2;
        }
    }

    /// <summary>
    /// Public class whose public overload takes any value while a private one takes the host the
    /// tests edit, which the reload is given while this file is not. The host file imports it with a
    /// using static directive.
    /// </summary>
    public static class HotReloadUnpassedHostTakeOverloads
    {
        public static int Take(object value)
        {
            return 1;
        }

        private static int Take(HotReloadUnpassedHiddenOverloadHost host)
        {
            return 2;
        }
    }

    /// <summary>
    /// Public extension class with a public extension method taking any value. Another class in the
    /// namespace declares a private extension method of the same name that takes the edited host.
    /// </summary>
    public static class HotReloadUnpassedPublicHostMarkExtensions
    {
        public static int HostMark(this int value, object other)
        {
            return 1;
        }
    }

    /// <summary>
    /// Public extension class whose only extension method is private and takes the edited host.
    /// </summary>
    public static class HotReloadUnpassedPrivateHostMarkExtensions
    {
        private static int HostMark(this int value, HotReloadUnpassedHiddenOverloadHost host)
        {
            return 2;
        }
    }

    /// <summary>
    /// Public extension class with a public extension method taking any value and a private one
    /// taking the edited host, called in static form and on a receiver.
    /// </summary>
    public static class HotReloadUnpassedHostTwinExtensions
    {
        public static int HostTwin(this int value, object other)
        {
            return 1;
        }

        private static int HostTwin(this int value, HotReloadUnpassedHiddenOverloadHost host)
        {
            return 2;
        }
    }

    /// <summary>
    /// Public generic class with a nested type, so a parameter can name the edited host only through
    /// the nested type's containing type.
    /// </summary>
    public sealed class HotReloadUnpassedEnvelope<T>
    {
        /// <summary>
        /// Public type nested in the generic class.
        /// </summary>
        public sealed class Item
        {
        }
    }

    /// <summary>
    /// Public class whose public overload takes any value while a private one takes a type nested in
    /// a generic class constructed with the edited host.
    /// </summary>
    public static class HotReloadUnpassedEnvelopeOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(HotReloadUnpassedEnvelope<HotReloadUnpassedHiddenOverloadHost>.Item item)
        {
            return 2;
        }
    }

    /// <summary>
    /// Public class whose overload taking any value is internal, which the worker's binding does not
    /// import, while a private one takes an ArraySegment.
    /// </summary>
    public static class HotReloadUnpassedInternalPickOverloads
    {
        internal static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }
}

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceForeignInternal
{
    /// <summary>
    /// Extension methods of the edited assembly in a namespace another assembly also declares
    /// extension methods in, so a call written on a receiver weighs both at one scope: a public
    /// overload taking any value and a private one taking IComparable.
    /// </summary>
    public static class HotReloadUnpassedElectExtensions
    {
        public static int Elect(this int value, object other)
        {
            return 1;
        }

        private static int Elect(this int value, IComparable other)
        {
            return 2;
        }
    }
}
