using System;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled call-site targets and callers for <see cref="HotReloadCallSiteScanner"/> tests.
    /// </summary>
    public static class HotReloadCallSiteScannerFixture
    {
        public static int CalledFromOrdinaryMethod()
        {
            return 1;
        }

        public static int OrdinaryCaller()
        {
            return CalledFromOrdinaryMethod();
        }

        public static int NeverCalled()
        {
            return 2;
        }

        public static int CalledOnlyViaDelegate()
        {
            return 3;
        }

        public static Func<int> CaptureDelegate()
        {
            Func<int> captured = CalledOnlyViaDelegate;
            return captured;
        }

        public static int CalledFromAsyncMethod()
        {
            return 4;
        }

        public static async Task<int> AsyncCaller()
        {
            await Task.Yield();
            return CalledFromAsyncMethod();
        }

        public static int CallGenericHostTarget()
        {
            GenericHost<int> host = new GenericHost<int>();
            return host.Target();
        }

        public static int GenericMethodTarget<T>()
        {
            return 1;
        }

        public static int CallGenericMethodTarget()
        {
            return GenericMethodTarget<int>();
        }

        public static Func<int> CaptureGenericMethodTarget()
        {
            Func<int> captured = GenericMethodTarget<int>;
            return captured;
        }

        public static int SelfRecursive(int remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            return SelfRecursive(remaining - 1);
        }

        public static async Task<int> AsyncSelfRecursive(int remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            return await AsyncSelfRecursive(remaining - 1);
        }

        public static int CalledFromGenericArityCaller()
        {
            return 5;
        }

        public static int Caller(int value)
        {
            return value;
        }

        public static int Caller<T>(int value)
        {
            CalledFromGenericArityCaller();
            return value;
        }

        public static int CalledFromCrossAssembly()
        {
            return 6;
        }
    }

    /// <summary>
    /// Gives cross-assembly coverage a target type whose name cannot collide with its foreign fixture.
    /// </summary>
    public static class HotReloadCallSiteScannerCrossAssemblyTarget
    {
        public static int Called()
        {
            return 7;
        }
    }

    /// <summary>
    /// Supplies a public target with same-key callers compiled into two different assemblies.
    /// </summary>
    public static class HotReloadQualifiedCallerIdentityTarget
    {
        public static int Called()
        {
            return 9;
        }
    }

    /// <summary>
    /// Supplies the main-assembly side of a same-metadata-name internal caller pair.
    /// </summary>
    internal static class HotReloadQualifiedCallerIdentityCaller
    {
        internal static int Call()
        {
            return HotReloadQualifiedCallerIdentityTarget.Called();
        }
    }

    /// <summary>
    /// Open generic host so call sites go through a constructed <c>GenericHost&lt;int&gt;</c>.
    /// </summary>
    public class GenericHost<T>
    {
        public int Target()
        {
            return 1;
        }
    }

    /// <summary>
    /// Supplies compiled MonoBehaviour caller shapes for caller-aware lifecycle note tests.
    /// </summary>
    public sealed class OneShotCallerScannerFixture : MonoBehaviour
    {
        private event Action RepeatedEvent;

        private void Awake()
        {
            AwakeOnlyTarget();
            MixedTarget();
            RepeatedEvent += DelegateAssignedTarget;
            ChainStep();
            new OneShotCallerChainHelper().Build();
            MixedChainStep();
            RepeatedEvent += DelegateChainStep;
        }

        private void Start()
        {
            DeepStep1();
        }

        private void Update()
        {
            RepeatedEvent?.Invoke();
        }

        private void AwakeOnlyTarget()
        {
        }

        private void MixedTarget()
        {
        }

        private void DelegateAssignedTarget()
        {
        }

        public void OrdinaryCaller()
        {
            MixedTarget();
        }

        private void ChainStep()
        {
            ChainedAwakeOnlyTarget();
        }

        private void ChainedAwakeOnlyTarget()
        {
        }

        private void MixedChainStep()
        {
            MixedChainTarget();
        }

        public void OrdinaryChainCaller()
        {
            MixedChainStep();
        }

        private void MixedChainTarget()
        {
        }

        private void DelegateChainStep()
        {
            DelegateChainTarget();
        }

        private void DelegateChainTarget()
        {
        }

        private void DeadEndStep()
        {
            DeadEndTarget();
        }

        private void DeadEndTarget()
        {
        }

        private void DeepStep1()
        {
            DeepStep2();
        }

        private void DeepStep2()
        {
            DeepStep3();
        }

        private void DeepStep3()
        {
            DeepStep4();
        }

        private void DeepStep4()
        {
            DeepStep5();
        }

        private void DeepStep5()
        {
            DeepTarget();
        }

        private void DeepTarget()
        {
        }
    }

    /// <summary>
    /// Non-MonoBehaviour intermediate so a compiled Awake chain can leave the fixture type.
    /// </summary>
    public sealed class OneShotCallerChainHelper
    {
        public void Build()
        {
            ConfigureTarget();
        }

        public void ConfigureTarget()
        {
        }
    }
}
