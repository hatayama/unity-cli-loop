using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Compiled Command Entry Point Resolver behavior.
    /// </summary>
    [TestFixture]
    public class CompiledCommandEntryPointResolverTests
    {
        [Test]
        public void TryFindExecuteMethod_WhenDirectTypeHasOverloads_ShouldPickSupportedSignature()
        {
            CompiledCommandEntryPointResolver resolver = new();

            (System.Type targetType, MethodInfo executeMethod) = resolver.TryFindExecuteMethod(
                typeof(global::io.github.hatayama.UnityCliLoop.Tests.Editor.Dynamic.DynamicCommand).Assembly);

            Assert.That(targetType, Is.EqualTo(typeof(global::io.github.hatayama.UnityCliLoop.Tests.Editor.Dynamic.DynamicCommand)));
            Assert.That(executeMethod, Is.Not.Null);
            Assert.That(executeMethod.GetParameters(), Has.Length.EqualTo(2));
            Assert.That(
                executeMethod.GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(Dictionary<string, object>)));
            Assert.That(
                executeMethod.GetParameters()[1].ParameterType,
                Is.EqualTo(typeof(CancellationToken)));
        }
    }
}

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.Dynamic
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DynamicCommand
    {
        public string Execute()
        {
            return "parameterless";
        }

        public string Execute(Dictionary<string, object> parameters, CancellationToken ct)
        {
            return "dictionary-and-cancellation";
        }

        public string Execute(Dictionary<string, object> parameters)
        {
            return "dictionary";
        }

        public string Execute(int unsupported)
        {
            return unsupported.ToString();
        }
    }
}
