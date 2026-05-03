using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
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
