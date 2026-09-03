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

            ExecutionResult result = await ExecuteAsync(assembly);

            AssertRecoveryGuidance(result);
        }

        /// <summary>
        /// What: a class-wrapped snippet whose Execute is static (the reported input shape) is not
        /// picked up as the entry point and receives the same recovery guidance.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenDynamicCommandOnlyHasStaticExecute_ReturnsRecoveryGuidance()
        {
            Assembly assembly = CreateAssemblyWithStaticExecuteMethod();

            ExecutionResult result = await ExecuteAsync(assembly);

            AssertRecoveryGuidance(result);
        }

        private static async Task<ExecutionResult> ExecuteAsync(Assembly assembly)
        {
            DynamicExecutionContext context = new()
            {
                CompiledAssembly = assembly,
                CancellationToken = CancellationToken.None
            };
            return await new CommandRunner().ExecuteAsync(context);
        }

        private static void AssertRecoveryGuidance(ExecutionResult result)
        {
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
                    "Remove the class, namespace, and method wrapper and pass the statements themselves, e.g. " +
                    "--code \"return GameObject.Find(\\\"Player\\\").transform.position;\""));
        }

        private static Assembly CreateAssemblyWithoutExecuteMethod()
        {
            TypeBuilder typeBuilder = DefineDynamicCommandType("CommandRunnerWithoutExecuteMethodTests");
            typeBuilder.CreateType();
            return typeBuilder.Assembly;
        }

        private static Assembly CreateAssemblyWithStaticExecuteMethod()
        {
            TypeBuilder typeBuilder = DefineDynamicCommandType("CommandRunnerStaticExecuteMethodTests");
            MethodBuilder executeMethod = typeBuilder.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                System.Type.EmptyTypes);
            ILGenerator il = executeMethod.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            typeBuilder.CreateType();
            return typeBuilder.Assembly;
        }

        private static TypeBuilder DefineDynamicCommandType(string assemblyNameText)
        {
            AssemblyName assemblyName = new(assemblyNameText);
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name);
            return moduleBuilder.DefineType(
                "UnityCliLoop.Dynamic.DynamicCommand",
                TypeAttributes.Public | TypeAttributes.Class);
        }
    }
}
