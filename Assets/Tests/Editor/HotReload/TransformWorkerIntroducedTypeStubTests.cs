using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which bodies of a newly introduced type the preparation stubs because they call
    /// members this reload adds to a type that already exists, and what the stubbed artifact
    /// source, the stub keys and the recorded fingerprint look like.
    /// </summary>
    public sealed class TransformWorkerIntroducedTypeStubTests
    {
        private const string StubThrow = "throw new global::System.InvalidOperationException(";

        // The compiled host reopened with the members this reload adds: a method, an overload of
        // a compiled name, a field, a property and an event.
        private const string HostWithAdditionsSource =
            "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
            + "{\n"
            + "    public sealed class HotReloadCrossFileAddedMemberHost\n"
            + "    {\n"
            + "        public int Value() { return 1; }\n"
            + "        public int Scaled(int factor) { return factor; }\n"
            + "        public int Scaled(int factor, int offset) { return factor + offset; }\n"
            + "        public int AddedValue() { return 42; }\n"
            + "        public int AddedField;\n"
            + "        public int AddedProperty { get { return 5; } }\n"
            + "        public event System.Action AddedEvent;\n"
            + "    }\n"
            + "}\n";

        private const string CallerUsings = "using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload;\n";

        /// <summary>
        /// A method that calls an added method is stubbed in the artifact source and keyed the way
        /// the transform keys its entry, its record marks the body as stubbed, and a method that
        /// calls nothing added keeps its body.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_MethodCallingAnAddedMethod_StubsOnlyThatBody()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "MethodCallsAddedMethod",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int Uses() { return new HotReloadCrossFileAddedMemberHost().AddedValue(); }\n"
                + "        public int Plain() { return 7; }\n"
                + "    }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.EqualTo(new[] { "Example.Stubs.Caller::Uses()" }));
            Assert.That(
                introducedType.source,
                Does.Contain("public int Uses() { " + StubThrow + "\"'Example.Stubs.Caller.Uses' calls members that a hot reload added"));
            Assert.That(introducedType.source, Does.Not.Contain("AddedValue"));
            Assert.That(introducedType.source, Does.Contain("public int Plain() { return 7; }"));
            Assert.That(introducedType.source, Does.Contain("Reload 'Assets/First.cs' again, or run 'uloop compile'."));
            Assert.That(ReadStubbedBodyCount(introducedType), Is.EqualTo(1));
        }

        /// <summary>
        /// Expression-bodied methods and getters keep their arrow form and a get accessor keeps its
        /// block, and each getter is keyed by its get method.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ExpressionBodiesAndGetters_KeepTheirBodyForm()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "ExpressionBodies",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int Arrow() => new HotReloadCrossFileAddedMemberHost().AddedValue();\n"
                + "        public int Getter => new HotReloadCrossFileAddedMemberHost().AddedValue();\n"
                + "        public int Accessor { get { return new HotReloadCrossFileAddedMemberHost().AddedValue(); } }\n"
                + "    }\n"
                + "}\n");

            Assert.That(
                introducedType.stubbedMethodKeys,
                Is.EqualTo(new[]
                {
                    "Example.Stubs.Caller::Arrow()",
                    "Example.Stubs.Caller::get_Getter()",
                    "Example.Stubs.Caller::get_Accessor()"
                }));
            Assert.That(introducedType.source, Does.Contain("public int Arrow() => " + StubThrow));
            Assert.That(introducedType.source, Does.Contain("public int Getter => " + StubThrow));
            Assert.That(introducedType.source, Does.Contain("public int Accessor { get { " + StubThrow));
            Assert.That(introducedType.source, Does.Not.Contain("AddedValue"));
            Assert.That(ReadStubbedBodyCount(introducedType), Is.EqualTo(3));
        }

        /// <summary>
        /// Bodies the transform cannot patch onto an artifact keep calling the added member, so the
        /// artifact keeps failing to compile on them instead of shipping a stub nothing replaces.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_BodiesTheTransformCannotPatch_AreNotStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "UnpatchableBodies",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        private int _seed = new HotReloadCrossFileAddedMemberHost().AddedValue();\n"
                + "        public Caller() { _seed = new HotReloadCrossFileAddedMemberHost().AddedValue(); }\n"
                + "        public int WithSetter { get { return new HotReloadCrossFileAddedMemberHost().AddedValue(); } set { _seed = value; } }\n"
                + "        public int this[int index] { get { return new HotReloadCrossFileAddedMemberHost().AddedValue(); } }\n"
                + "        public static int operator +(Caller left, int right) { return new HotReloadCrossFileAddedMemberHost().AddedValue(); }\n"
                + "    }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.Empty);
            Assert.That(introducedType.source, Does.Not.Contain(StubThrow));
            Assert.That(ReadStubbedBodyCount(introducedType), Is.EqualTo(0));
        }

        /// <summary>
        /// A call that resolves to an overload this reload adds to a compiled name is stubbed, since
        /// the compiled type holds only the other overload.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_CallToAnAddedOverload_IsStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "AddedOverload",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int Uses() { return new HotReloadCrossFileAddedMemberHost().Scaled(2, 3); }\n"
                + "    }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.EqualTo(new[] { "Example.Stubs.Caller::Uses()" }));
        }

        /// <summary>
        /// A call that resolves to the overload the compiled type already holds is not stubbed, even
        /// though this reload adds another overload of the same name.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_CallToTheCompiledOverload_IsNotStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "CompiledOverload",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int Uses() { return new HotReloadCrossFileAddedMemberHost().Scaled(2); }\n"
                + "    }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.Empty);
            Assert.That(introducedType.source, Does.Contain(".Scaled(2)"));
        }

        /// <summary>
        /// Reading an added field or an added property is stubbed the same way as calling an added method.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_AddedFieldAndProperty_AreStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "AddedFieldAndProperty",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int UsesField() { return new HotReloadCrossFileAddedMemberHost().AddedField; }\n"
                + "        public int UsesProperty() { return new HotReloadCrossFileAddedMemberHost().AddedProperty; }\n"
                + "    }\n"
                + "}\n");

            Assert.That(
                introducedType.stubbedMethodKeys,
                Is.EqualTo(new[] { "Example.Stubs.Caller::UsesField()", "Example.Stubs.Caller::UsesProperty()" }));
        }

        /// <summary>
        /// A body that uses an added event is not stubbed, even when it also calls an added method:
        /// the transform never patches such a body, so a stub would stay in place.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_BodyUsingAnAddedEvent_IsNotStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "AddedEvent",
                CallerUsings
                + "namespace Example.Stubs\n"
                + "{\n"
                + "    public class Caller\n"
                + "    {\n"
                + "        public int Subscribes()\n"
                + "        {\n"
                + "            HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();\n"
                + "            host.AddedEvent += () => { };\n"
                + "            return host.AddedValue();\n"
                + "        }\n"
                + "    }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.Empty);
            Assert.That(introducedType.source, Does.Contain("host.AddedEvent += () => { };"));
        }

        /// <summary>
        /// A call into a type this same reload introduces is not stubbed: that type compiles into
        /// the same artifact, so the call binds there.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_CallIntoATypeThisReloadIntroduces_IsNotStubbed()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("StubSameRunType");
            string callerPath = Path.Combine(directory, "First.cs");
            string helperPath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                callerPath,
                "namespace Example.Stubs { public class Caller { public int Uses() { return new Helper().Help(); } } }");
            File.WriteAllText(helperPath, "namespace Example.Stubs { public class Helper { public int Help() { return 3; } } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(callerPath, helperPath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].stubbedMethodKeys, Is.Empty);
            Assert.That(result.Output.files[1].introducedTypes[0].stubbedMethodKeys, Is.Empty);
        }

        /// <summary>
        /// A call whose added member is declared in a file this run does not compile does not bind,
        /// so nothing tells it from a typo and the body is not stubbed.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_AdditionOutsideTheRun_IsNotStubbed()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("StubAdditionOutsideRun");
            string callerPath = Path.Combine(directory, "First.cs");
            string unrelatedPath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                callerPath,
                CallerUsings
                + "namespace Example.Stubs { public class Caller { public int Uses() { return new HotReloadCrossFileAddedMemberHost().AddedValue(); } } }");
            File.WriteAllText(unrelatedPath, "namespace Unrelated { public class Untouched { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(callerPath, unrelatedPath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerIntroducedTypeDto caller = result.Output.files[0].introducedTypes[0];
            Assert.That(caller.stubbedMethodKeys, Is.Empty);
            Assert.That(caller.source, Does.Contain(".AddedValue()"));
        }

        /// <summary>
        /// A type declared outside any namespace is stubbed the same way, through the artifact
        /// source that has no namespace to wrap it in.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_TypeWithoutNamespace_IsStubbed()
        {
            TransformWorkerIntroducedTypeDto introducedType = await PrepareSingleTypeAsync(
                "NoNamespace",
                CallerUsings
                + "public class StubNoNamespaceCaller\n"
                + "{\n"
                + "    public int Uses() { return new HotReloadCrossFileAddedMemberHost().AddedValue(); }\n"
                + "}\n");

            Assert.That(introducedType.stubbedMethodKeys, Is.EqualTo(new[] { "StubNoNamespaceCaller::Uses()" }));
            Assert.That(introducedType.source, Does.Not.Contain("namespace"));
            Assert.That(introducedType.source, Does.Contain("public int Uses() { " + StubThrow));
        }

        private static async Task<TransformWorkerIntroducedTypeDto> PrepareSingleTypeAsync(string caseName, string callerSource)
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("Stub" + caseName);
            string callerPath = Path.Combine(directory, "First.cs");
            string hostPath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(callerPath, callerSource);
            File.WriteAllText(hostPath, HostWithAdditionsSource);

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(callerPath, hostPath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Is.Empty);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[1].introducedTypes, Is.Empty);
            return result.Output.files[0].introducedTypes[0];
        }

        private static int ReadStubbedBodyCount(TransformWorkerIntroducedTypeDto introducedType)
        {
            bool parsed = HotReloadIntroducedTypeFingerprint.TryParse(
                introducedType.declarationFingerprint,
                out HotReloadIntroducedTypeFingerprint fingerprint);
            Assert.That(parsed, Is.True, "The record must carry a canonical fingerprint.");
            int count = 0;
            foreach (HotReloadIntroducedTypeMemberFingerprint member in fingerprint.Members)
            {
                if (member.HasStubbedBody)
                {
                    count++;
                }
            }

            Assert.That(fingerprint.HoldsStubbedBodies, Is.EqualTo(count > 0));
            return count;
        }
    }
}
