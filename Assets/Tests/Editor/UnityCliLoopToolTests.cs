using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the typed tool base class turns a request's parameter token into its schema.
    /// </summary>
    public sealed class UnityCliLoopToolTests
    {
        /// <summary>
        /// Verifies a parameter token that deserializes to no object still reaches the tool as a default schema.
        /// </summary>
        [Test]
        public void ExecuteAsync_WhenTokenDeserializesToNull_PassesDefaultSchema()
        {
            RecordingTool tool = new RecordingTool();

            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(JValue.CreateUndefined(), CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
            Assert.That(tool.ReceivedSchema, Is.Not.Null);
            Assert.That(tool.ReceivedSchema.Count, Is.EqualTo(5));
        }

        public sealed class RecordingSchema : UnityCliLoopToolSchema
        {
            public int Count { get; set; } = 5;
        }

        public sealed class RecordingResponse : UnityCliLoopToolResponse
        {
        }

        private sealed class RecordingTool : UnityCliLoopTool<RecordingSchema, RecordingResponse>
        {
            public override string ToolName => "recording-tool";

            internal RecordingSchema ReceivedSchema { get; private set; }

            protected override Task<RecordingResponse> ExecuteAsync(RecordingSchema parameters, CancellationToken ct)
            {
                ReceivedSchema = parameters;
                return Task.FromResult(new RecordingResponse());
            }
        }
    }
}
