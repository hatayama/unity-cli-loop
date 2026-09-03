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
            // The literal keeps the guidance itself under test: comparing against the shared
            // constant alone would still pass if that constant were emptied or reworded.
            Assert.That(result.NextActions, Has.Count.EqualTo(1));
            Assert.That(
                result.NextActions[0],
                Is.EqualTo(
                    "Remove the class and method wrapper and pass the statements themselves, e.g. " +
                    "--code \"return GameObject.Find(\\\"Player\\\").transform.position;\""));
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
