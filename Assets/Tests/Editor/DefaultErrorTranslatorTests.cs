using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Default Error Translator behavior.
    /// </summary>
    [TestFixture]
    public class DefaultErrorTranslatorTests
    {
        private DefaultErrorTranslator _translator;
        private DefaultErrorFormatter _formatter;

        [SetUp]
        public void SetUp()
        {
            _translator = new DefaultErrorTranslator();
            _formatter = new DefaultErrorFormatter();
        }

        // ── ToolDisabledException ──────────────────────────────────────

        [Test]
        public void TranslateFromException_ToolDisabled_ShouldReturnToolNameInMessage()
        {
            ToolDisabledException exception = new("compile");

            TranslationOutput result = _translator.TranslateFromException(exception);

            StringAssert.Contains("compile", result.FriendlyMessage);
            StringAssert.Contains("disabled", result.FriendlyMessage);
        }

        [Test]
        public void TranslateFromException_ToolDisabled_ShouldIncludeMenuPath()
        {
            ToolDisabledException exception = new("compile");

            TranslationOutput result = _translator.TranslateFromException(exception);

            StringAssert.Contains(UnityCliLoopUIConstants.TOOL_SETTINGS_MENU_PATH, result.FriendlyMessage);
        }

        [Test]
        public void TranslateFromException_ToolDisabled_ShouldHaveExplanation()
        {
            ToolDisabledException exception = new("compile");

            TranslationOutput result = _translator.TranslateFromException(exception);

            Assert.IsNotEmpty(result.Explanation);
        }

        [Test]
        public void TranslateFromException_ToolDisabled_ShouldHaveSolution()
        {
            ToolDisabledException exception = new("get-logs");

            TranslationOutput result = _translator.TranslateFromException(exception);

            Assert.AreEqual(1, result.Solutions.Count);
            StringAssert.Contains("get-logs", result.Solutions[0]);
        }

        // ── Severity ───────────────────────────────────────────────────

        [Test]
        public void DetermineSeverity_ToolDisabled_ShouldBeMedium()
        {
            ToolDisabledException exception = new("compile");
            TranslationOutput translation = _translator.TranslateFromException(exception);

            UserFriendlyErrorDto dto = _formatter.Format(translation, exception.Message, exception);

            Assert.AreEqual(ErrorSeverity.Medium, dto.Severity);
        }

        // ── Other exception types still work ───────────────────────────

        [Test]
        public void TranslateFromException_GenericException_ShouldReturnInternalError()
        {
            System.Exception exception = new("something went wrong");

            TranslationOutput result = _translator.TranslateFromException(exception);

            Assert.AreEqual("Internal error", result.FriendlyMessage);
        }

        [Test]
        public void TranslateFromException_Null_ShouldReturnInternalError()
        {
            TranslationOutput result = _translator.TranslateFromException(null);

            Assert.AreEqual("Internal error", result.FriendlyMessage);
        }
    }
}
