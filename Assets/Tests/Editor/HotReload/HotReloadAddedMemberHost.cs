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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public HotReloadAddedMemberHost Get()
        {
            return this;
        }

        public HotReloadAddedMemberHost this[int index]
        {
            get { return this; }
        }

        private int _privateSeed = 7;

        private const int PrivateConstThree = 3;

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
        private static int PrivateStaticSeven()
        {
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int PrivateCall()
        {
            return _privateSeed;
        }

        private int PrivateSeedValue
        {
            get { return _privateSeed; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadPrivateConst()
        {
            return PrivateConstThree;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadPrivateSeed()
        {
            return _privateSeed;
        }
    }

    public interface IHotReloadFieldKindChangeMarker
    {
        int ExplicitHp { get; }
    }

    /// <summary>
    /// Compiled host with an auto-property and an event so a same-name field declaration
    /// can be classified against live compiled members instead of a side-table store.
    /// </summary>
    public class HotReloadFieldKindChangeFixture : IHotReloadFieldKindChangeMarker
    {
        public int Hp { get; set; }

        public event Action ScoreChanged;

        int IHotReloadFieldKindChangeMarker.ExplicitHp
        {
            get { return 0; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ClearScoreChanged()
        {
            ScoreChanged = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int UntouchedKind()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadKind(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int WriteKind(int value)
        {
            return value;
        }
    }

    /// <summary>
    /// Compiled struct host so added-field tests can skip struct types against a real type.
    /// GetOrInit boxes struct receivers and would reinitialize on every access.
    /// </summary>
    public struct HotReloadAddedFieldStructHost
    {
        public int Existing;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadExisting()
        {
            return Existing;
        }
    }

    /// <summary>
    /// Compiled op_Increment struct so added-field ++ skip can target a real non-numeric type.
    /// </summary>
    public struct HotReloadAddedFieldCounter
    {
        public int Value;

        public static HotReloadAddedFieldCounter operator ++(HotReloadAddedFieldCounter counter)
        {
            counter.Value += 1;
            return counter;
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
    /// Compiled interface with a default method so edits to interface members can be skipped
    /// without becoming Harmony patch candidates.
    /// </summary>
    public interface IHotReloadAddedMemberDefault
    {
        int ExistingDefault() => 1;
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

    /// <summary>
    /// Nested interface host so skip labels can pin FormatMethodKeyParts for nested + generic
    /// + multi-parameter methods.
    /// </summary>
    public class HotReloadMethodLabelNestedHost
    {
        public interface INestedGeneric
        {
            int GenericPing<T>(int left, string right) => 1;
        }
    }

    /// <summary>
    /// Partial host so worker tests can prove deleted-method signatures are omitted on partials.
    /// </summary>
    public partial class HotReloadAddedMemberPartialHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PartialKept()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PartialRemoved()
        {
            return 2;
        }
    }
}
