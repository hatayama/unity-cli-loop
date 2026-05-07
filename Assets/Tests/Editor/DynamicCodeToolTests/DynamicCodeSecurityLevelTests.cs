using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Security Level behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeSecurityLevelTests
    {
        [Test]
        public void Verify_Correct_Level_Values()
        {
            Assert.AreEqual(1, (int)DynamicCodeSecurityLevel.Restricted);
            Assert.AreEqual(2, (int)DynamicCodeSecurityLevel.FullAccess);
        }

        [Test]
        public void Verify_All_Levels_Are_Defined()
        {
            string[] expectedNames = { "Restricted", "FullAccess" };
            string[] actualNames = System.Enum.GetNames(typeof(DynamicCodeSecurityLevel));

            Assert.AreEqual(expectedNames.Length, actualNames.Length);
            foreach (string name in expectedNames)
            {
                Assert.Contains(name, actualNames);
            }
        }
    }
}
