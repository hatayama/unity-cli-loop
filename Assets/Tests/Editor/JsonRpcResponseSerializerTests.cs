using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests JSON-RPC serializer settings compatibility with the shared CLI wire contract.
    /// </summary>
    [TestFixture]
    public sealed class JsonRpcResponseSerializerTests
    {
        [Test]
        public void Settings_WhenReadFromInfrastructure_UsesSharedWireContractInstance()
        {
            // Tests that JSON-RPC responses and stored compile results cannot drift into separate serializer shapes.
            Assert.That(JsonRpcResponseSerializer.Settings, Is.SameAs(UnityCliLoopJsonResponseSerializerSettings.Settings));
        }
    }
}
