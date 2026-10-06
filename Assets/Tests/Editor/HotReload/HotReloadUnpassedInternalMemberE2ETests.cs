using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for edited bodies that use internal members of a compiled type
    /// the reload was not given: what each row reports, and what the method does when called.
    /// </summary>
    /// <remarks>
    /// Why separate groups of edits: a straight-line body runs as IL copied into the patched method,
    /// while a lambda or iterator body runs in code the shim assembly compiles on its own, and a
    /// name inherited by simple name has to be qualified before the shim can compile it at all.
    /// A partial type also skips some uses that run in the patched method, such as a method passed
    /// as a delegate, so those have a group of their own.
    /// </remarks>
    public class HotReloadUnpassedInternalMemberE2ETests
    {
        private const string PlainDerivedFileName = "HotReloadPlainDerivedFixture.cs";
        private const string PartialDerivedFileName = "HotReloadPartialDerivedFixture.cs";
        private const string CallerFileName = "HotReloadInternalMemberCaller.cs";
        private const string PlainDerivedValueBody = "return 10;";
        private const string PartialDerivedValueBody = "return 9;";
        private const string DerivedValueAnchor =
            "        [MethodImpl(MethodImplOptions.NoInlining)]\n        public int DerivedValue()";
        private const string DerivedValue = "DerivedValue";
        private const string ClosureValue = "ClosureValue";
        private const string ClosureSeedValue = "ClosureSeedValue";
        private const string IteratorValues = "IteratorValues";
        private const string ClosureSeedPlusValue = "ClosureSeedPlusValue";
        private const string DerivedPropertyGetter = "get_DerivedProperty";
        private const string AsyncValue = "AsyncValue";
        private const string RaiseDerivedEvent = "RaiseDerivedEvent";

        // Public only because a test case argument has to be as visible as the test method.
        public enum FixtureKind
        {
            Plain,
            Partial
        }

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        // Edits whose body runs as IL copied into the patched method and uses an internal member of
        // a type in the edited file's own assembly.
        private static IEnumerable<string> EditsOfTheSameAssemblyThatRunInThePatchedMethod()
        {
            yield return "InternalStaticMethod";
            yield return "InternalInstanceMethod";
            yield return "InternalFieldReadAndWrite";
            yield return "InternalPropertyGet";
            yield return "InternalPropertyGetAndSet";
            yield return "InheritedInternalMethodThroughThis";
            yield return "InternalInstanceMethodThroughConditionalAccess";
            yield return "InternalStaticMethodOfNestedType";
            yield return "InternalFieldInsideNameof";
            yield return "InternalStaticMethodNextToLambdaReadingOwnPrivateField";
            yield return "InternalInstanceMethodOfAnInternalResult";
        }

        // Edits whose body runs as IL copied into the patched method.
        private static IEnumerable<string> EditsThatRunInThePatchedMethod()
        {
            foreach (string editName in EditsOfTheSameAssemblyThatRunInThePatchedMethod())
            {
                yield return editName;
            }

            yield return "InternalMethodOfPublicTypeOfAnotherAssemblyThroughInternalsVisibleTo";
        }

        // Edits the shim cannot run as written: a simple name it cannot qualify, or a use inside a
        // lambda, local function, anonymous method, iterator or async method, or in a getter or a
        // method that runs through a delegating shim, which the shim assembly compiles as ordinary
        // code of its own, also when the lambda reaches the member through the result of another
        // internal member. A lambda that only uses such a result as a value is here too, because the
        // worker cannot tell it from one that reaches the member through the result.
        private static IEnumerable<string> EditsThatDoNotRunInThePatchedMethod()
        {
            yield return "InheritedInternalMethodBySimpleName";
            yield return "InternalStaticMethodInLambda";
            yield return "InternalInstanceMethodInLambda";
            yield return "InternalStaticMethodInLambdaReadingOwnPrivateField";
            yield return "InternalStaticMethodInIteratorReadingOwnPrivateField";
            yield return "InternalInstanceMethodInIteratorReadingOwnPrivateField";
            yield return "InternalStaticMethodInIterator";
            yield return "InternalInstanceMethodInLambdaOverAnInternalResult";
            yield return "InternalFieldInLambdaParameterFromAnInternalResult";
            yield return "InternalStaticMethodInLocalFunction";
            yield return "InternalStaticMethodInAnonymousMethod";
            yield return "InternalStaticMethodInAsyncMethod";
            yield return "InternalStaticMethodNextToLambdaReadingOwnPrivateFieldInGetter";
            yield return "InternalStaticMethodNextToOwnEventRaise";
            yield return "InternalFieldReadIntoAVarCapturedByALambda";
        }

        // Edits whose body runs as IL copied into the patched method but uses an internal member in a
        // form a partial type does not patch: a method passed as a delegate, an event, or a member
        // named in an object initializer or a property pattern.
        private static IEnumerable<string> EditsThatOnlyAPlainTypeRunsInThePatchedMethod()
        {
            yield return "InternalStaticMethodPassedAsDelegate";
            yield return "InternalInstanceMethodPassedAsDelegate";
            yield return "InternalEventSubscription";
            yield return "InternalFieldInObjectInitializer";
            yield return "InternalPropertyInPropertyPattern";
        }

        /// <summary>
        /// What: an edited body of a plain type that uses an internal member of a compiled type the
        /// reload was not given is patched, and the method returns the edited value.
        /// </summary>
        [TestCaseSource(nameof(EditsThatRunInThePatchedMethod))]
        public async Task Run_PlainTypeBodyUsingInternalMemberOfUnpassedType_PatchesBehavior(string editName)
        {
            BodyEdit edit = FindEdit(editName);
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Plain, edit);

            AssertPatchedAsEdited(result, FixtureKind.Plain, edit);
        }

        /// <summary>
        /// What: the edits that use an internal member of a type in the edited file's own assembly
        /// are patched on a partial type too, and the method returns the edited value, as on a plain
        /// type.
        /// </summary>
        [TestCaseSource(nameof(EditsOfTheSameAssemblyThatRunInThePatchedMethod))]
        public async Task Run_PartialTypeBodyUsingInternalMemberOfUnpassedType_PatchesBehavior(string editName)
        {
            BodyEdit edit = FindEdit(editName);
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Partial, edit);

            AssertPatchedAsEdited(result, FixtureKind.Partial, edit);
        }

        /// <summary>
        /// What: an edited body of a partial type that uses an internal member of a type in another
        /// assembly is either patched and returns the edited value, or skipped and keeps the
        /// compiled behavior.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeBodyUsingInternalMemberOfATypeOfAnotherAssembly_IsAppliedAsEditedOrSkipped()
        {
            BodyEdit edit = FindEdit("InternalMethodOfPublicTypeOfAnotherAssemblyThroughInternalsVisibleTo");
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Partial, edit);

            AssertAppliedAsEditedOrSkipped(result, FixtureKind.Partial, edit);
        }

        /// <summary>
        /// What: edits on a partial type that the shim cannot run as written are either patched and
        /// return the edited value, or skipped and keep the compiled behavior. They are never
        /// reported as patched and then fail when called, and never fail the file.
        /// </summary>
        [TestCaseSource(nameof(EditsThatDoNotRunInThePatchedMethod))]
        public async Task Run_PartialTypeBodyUsingInternalMemberOfUnpassedType_IsAppliedAsEditedOrSkipped(string editName)
        {
            BodyEdit edit = FindEdit(editName);
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Partial, edit);

            AssertAppliedAsEditedOrSkipped(result, FixtureKind.Partial, edit);
        }

        /// <summary>
        /// What: the same edits on a plain type are either patched and return the edited value, or
        /// skipped and keep the compiled behavior. They are never reported as patched and then fail
        /// when called, and never fail the file.
        /// </summary>
        [TestCaseSource(nameof(EditsThatDoNotRunInThePatchedMethod))]
        public async Task Run_PlainTypeBodyUsingInternalMemberOfUnpassedType_IsAppliedAsEditedOrSkipped(string editName)
        {
            BodyEdit edit = FindEdit(editName);
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Plain, edit);

            AssertAppliedAsEditedOrSkipped(result, FixtureKind.Plain, edit);
        }

        /// <summary>
        /// What: an edited body of a plain type that passes an internal method of a compiled type the
        /// reload was not given as a delegate, subscribes to its internal event, or names its internal
        /// member in an object initializer or a property pattern is patched, and the method returns
        /// the edited value.
        /// </summary>
        [TestCaseSource(nameof(EditsThatOnlyAPlainTypeRunsInThePatchedMethod))]
        public async Task Run_PlainTypeBodyUsingInternalMemberAsADelegateEventInitializerOrPattern_PatchesBehavior(string editName)
        {
            BodyEdit edit = FindEdit(editName);
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Plain, edit);

            AssertPatchedAsEdited(result, FixtureKind.Plain, edit);
        }

        /// <summary>
        /// What: an edited getter that uses an internal member of a compiled type the reload was not
        /// given is patched on a plain type and on a partial type, and the property returns the edited
        /// value.
        /// </summary>
        [TestCase(FixtureKind.Plain)]
        [TestCase(FixtureKind.Partial)]
        public async Task Run_GetterUsingInternalMemberOfUnpassedType_PatchesBehavior(FixtureKind fixture)
        {
            BodyEdit edit = new BodyEdit(
                DerivedPropertyGetter,
                "return 40;",
                "return HotReloadInternalMemberHost.InternalStaticValue() + 100;",
                101);
            HotReloadOrchestratorResult result = await RunEditAsync(fixture, edit);

            AssertPatchedAsEdited(result, fixture, edit);
        }

        /// <summary>
        /// What: a file whose applied body uses an internal member of a type the reload was not given
        /// is brought back by a later reload of another file, and its method still returns the value
        /// that body computes, whether the row re-applies it or skips it.
        /// </summary>
        [TestCase("CallsInternal", FixtureKind.Plain)]
        [TestCase("CallsInternal", FixtureKind.Partial)]
        [TestCase("PlainValue", FixtureKind.Plain)]
        [TestCase("PlainValue", FixtureKind.Partial)]
        public async Task Run_SiblingBroughtBackUsingInternalMemberOfUnpassedType_KeepsTheAppliedBodyRunning(
            string methodName,
            FixtureKind passedFixture)
        {
            bool callsInternal = methodName == "CallsInternal";
            int edited = callsInternal ? 201 : 202;
            string callerPath = FixturePath(CallerFileName);
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                [callerPath] = WriteEdited(
                    callerPath,
                    "UnpassedInternalSiblingCaller.cs",
                    callsInternal ? "return HotReloadInternalMemberHost.InternalStaticValue();" : "return 1;",
                    callsInternal
                        ? "return HotReloadInternalMemberHost.InternalStaticValue() + 200;"
                        : "return new HotReloadInternalMemberHost().InternalInstanceValue() + 200;")
            };
            HotReloadOrchestratorResult first = await RunAsync(new[] { callerPath }, overrides);
            Assert.That(FindRow(first, "HotReloadInternalMemberCaller", methodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), "Precondition: the caller must be patched.\n" + FormatOutcomes(first));
            Assert.That(CallCaller(methodName), Is.EqualTo(edited), "Precondition: the caller must run the edited body.\n" + FormatOutcomes(first));

            string passedPath = FixturePath(FixtureFileName(passedFixture));
            overrides[passedPath] = WriteEdited(
                passedPath,
                "UnpassedInternalSiblingPassed.cs",
                ValueBody(passedFixture),
                "return 50;");
            HotReloadOrchestratorResult second = await RunAsync(new[] { passedPath }, overrides);

            Assert.That(second.ReappliedSiblingPaths, Has.Some.EndsWith(CallerFileName), FormatOutcomes(second));
            Assert.That(
                FindRow(second, "HotReloadInternalMemberCaller", methodName).Kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Patched).Or.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                FormatOutcomes(second));
            Assert.That(CallCaller(methodName), Is.EqualTo(edited), FormatOutcomes(second));
        }

        /// <summary>
        /// What: an added method whose body calls an internal method of a type the reload was not
        /// given is either added and runs the edited body, or skipped together with its caller so
        /// the compiled behavior stays. It is never added and then fails when called.
        /// </summary>
        [TestCase("HotReloadInternalMemberHost.InternalStaticValue() + 300", 301)]
        [TestCase("new HotReloadInternalMemberHost().InternalInstanceValue() + 300", 302)]
        public async Task Run_AddedMethodUsingInternalMemberOfUnpassedType_IsAddedAsEditedOrSkipped(
            string expression,
            int edited)
        {
            string path = FixturePath(PlainDerivedFileName);
            string source = ReplaceOnce(File.ReadAllText(path), PlainDerivedValueBody, "return AddedInternalCall();");
            source = ReplaceOnce(
                source,
                DerivedValueAnchor,
                "        private int AddedInternalCall()\n        {\n            return " + expression + ";\n        }\n\n"
                + DerivedValueAnchor);
            HotReloadOrchestratorResult result = await RunAsync(
                new[] { path },
                new Dictionary<string, string>
                {
                    [path] = HotReloadTestSourceWriter.WriteEditedSource("UnpassedInternalAddedMethod.cs", source)
                });

            HotReloadMethodOutcomeKind addedKind = FindRow(result, "HotReloadPlainDerivedFixture", "AddedInternalCall").Kind;
            Assert.That(
                addedKind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Added).Or.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                FormatOutcomes(result));
            int expected = addedKind == HotReloadMethodOutcomeKind.Added ? edited : 10;
            Assert.That(new HotReloadPlainDerivedFixture().DerivedValue(), Is.EqualTo(expected), FormatOutcomes(result));
        }

        private static BodyEdit FindEdit(string editName)
        {
            switch (editName)
            {
                case "InternalStaticMethod":
                    return BodyEdit.OfDerivedValue("return HotReloadInternalMemberHost.InternalStaticValue() + 100;", 101);
                case "InternalInstanceMethod":
                    return BodyEdit.OfDerivedValue("return new HotReloadInternalMemberHost().InternalInstanceValue() + 100;", 102);
                case "InternalFieldReadAndWrite":
                    return BodyEdit.OfDerivedValue(
                        "HotReloadInternalMemberHost host = new HotReloadInternalMemberHost();\n"
                        + "            host.InternalField = host.InternalField + 40;\n"
                        + "            return host.InternalField + 100;",
                        143);
                case "InternalPropertyGet":
                    return BodyEdit.OfDerivedValue("return new HotReloadInternalMemberHost().InternalProperty + 100;", 104);
                case "InternalPropertyGetAndSet":
                    return BodyEdit.OfDerivedValue(
                        "HotReloadInternalMemberHost host = new HotReloadInternalMemberHost();\n"
                        + "            host.InternalSettableProperty = host.InternalSettableProperty + 50;\n"
                        + "            return host.InternalSettableProperty + 100;",
                        170);
                case "InheritedInternalMethodThroughThis":
                    return BodyEdit.OfDerivedValue("return this.InternalInstanceValue() + 100;", 102);
                case "InternalInstanceMethodThroughConditionalAccess":
                    return BodyEdit.OfDerivedValue(
                        "HotReloadInternalMemberHost host = new HotReloadInternalMemberHost();\n"
                        + "            return (host?.InternalInstanceValue() ?? 0) + 100;",
                        102);
                case "InternalStaticMethodOfNestedType":
                    return BodyEdit.OfDerivedValue("return HotReloadInternalMemberHost.Nested.NestedInternalValue() + 100;", 111);
                case "InternalFieldInsideNameof":
                    return BodyEdit.OfDerivedValue("return nameof(HotReloadInternalMemberHost.InternalField).Length + 100;", 113);
                case "InternalStaticMethodNextToLambdaReadingOwnPrivateField":
                    return new BodyEdit(ClosureSeedPlusValue, "read() + 7", "read() + HotReloadInternalMemberHost.InternalStaticValue()", 1001);
                case "InternalInstanceMethodOfAnInternalResult":
                    return BodyEdit.OfDerivedValue("return HotReloadInternalMemberHost.InternalSelf().InternalInstanceValue() + 120;", 122);
                case "InternalMethodOfPublicTypeOfAnotherAssemblyThroughInternalsVisibleTo":
                    return BodyEdit.OfDerivedValue(
                        "return global::io.github.hatayama.UnityCliLoop.FirstPartyTools.PausePointResponse"
                        + ".NormalizeNotCapturableVariables(new string[] { \"a\", \"b\" }).Count + 100;",
                        102);
                case "InheritedInternalMethodBySimpleName":
                    return BodyEdit.OfDerivedValue("return InternalInstanceValue() + 100;", 102);
                case "InternalStaticMethodInLambda":
                    return new BodyEdit(ClosureValue, "() => 30", "() => HotReloadInternalMemberHost.InternalStaticValue() + 100", 101);
                case "InternalInstanceMethodInLambda":
                    return new BodyEdit(ClosureValue, "() => 30", "() => new HotReloadInternalMemberHost().InternalInstanceValue() + 100", 102);
                case "InternalStaticMethodInLambdaReadingOwnPrivateField":
                    return new BodyEdit(ClosureSeedValue, "() => _seed", "() => _seed + HotReloadInternalMemberHost.InternalStaticValue()", 1001);
                case "InternalStaticMethodInIteratorReadingOwnPrivateField":
                    return new BodyEdit(IteratorValues, "yield return _seed;", "yield return _seed + HotReloadInternalMemberHost.InternalStaticValue();", 1001);
                case "InternalInstanceMethodInIteratorReadingOwnPrivateField":
                    return new BodyEdit(IteratorValues, "yield return _seed;", "yield return _seed + new HotReloadInternalMemberHost().InternalInstanceValue();", 1002);
                case "InternalStaticMethodInIterator":
                    return new BodyEdit(IteratorValues, "yield return _seed;", "yield return HotReloadInternalMemberHost.InternalStaticValue() + 100;", 101);
                case "InternalInstanceMethodInLambdaOverAnInternalResult":
                    return BodyEdit.OfDerivedValue(
                        "var host = HotReloadInternalMemberHost.InternalSelf();\n"
                        + "            System.Func<int> read = () => host.InternalInstanceValue() + 100;\n"
                        + "            return read();",
                        102);
                case "InternalFieldInLambdaParameterFromAnInternalResult":
                    return BodyEdit.OfDerivedValue(
                        "return System.Array.Exists(HotReloadInternalMemberHost.InternalHosts(), host => host.InternalField > 0) ? 100 : 0;",
                        100);
                case "InternalStaticMethodInLocalFunction":
                    return BodyEdit.OfDerivedValue(
                        "int Read() { return HotReloadInternalMemberHost.InternalStaticValue() + 100; }\n"
                        + "            return Read();",
                        101);
                case "InternalStaticMethodInAnonymousMethod":
                    return BodyEdit.OfDerivedValue(
                        "System.Func<int> read = delegate { return HotReloadInternalMemberHost.InternalStaticValue() + 100; };\n"
                        + "            return read();",
                        101);
                case "InternalStaticMethodInAsyncMethod":
                    return new BodyEdit(AsyncValue, "return 50;", "return HotReloadInternalMemberHost.InternalStaticValue() + 100;", 101);
                case "InternalStaticMethodNextToLambdaReadingOwnPrivateFieldInGetter":
                    return new BodyEdit(
                        DerivedPropertyGetter,
                        "return 40;",
                        "System.Func<int> read = () => this._seed; return read() + HotReloadInternalMemberHost.InternalStaticValue();",
                        1001);
                case "InternalStaticMethodNextToOwnEventRaise":
                    return new BodyEdit(RaiseDerivedEvent, "return 60;", "return 60 + HotReloadInternalMemberHost.InternalStaticValue();", 61);
                case "InternalFieldReadIntoAVarCapturedByALambda":
                    return BodyEdit.OfDerivedValue(
                        "var seed = new HotReloadInternalMemberHost().InternalField;\n"
                        + "            System.Func<int> read = () => seed + 100;\n"
                        + "            return read();",
                        103);
                case "InternalStaticMethodPassedAsDelegate":
                    return BodyEdit.OfDerivedValue(
                        "System.Func<int> read = HotReloadInternalMemberHost.InternalStaticValue;\n"
                        + "            return read() + 100;",
                        101);
                case "InternalInstanceMethodPassedAsDelegate":
                    return BodyEdit.OfDerivedValue(
                        "System.Func<int> read = new HotReloadInternalMemberHost().InternalInstanceValue;\n"
                        + "            return read() + 100;",
                        102);
                case "InternalEventSubscription":
                    return BodyEdit.OfDerivedValue(
                        "HotReloadInternalMemberHost host = new HotReloadInternalMemberHost();\n"
                        + "            host.InternalEvent += HotReloadInternalMemberHost.NoOp;\n"
                        + "            return host.RaiseInternalEvent() + 100;",
                        101);
                case "InternalFieldInObjectInitializer":
                    return BodyEdit.OfDerivedValue("return new HotReloadInternalMemberHost { InternalField = 150 }.InternalField;", 150);
                case "InternalPropertyInPropertyPattern":
                    return BodyEdit.OfDerivedValue("return new HotReloadInternalMemberHost() is { InternalProperty: 4 } ? 104 : 0;", 104);
                default:
                    throw new ArgumentException("Unknown edit: " + editName);
            }
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(FixtureKind fixture, BodyEdit edit)
        {
            string path = FixturePath(FixtureFileName(fixture));
            string fragment = edit.Fragment ?? ValueBody(fixture);
            return await RunAsync(
                new[] { path },
                new Dictionary<string, string>
                {
                    [path] = WriteEdited(path, "UnpassedInternal" + fixture + edit.MethodName + ".cs", fragment, edit.Replacement)
                });
        }

        private static void AssertPatchedAsEdited(HotReloadOrchestratorResult result, FixtureKind fixture, BodyEdit edit)
        {
            Assert.That(FindRow(result, FixtureTypeName(fixture), edit.MethodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(CallFixture(fixture, edit.MethodName), Is.EqualTo(edit.EditedValue), FormatOutcomes(result));
        }

        // Why the value follows the row: a skipped row keeps the compiled body, and a patched row
        // must run the edited one. A call that throws fails the test, which is the point.
        private static void AssertAppliedAsEditedOrSkipped(HotReloadOrchestratorResult result, FixtureKind fixture, BodyEdit edit)
        {
            HotReloadMethodOutcomeKind kind = FindRow(result, FixtureTypeName(fixture), edit.MethodName).Kind;
            Assert.That(
                kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Patched).Or.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                FormatOutcomes(result));
            int expected = kind == HotReloadMethodOutcomeKind.Patched ? edit.EditedValue : CompiledValue(fixture, edit.MethodName);
            Assert.That(CallFixture(fixture, edit.MethodName), Is.EqualTo(expected), FormatOutcomes(result));
        }

        private static int CompiledValue(FixtureKind fixture, string methodName)
        {
            switch (methodName)
            {
                case DerivedValue:
                    return fixture == FixtureKind.Plain ? 10 : 9;
                case ClosureValue:
                    return 30;
                case ClosureSeedPlusValue:
                    return 1007;
                case AsyncValue:
                    return 50;
                case DerivedPropertyGetter:
                    return 40;
                case RaiseDerivedEvent:
                    return 60;
                default:
                    return 1000;
            }
        }

        private static int CallFixture(FixtureKind fixture, string methodName)
        {
            if (fixture == FixtureKind.Plain)
            {
                HotReloadPlainDerivedFixture plain = new HotReloadPlainDerivedFixture();
                switch (methodName)
                {
                    case DerivedValue:
                        return plain.DerivedValue();
                    case ClosureValue:
                        return plain.ClosureValue();
                    case ClosureSeedValue:
                        return plain.ClosureSeedValue();
                    case ClosureSeedPlusValue:
                        return plain.ClosureSeedPlusValue();
                    case IteratorValues:
                        return First(plain.IteratorValues());
                    case DerivedPropertyGetter:
                        return plain.DerivedProperty;
                    case AsyncValue:
                        return plain.AsyncValue().GetAwaiter().GetResult();
                    case RaiseDerivedEvent:
                        return plain.RaiseDerivedEvent();
                }
            }
            else
            {
                HotReloadPartialDerivedFixture partial = new HotReloadPartialDerivedFixture();
                switch (methodName)
                {
                    case DerivedValue:
                        return partial.DerivedValue();
                    case ClosureValue:
                        return partial.ClosureValue();
                    case ClosureSeedValue:
                        return partial.ClosureSeedValue();
                    case ClosureSeedPlusValue:
                        return partial.ClosureSeedPlusValue();
                    case IteratorValues:
                        return First(partial.IteratorValues());
                    case DerivedPropertyGetter:
                        return partial.DerivedProperty;
                    case AsyncValue:
                        return partial.AsyncValue().GetAwaiter().GetResult();
                    case RaiseDerivedEvent:
                        return partial.RaiseDerivedEvent();
                }
            }

            throw new ArgumentException("Unknown fixture method: " + methodName);
        }

        private static int CallCaller(string methodName)
        {
            HotReloadInternalMemberCaller caller = new HotReloadInternalMemberCaller();
            return methodName == "CallsInternal" ? caller.CallsInternal() : caller.PlainValue();
        }

        private static int First(IEnumerable<int> values)
        {
            foreach (int value in values)
            {
                return value;
            }

            throw new InvalidOperationException("The iterator yielded nothing.");
        }

        private static string FixtureFileName(FixtureKind fixture)
        {
            return fixture == FixtureKind.Plain ? PlainDerivedFileName : PartialDerivedFileName;
        }

        private static string FixtureTypeName(FixtureKind fixture)
        {
            return fixture == FixtureKind.Plain ? nameof(HotReloadPlainDerivedFixture) : nameof(HotReloadPartialDerivedFixture);
        }

        private static string ValueBody(FixtureKind fixture)
        {
            return fixture == FixtureKind.Plain ? PlainDerivedValueBody : PartialDerivedValueBody;
        }

        // Why the type and the parenthesis: a bare method name would also match a longer name that
        // contains it, or the same method on the other fixture type.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, string typeName, string methodName)
        {
            string labelPart = "." + typeName + "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + typeName + "." + methodName + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string[] files, Dictionary<string, string> overrides)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(overrides));
        }

        private static string WriteEdited(string sourcePath, string editedFileName, string fragment, string replacement)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                editedFileName,
                ReplaceOnce(File.ReadAllText(sourcePath), fragment, replacement));
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not call, and the assert would pass or fail for the wrong reason.
        private static string ReplaceOnce(string source, string fragment, string replacement)
        {
            int first = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Fragment missing from the fixture: " + fragment);
            Assert.That(
                source.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Fragment occurs more than once in the fixture: " + fragment);
            return source.Replace(fragment, replacement, StringComparison.Ordinal);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines) + "\nWarnings:\n" + string.Join("\n", result.Warnings);
        }

        // One edit of a fixture method: the fragment it replaces (null for the fixture's own
        // DerivedValue body, which differs between the two fixtures) and the value the edit returns.
        private sealed class BodyEdit
        {
            internal BodyEdit(string methodName, string fragment, string replacement, int editedValue)
            {
                MethodName = methodName;
                Fragment = fragment;
                Replacement = replacement;
                EditedValue = editedValue;
            }

            internal string MethodName { get; }
            internal string Fragment { get; }
            internal string Replacement { get; }
            internal int EditedValue { get; }

            internal static BodyEdit OfDerivedValue(string replacement, int editedValue)
            {
                return new BodyEdit(DerivedValue, null, replacement, editedValue);
            }
        }
    }
}
