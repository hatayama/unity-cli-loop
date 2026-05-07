using NUnit.Framework;
using System;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Tests for DynamicCodeSecurityManager
    /// Verify security level management and dangerous API detection functionality
    /// </summary>
    [TestFixture]
    public class DynamicCodeSecurityManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            // No setup required for v4.0 stateless design
        }

        [Test]
        public void CanExecute_ReturnsTrueForLevel1()
        {
            // Act
            bool canExecute = DynamicCodeSecurityManager.CanExecute(DynamicCodeSecurityLevel.Restricted);
            
            // Assert
            Assert.IsTrue(canExecute);
        }

        [Test]
        public void CanExecute_ReturnsTrueForLevel2()
        {
            // Act
            bool canExecute = DynamicCodeSecurityManager.CanExecute(DynamicCodeSecurityLevel.FullAccess);
            
            // Assert
            Assert.IsTrue(canExecute);
        }

        [Test]
        public void GetAllowedAssemblies_ReturnsAppropriateListForEachLevel()
        {
            // Act
            var level1Assemblies = DynamicCodeSecurityManager.GetAllowedAssemblies(DynamicCodeSecurityLevel.Restricted);
            var level2Assemblies = DynamicCodeSecurityManager.GetAllowedAssemblies(DynamicCodeSecurityLevel.FullAccess);

            Assert.Greater(level1Assemblies.Count, 0);
            Assert.AreEqual(level2Assemblies.Count, level1Assemblies.Count);
        }
    }
}