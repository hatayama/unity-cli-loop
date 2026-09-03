using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using DynamicExecutionContext = io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies CommandRunner execution results.
    /// </summary>
    [TestFixture]
    public class CommandRunnerTests
    {
        /// <summary>
        /// What: a generated command type without Execute returns the statement-input recovery guidance.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenDynamicCommandHasNoExecuteMethod_ReturnsRecoveryGuidance()
        {
            Assembly assembly = CreateAssemblyWithoutExecuteMethod();
            DynamicExecutionContext context = new()
            {
                CompiledAssembly = assembly,
                CancellationToken = CancellationToken.None
            };
            ExecutionResult result = await new CommandRunner().ExecuteAsync(context);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_NO_EXECUTE_METHOD));
            Assert.That(
                result.NextActions,
                Is.EqualTo(UnityCliLoopConstants.DYNAMIC_CODE_NO_EXECUTE_METHOD_NEXT_ACTIONS));
        }

        private static Assembly CreateAssemblyWithoutExecuteMethod()
        {
            AssemblyName assemblyName = new("CommandRunnerWithoutExecuteMethodTests");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name);
            TypeBuilder typeBuilder = moduleBuilder.DefineType(
                "UnityCliLoop.Dynamic.DynamicCommand",
                TypeAttributes.Public | TypeAttributes.Class);
            typeBuilder.CreateType();
            return assemblyBuilder;
        }
    }
}
