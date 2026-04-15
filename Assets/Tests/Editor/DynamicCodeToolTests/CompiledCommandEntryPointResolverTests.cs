using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class CompiledCommandEntryPointResolverTests
    {
        [Test]
        public void TryFindExecuteMethod_WhenDirectTypeHasOverloads_ShouldPickSupportedSignature()
        {
            CompiledCommandEntryPointResolver resolver = new CompiledCommandEntryPointResolver();

            (System.Type targetType, MethodInfo executeMethod) = resolver.TryFindExecuteMethod(
                typeof(global::uLoopMCP.Dynamic.DynamicCommand).Assembly);

            Assert.That(targetType, Is.EqualTo(typeof(global::uLoopMCP.Dynamic.DynamicCommand)));
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

namespace uLoopMCP.Dynamic
{
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
