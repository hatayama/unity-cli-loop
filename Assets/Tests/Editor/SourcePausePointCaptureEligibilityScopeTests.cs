using System;
using System.Collections.Generic;
using System.Linq;

using Mono.Cecil;
using Mono.Cecil.Cil;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how capture eligibility collects every local in a method's debug scopes, using a Cecil module built
    /// in memory so each scope, slot, and name can be set up directly.
    /// </summary>
    public sealed class SourcePausePointCaptureEligibilityScopeTests
    {
        private ModuleDefinition _module;
        private MethodDefinition _method;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule("CaptureEligibilityProbe", ModuleKind.Dll);
            TypeDefinition host = new TypeDefinition("Probe", "Host", TypeAttributes.Public | TypeAttributes.Class, _module.TypeSystem.Object);
            _module.Types.Add(host);
            _method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, _module.TypeSystem.Void);
            host.Methods.Add(_method);
            ILProcessor il = _method.Body.GetILProcessor();
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);
        }

        [TearDown]
        public void TearDown()
        {
            _module.Dispose();
        }

        /// <summary>
        /// Verifies a method without debug scopes has no locals to collect.
        /// </summary>
        [Test]
        public void CollectAllCapturableLocals_WithoutARootScope_ReturnsNothing()
        {
            AddVariable(_module.TypeSystem.Int32);

            List<SourcePausePointLocalVariable> locals = SourcePausePointCaptureEligibility.CollectAllCapturableLocals(_method);

            Assert.That(locals, Is.Empty);
        }

        /// <summary>
        /// Verifies locals are collected from nested scopes, keeping the first local for each slot and each name and
        /// leaving out compiler-generated names.
        /// </summary>
        [Test]
        public void CollectAllCapturableLocals_WithNestedScopes_KeepsTheFirstLocalPerSlotAndName()
        {
            VariableDefinition first = AddVariable(_module.TypeSystem.Int32);
            VariableDefinition hoisted = AddVariable(_module.TypeSystem.Int32);
            VariableDefinition sameName = AddVariable(_module.TypeSystem.Int32);
            VariableDefinition text = AddVariable(_module.TypeSystem.String);
            ScopeDebugInformation root = CreateScope();
            root.Variables.Add(new VariableDebugInformation(first, "count"));
            root.Variables.Add(new VariableDebugInformation(hoisted, "<>u__1"));
            ScopeDebugInformation child = CreateScope();
            child.Variables.Add(new VariableDebugInformation(sameName, "count"));
            child.Variables.Add(new VariableDebugInformation(first, "reused"));
            child.Variables.Add(new VariableDebugInformation(text, "label"));
            root.Scopes.Add(child);
            _method.DebugInformation.Scope = root;

            List<SourcePausePointLocalVariable> locals = SourcePausePointCaptureEligibility.CollectAllCapturableLocals(_method);

            Assert.That(locals.Select(local => local.Name), Is.EqualTo(new[] { "count", "label" }));
            Assert.That(locals.Select(local => local.SlotIndex), Is.EqualTo(new[] { 0, 3 }));
        }

        /// <summary>
        /// Verifies a debug entry for a variable that is not one of the method's locals (no slot) is skipped.
        /// </summary>
        [Test]
        public void CollectAllCapturableLocals_WithASlotOutsideTheLocals_SkipsIt()
        {
            VariableDefinition near = AddVariable(_module.TypeSystem.Int32);
            VariableDefinition detached = new VariableDefinition(_module.TypeSystem.Int32);
            ScopeDebugInformation root = CreateScope();
            root.Variables.Add(new VariableDebugInformation(detached, "far"));
            root.Variables.Add(new VariableDebugInformation(near, "near"));
            _method.DebugInformation.Scope = root;

            List<SourcePausePointLocalVariable> locals = SourcePausePointCaptureEligibility.CollectAllCapturableLocals(_method);

            Assert.That(locals.Select(local => local.Name), Is.EqualTo(new[] { "near" }));
        }

        /// <summary>
        /// Verifies a struct declared in the module with an attribute other than IsByRefLike is still captured.
        /// </summary>
        [Test]
        public void CollectAllCapturableLocals_WithAnAttributedStructThatIsNotARefStruct_CapturesIt()
        {
            TypeDefinition plainStruct = new TypeDefinition(
                "Probe",
                "PlainStruct",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                _module.ImportReference(typeof(ValueType)));
            plainStruct.CustomAttributes.Add(
                new CustomAttribute(_module.ImportReference(typeof(ObsoleteAttribute).GetConstructor(Type.EmptyTypes))));
            _module.Types.Add(plainStruct);
            VariableDefinition variable = AddVariable(plainStruct);
            ScopeDebugInformation root = CreateScope();
            root.Variables.Add(new VariableDebugInformation(variable, "point"));
            _method.DebugInformation.Scope = root;

            List<SourcePausePointLocalVariable> locals = SourcePausePointCaptureEligibility.CollectAllCapturableLocals(_method);

            Assert.That(locals.Select(local => local.Name), Is.EqualTo(new[] { "point" }));
            Assert.That(locals[0].IsValueType, Is.True);
        }

        private VariableDefinition AddVariable(TypeReference type)
        {
            VariableDefinition variable = new VariableDefinition(type);
            _method.Body.Variables.Add(variable);
            return variable;
        }

        private ScopeDebugInformation CreateScope()
        {
            return new ScopeDebugInformation(_method.Body.Instructions[0], null);
        }
    }
}
