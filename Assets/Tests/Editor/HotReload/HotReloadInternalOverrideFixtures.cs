using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Supplies compiled internal virtual members for introduced-type refusal tests.
    /// </summary>
    public class HotReloadInternalOverrideBase
    {
        protected int Count;

        internal virtual int Read() => 1;
        internal virtual int Value => 1;
        internal virtual int this[int index] => index;
        internal virtual event Action Changed { add { } remove { } }

        public int InvokeRead() => Read();
        public int InvokeValue() => Value;
        public int InvokeIndexer() => this[1];
        public void InvokeEvent(Action handler) => Changed += handler;
    }

    /// <summary>
    /// Supplies compiled protected internal virtual members for introduced-type refusal tests.
    /// </summary>
    public class HotReloadProtectedInternalOverrideBase
    {
        protected int Count;

        protected internal virtual int Read() => 1;
        protected internal virtual int Value => 1;
        protected internal virtual int this[int index] => index;
        protected internal virtual event Action Changed { add { } remove { } }

        public int InvokeRead() => Read();
        public int InvokeValue() => Value;
        public int InvokeIndexer() => this[1];
        public void InvokeEvent(Action handler) => Changed += handler;
    }

    /// <summary>
    /// Supplies compiled private protected virtual members for introduced-type refusal tests.
    /// </summary>
    public class HotReloadPrivateProtectedOverrideBase
    {
        protected int Count;

        private protected virtual int Read() => 1;
        private protected virtual int Value => 1;
        private protected virtual int this[int index] => index;
        private protected virtual event Action Changed { add { } remove { } }

        public int InvokeRead() => Read();
        public int InvokeValue() => Value;
        public int InvokeIndexer() => this[1];
        public void InvokeEvent(Action handler) => Changed += handler;
    }

    /// <summary>
    /// Supplies virtual members whose overrides can cross the artifact assembly boundary.
    /// </summary>
    public class HotReloadVisibleOverrideBase
    {
        public virtual int Read() => 1;
        protected virtual int Value => 2;
        public int InvokeValue() => Value;
    }
}
