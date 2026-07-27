#if ULOOP_HAS_INPUT_SYSTEM
using System.Collections.Generic;
using System.Linq;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    [TestFixture]
    public class KeyboardKeyNameSuggesterTests
    {
        // Verifies bare digit keys suggest both Digit and Numpad enum names.
        [Test]
        public void Suggest_ForBareDigit_IncludesDigitAndNumpadNames()
        {
            IReadOnlyList<string> suggestions = KeyboardKeyNameSuggester.Suggest("3");

            Assert.That(suggestions, Does.Contain("Digit3"));
            Assert.That(suggestions, Does.Contain("Numpad3"));
        }

        // Verifies non-ASCII digits do not produce Digit/Numpad names that no Key enum value has.
        [Test]
        public void Suggest_ForFullWidthDigit_DoesNotSuggestDigitNames()
        {
            IReadOnlyList<string> suggestions = KeyboardKeyNameSuggester.Suggest("３");

            Assert.That(suggestions, Does.Not.Contain("Digit３"));
            Assert.That(suggestions, Does.Not.Contain("Numpad３"));
            Assert.That(suggestions.Any(name => name.StartsWith("Digit")), Is.False);
        }

        // Verifies partial key names still return close enum matches.
        [Test]
        public void Suggest_ForPartialName_ReturnsPrefixMatches()
        {
            IReadOnlyList<string> suggestions = KeyboardKeyNameSuggester.Suggest("LeftSh");

            Assert.That(suggestions.Any(name => name == "LeftShift"), Is.True);
        }
    }
}
#endif
