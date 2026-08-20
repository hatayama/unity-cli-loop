using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies pause-point caller-frame DTOs copy Note from the runtime frame.
    /// </summary>
    [TestFixture]
    public sealed class PausePointStatusCallerFrameTests
    {
        private const string WantDynamicMethodNote =
            "dynamic method (patched by hot reload or pause-point instrumentation); no debug symbols";

        /// <summary>
        /// What: FromCallerFrame copies Note so a selector-set note is not dropped at the bridge.
        /// </summary>
        [Test]
        public void FromCallerFrame_WhenNoteIsSet_CopiesNoteOntoTheStatusDto()
        {
            UloopPausePointCallerFrame source = new(
                "Game.Input.HandleJump",
                null,
                0,
                WantDynamicMethodNote);

            PausePointStatusCallerFrame dto = PausePointStatusCallerFrame.FromCallerFrame(source);

            Assert.That(dto.Method, Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(dto.File, Is.Null);
            Assert.That(dto.Line, Is.EqualTo(0));
            Assert.That(dto.Note, Is.EqualTo(WantDynamicMethodNote));
        }

        /// <summary>
        /// What: PausePointCallerFrame.FromSnapshot copies Note so enable/clear history frames
        /// do not drop a selector-set note.
        /// </summary>
        [Test]
        public void PausePointCallerFrameFromSnapshot_WhenNoteIsSet_CopiesNoteOntoTheHistoryDto()
        {
            UloopPausePointCallerFrame source = new(
                "Game.Input.HandleJump",
                null,
                0,
                WantDynamicMethodNote);

            PausePointCallerFrame dto = PausePointCallerFrame.FromSnapshot(source);

            Assert.That(dto.Method, Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(dto.File, Is.Null);
            Assert.That(dto.Line, Is.EqualTo(0));
            Assert.That(dto.Note, Is.EqualTo(WantDynamicMethodNote));
        }

        /// <summary>
        /// What: a null Note is omitted from JSON so File-bearing fixture frames keep their shape.
        /// </summary>
        [Test]
        public void PausePointStatusCallerFrame_WhenNoteIsNull_OmitsNoteFromJson()
        {
            PausePointStatusCallerFrame frame = new()
            {
                Method = "Game.AI.Tick",
                File = "Assets/Scripts/AI.cs",
                Line = 44,
                Note = null
            };

            string json = JsonConvert.SerializeObject(
                frame,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);

            Assert.That(json, Does.Not.Contain("Note"));
        }
    }
}
