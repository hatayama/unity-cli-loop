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
}
