using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Coverage for the base-call stand-in naming scheme the worker writes and the Editor parses
    /// back to find the base method a stand-in call must be redirected to.
    /// </summary>
    public class HotReloadBaseCallStandInNamesTests
    {
        /// <summary>What: a formatted stand-in name parses back to the same depth and method name.</summary>
        [Test]
        public void Format_ThenTryParse_RoundTripsDepthAndMethodName()
        {
            string name = HotReloadBaseCallStandInNames.Format(2, "Compute", 0);

            bool parsed = HotReloadBaseCallStandInNames.TryParse(name, out int depth, out string methodName);

            Assert.That(parsed, Is.True);
            Assert.That(depth, Is.EqualTo(2));
            Assert.That(methodName, Is.EqualTo("Compute"));
        }

        /// <summary>What: a method name that itself contains "__" survives the round trip intact.</summary>
        [Test]
        public void TryParse_MethodNameContainingDoubleUnderscore_KeepsTheWholeName()
        {
            string name = HotReloadBaseCallStandInNames.Format(1, "On__Enable", 3);

            bool parsed = HotReloadBaseCallStandInNames.TryParse(name, out int depth, out string methodName);

            Assert.That(parsed, Is.True);
            Assert.That(depth, Is.EqualTo(1));
            Assert.That(methodName, Is.EqualTo("On__Enable"));
        }

        /// <summary>
        /// What: names without the prefix, without a numeric positive depth, without an index
        /// segment, or whose separators overlap are not stand-in names.
        /// </summary>
        [TestCase("Compute")]
        [TestCase("__uloopBaseCall__")]
        [TestCase("__uloopBaseCall__dx__M__0")]
        [TestCase("__uloopBaseCall__d0__M__0")]
        [TestCase("__uloopBaseCall__d1__M")]
        [TestCase("__uloopBaseCall__d1___0")]
        public void TryParse_NonStandInNames_ReturnsFalse(string name)
        {
            bool parsed = HotReloadBaseCallStandInNames.TryParse(name, out int _, out string _);

            Assert.That(parsed, Is.False);
        }
    }
}
