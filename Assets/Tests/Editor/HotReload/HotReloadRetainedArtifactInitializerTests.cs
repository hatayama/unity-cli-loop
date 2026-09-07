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
        private const string SkipReasonFragment = "Added field initializer";

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

            return await TransformWorkerClient.RunAsync(
                fixture.BuildTransformInput(
                    new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) }),
                CancellationToken.None);
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodName))
                {
                    return skipped.reason;
                }
            }

            return null;
        }
    }
}
