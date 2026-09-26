using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which initializer of a field added next to a retained declaration can run in the
    /// shim lambda: only a public constructor of a type the verified artifact mapping holds, and
    /// nothing else that needs an instance.
    /// </summary>
    public sealed class HotReloadRetainedArtifactInitializerTests
    {
        private const string SkipReasonFragment = "has an initializer that is not a literal or an externally visible static member";

        private const string SourceFormat =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public {0} Cache = {1};\n"
            + "\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + ({2});\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: a field added next to a retained declaration keeps its initializer when it only
        /// constructs a type the verified mapping holds, so the method that reads it is patched
        /// through the added-field store instead of being skipped.
        /// </summary>
        [Test]
        public async Task Initializer_PublicConstructorOfMappedType_IsEmitted()
        {
            TransformWorkerClientResult result = await RunAsync(
                "MappedConstructor",
                "Retained",
                "new Retained()",
                "Cache == null ? 0 : 1");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, "Read"), Is.Null);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        // The same world with an ordinary method body of the retained type available to edit, so a
        // run can reach the verdict a body edit produces instead of the identical one.
        private const string BodyEditedSourceFormat =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Twice()\n"
            + "        {\n"
            + "            return Value * {0};\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public Retained Cache = new Retained();\n"
            + "\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + (Cache == null ? 0 : 1);\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: a field added with an initializer that constructs a retained type keeps its
        /// initializer in the reload that edits a method body of that type too, because the
        /// artifact assembly still serves the type the lambda constructs.
        /// </summary>
        [Test]
        public async Task Initializer_ConstructorOfRetainedTypeWhoseBodyThisRunEdits_IsEmitted()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "BodyEditedConstructor",
                    BodyEditedSourceFormat.Replace("{0}", "2"),
                    new[] { "Twice" },
                    new string[0]);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BodyEditedSourceFormat.Replace("{0}", "4"));

            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildTransformInput(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            // The body edit has to be on the retained path for this run to describe the case at
            // all: the edited method is patched onto the artifact assembly, not onto the file's
            // own assembly.
            Assert.That(FindHomeAssemblyName(result, "Twice"), Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindSkipReason(result, "Read"), Is.Null);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        // The retained type declaring a constructor the artifact assembly does not hold, which is
        // what a constructor this reload adds to the type looks like to the lambda: the assembly
        // the domain loaded is the one that is constructed, and it has no such constructor.
        private const string SeededConstructorSourceFormat =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public Retained(int seed)\n"
            + "        {\n"
            + "            Value = seed;\n"
            + "        }\n"
            + "\n"
            + "        public int Twice()\n"
            + "        {\n"
            + "            return Value * {0};\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public Retained Cache = new Retained(7);\n"
            + "\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + (Cache == null ? 0 : 1);\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // A type nested in the compiled host, which no record can ever hold: an introduced type is
        // never nested, and its metadata name nests with '/' where the source spells it with '.'.
        private const string NestedTypeSourceFormat =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Twice()\n"
            + "        {\n"
            + "            return Value * {0};\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public class Inner\n"
            + "        {\n"
            + "        }\n"
            + "\n"
            + "        public Inner Cache = new Inner();\n"
            + "\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + (Cache == null ? 0 : 1);\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // The declaration of the retained type on its own, so the file that adds the field holds
        // no declaration of it and has to read the record of the other file of the group.
        private const string RetainedOnlySourceFormat =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Compute()\n"
            + "        {\n"
            + "            return Value * {0};\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string CallerSiblingSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public Retained Cache = new Retained();\n"
            + "\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + (Cache == null ? 0 : 1);\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: a constructor the artifact assembly does not hold is refused even while the type
        /// is on the retained path, because the instance the lambda creates comes from that
        /// assembly and the source spelling of the constructor is not what runs.
        /// </summary>
        [Test]
        public async Task Initializer_ConstructorTheArtifactDoesNotHold_IsRefused()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "SeededConstructor",
                    SeededConstructorSourceFormat.Replace("{0}", "2"),
                    new[] { "Twice" },
                    new string[0]);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, SeededConstructorSourceFormat.Replace("{0}", "4"));

            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildTransformInput(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            // The refusal has to come from the constructor and not from the run losing the record:
            // the edited body is still patched onto the artifact assembly.
            Assert.That(FindHomeAssemblyName(result, "Twice"), Is.EqualTo(fixture.ArtifactAssemblyName));
            AssertReadIsRefused(result);
        }

        /// <summary>
        /// What: constructing a type nested in the compiled host stays refused while the run also
        /// serves a retained type, so the allowance follows the records and not a name that only
        /// looks like one of them.
        /// </summary>
        [Test]
        public async Task Initializer_ConstructorOfTypeNestedInTheHost_IsRefused()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "NestedConstructor",
                    NestedTypeSourceFormat.Replace("{0}", "2"),
                    new[] { "Twice" },
                    new string[0]);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, NestedTypeSourceFormat.Replace("{0}", "4"));

            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildTransformInput(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            AssertReadIsRefused(result);
        }

        /// <summary>
        /// What: the file that adds the field keeps its initializer when another file of the same
        /// run declares the retained type, because the records belong to the run rather than to
        /// the file that declares the type.
        /// </summary>
        [Test]
        public async Task Initializer_ConstructorOfRetainedTypeDeclaredInAnotherFile_IsEmitted()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithSiblingSourceAsync(
                    "CrossFileConstructor",
                    RetainedOnlySourceFormat.Replace("{0}", "2"),
                    CallerSiblingSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, RetainedOnlySourceFormat.Replace("{0}", "4"));

            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildGroupTransformInput(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, "Read"), Is.Null);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: a constructor the artifact assembly does not expose is refused, because the
        /// shim assembly cannot reach it once the type is served from the artifact.
        /// </summary>
        [Test]
        public async Task Initializer_NonPublicConstructorOfMappedType_IsRefused()
        {
            TransformWorkerClientResult result = await RunAsync(
                "RestrictedConstructor",
                "RetainedRestricted",
                "new RetainedRestricted()",
                "Cache == null ? 0 : 1");

            AssertReadIsRefused(result);
        }

        /// <summary>
        /// What: an instance method call on a mapped type is still refused, so the allowance
        /// covers construction only and not everything the mapped type exposes.
        /// </summary>
        [Test]
        public async Task Initializer_InstanceMethodOnMappedType_IsRefused()
        {
            TransformWorkerClientResult result = await RunAsync(
                "MappedInstanceMethod",
                "int",
                "new Retained().Compute()",
                "Cache");

            AssertReadIsRefused(result);
        }

        /// <summary>
        /// What: reading an instance property of a mapped type is refused for the same reason as
        /// an instance method call.
        /// </summary>
        [Test]
        public async Task Initializer_InstancePropertyOnMappedType_IsRefused()
        {
            TransformWorkerClientResult result = await RunAsync(
                "MappedInstanceProperty",
                "int",
                "new Retained().Number",
                "Cache");

            AssertReadIsRefused(result);
        }

        /// <summary>
        /// What: constructing a public type the mapping does not hold stays refused, so the
        /// allowance is decided by the mapping and not by the constructor alone.
        /// </summary>
        [Test]
        public async Task Initializer_ConstructorOfUnmappedType_IsRefused()
        {
            TransformWorkerClientResult result = await RunAsync(
                "UnmappedConstructor",
                "System.Text.StringBuilder",
                "new System.Text.StringBuilder()",
                "Cache == null ? 0 : 1");

            AssertReadIsRefused(result);
        }

        private static void AssertReadIsRefused(TransformWorkerClientResult result)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string reason = FindSkipReason(result, "Read");
            Assert.That(reason, Is.Not.Null, "The method reading the added field must be skipped.");
            Assert.That(reason, Does.Contain(SkipReasonFragment));
        }

        private static async Task<TransformWorkerClientResult> RunAsync(
            string name,
            string fieldTypeName,
            string initializer,
            string readExpression)
        {
            string editedSource = SourceFormat
                .Replace("{0}", fieldTypeName)
                .Replace("{1}", initializer)
                .Replace("{2}", readExpression);
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync(name, editedSource);

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildTransformInput(
                    new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) }),
                CancellationToken.None);
        }

        private static string FindHomeAssemblyName(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry.homeAssemblyName;
                }
            }

            return null;
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodName))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
                }
            }

            return null;
        }
    }
}
