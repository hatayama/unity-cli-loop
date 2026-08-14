using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host type for added-member worker tests. Tests copy this source, add members,
    /// and run the transform worker against the already-compiled assembly as ground truth.
    /// </summary>
    public class HotReloadAddedMemberHost
    {
        public int PublicSeed = 3;

        public HotReloadAddedMemberHost Inner;

        private int _privateSeed = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingValue()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingCaller(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingFail(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ArrowRead() => 1;

        public int ExistingGetter
        {
            get { return 1; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingDynamic(object value)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingDynamicList(System.Collections.Generic.List<object> values)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingDynamicArray(object[] values)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadPrivateSeed()
        {
            return _privateSeed;
        }
    }

    /// <summary>
    /// Compiled MonoBehaviour host so Unity-message added-method warnings can be classified
    /// against a real compiled type rather than a throwaway uncompiled class.
    /// </summary>
    public class HotReloadAddedMemberBehaviour : MonoBehaviour
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ExistingTick()
        {
        }
    }

    /// <summary>
    /// Compiled interface used to skip explicit-interface added methods.
    /// </summary>
    public interface IHotReloadAddedMemberPing
    {
        int Ping(int value);
    }

    /// <summary>
    /// Compiled host that already implements the interface so an added explicit implementation
    /// is a new method on an existing type, not a new type.
    /// </summary>
    public class HotReloadAddedMemberInterfaceHost : IHotReloadAddedMemberPing
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Ping(int value)
        {
            return value;
        }
    }

    /// <summary>
    /// Compiled virtual host so added override/virtual methods can be skipped against a real type.
    /// </summary>
    public class HotReloadAddedMemberVirtualHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual int ExistingVirtual(int value)
        {
            return value;
        }
    }

    /// <summary>
    /// Compiled derived host so an added override can be classified against a real compiled type.
    /// </summary>
    public class HotReloadAddedMemberVirtualChild : HotReloadAddedMemberVirtualHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ChildExisting()
        {
            return 1;
        }
    }

    /// <summary>
    /// Compiled lifecycle hosts so worker tests bind against real types rather than throwaways.
    /// </summary>
    public class HotReloadLifecycleMonoPrivateStartFixture : MonoBehaviour
    {
        private void Start()
        {
            int x = 1;
            x += 1;
        }
    }

    /// <summary>
    /// Compiled POCO Start host for the lifecycle-note gate.
    /// </summary>
    public class HotReloadLifecyclePocoStartFixture
    {
        private void Start()
        {
            int x = 1;
            x += 1;
        }
    }

    /// <summary>
    /// Compiled public Start host; name-only notes must not flag this.
    /// </summary>
    public class HotReloadLifecycleMonoPublicStartFixture : MonoBehaviour
    {
        public void Start()
        {
            int x = 1;
            x += 1;
        }
    }

    /// <summary>
    /// Compiled parameterized Start host; Unity will not invoke this as a message.
    /// </summary>
    public class HotReloadLifecycleMonoParamStartFixture : MonoBehaviour
    {
        private void Start(int delay)
        {
            int x = delay;
            x += 1;
        }
    }

    /// <summary>
    /// Compiled Awake host for the direct one-shot lifecycle note.
    /// </summary>
    public class HotReloadLifecycleAwakeFixture : MonoBehaviour
    {
        private void Awake()
        {
            int x = 1;
            x += 1;
        }
    }

    /// <summary>
    /// Compiled alias-shadow host so the local-vs-global using-alias test is not a new type.
    /// </summary>
    internal class HotReloadAliasShadowFixture
    {
        public string Build()
        {
            return "ok";
        }
    }
}
