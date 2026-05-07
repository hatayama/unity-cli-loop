using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test for AssemblyReferencePolicy
    /// Verify assembly reference policy for different security levels
    /// </summary>
    [TestFixture]
    public class AssemblyReferencePolicyTests
    {
        [Test]
        public void Level1_GetAssemblies_Includes_UnityEngine()
        {
            // Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.Restricted);
            
            // Assert
            Assert.IsNotNull(assemblies);
            Assert.Greater(assemblies.Count, 0);
            Assert.IsTrue(assemblies.Any(a => a.StartsWith("UnityEngine")));
        }

        [Test]
        public void Level1_GetAssemblies_Includes_UnityEditor()
        {
            // Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.Restricted);
            
            // Assert
            Assert.IsNotNull(assemblies);
            Assert.IsTrue(assemblies.Any(a => a.StartsWith("UnityEditor")));
        }

        [Test]
        public void Level1_GetAssemblies_Includes_SystemIO()
        {
            // New specification: Include all assemblies even in Restricted mode
            // Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.Restricted);
            
            // Assert
            Assert.IsNotNull(assemblies);
            // Confirm system assemblies are included
            Assert.IsTrue(assemblies.Any(a => a.StartsWith("System")), "System assemblies should be included in Restricted mode");
        }

        [Test]
        public void Level1_GetAssemblies_Includes_AssemblyCSharp()
        {
            // Arrange & Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.Restricted);
            
            // Assert
            Assert.IsNotNull(assemblies);
            // New specification: Allow Assembly-CSharp in Restricted mode (user-defined class execution feature)
            // Should include Assembly-CSharp if it exists
            Assembly assemblyCSharp = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            
            if (assemblyCSharp != null)
            {
                Assert.IsTrue(assemblies.Any(a => a == "Assembly-CSharp"), 
                    "Level 1 (Restricted) should include Assembly-CSharp when it exists");
            }
        }

        [Test]
        public void Level2_GetAssemblies_Includes_Multiple_Assemblies()
        {
            // Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.FullAccess);
            
            // Assert
            Assert.IsNotNull(assemblies);
            Assert.Greater(assemblies.Count, 10); // At least 10 or more assemblies
            Assert.IsTrue(assemblies.Any(a => a.StartsWith("System")));
            Assert.IsTrue(assemblies.Any(a => a.StartsWith("UnityEngine")));
        }

        [Test]
        public void Level2_AssemblyCSharp_Is_Included()
        {
            // Arrange & Act
            IReadOnlyList<string> assemblies = AssemblyReferencePolicy.GetAssemblies(DynamicCodeSecurityLevel.FullAccess);
            
            // Assert  
            Assert.IsNotNull(assemblies);
            // Assembly-CSharp is available in Level 2 (FullAccess)
            Assert.IsTrue(assemblies.Any(a => a == "Assembly-CSharp"), "Level 2 (FullAccess) should include Assembly-CSharp");
        }

        [Test]
        public void IsAssemblyAllowed_Level1_Allows_UnityEngine()
        {
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("UnityEngine", DynamicCodeSecurityLevel.Restricted));
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("UnityEngine.CoreModule", DynamicCodeSecurityLevel.Restricted));
        }

                [Test]
        public void IsAssemblyAllowed_Level1_Allows_SystemIO()
        {
            // New specification: Allow all assemblies in Restricted mode (block dangerous APIs after compilation)
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.IO", DynamicCodeSecurityLevel.Restricted));
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.IO.FileSystem", DynamicCodeSecurityLevel.Restricted));
        }

        [Test]
        public void IsAssemblyAllowed_Level1_Allows_SystemNet()
        {
            // New specification: Allow all assemblies in Restricted mode (block dangerous APIs after compilation)
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.Net", DynamicCodeSecurityLevel.Restricted));
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.Net.Http", DynamicCodeSecurityLevel.Restricted));
        }

        [Test]
        public void IsAssemblyAllowed_Level1_Allows_AssemblyCSharp()
        {
            // New specification: Allow Assembly-CSharp in Restricted mode (user-defined class execution feature)
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("Assembly-CSharp", DynamicCodeSecurityLevel.Restricted),
                "Level 1 (Restricted) should allow Assembly-CSharp (new feature)");
        }

        [Test]
        public void IsAssemblyAllowed_Level2_Allows_Basic_Assemblies()
        {
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("UnityEngine", DynamicCodeSecurityLevel.FullAccess));
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System", DynamicCodeSecurityLevel.FullAccess));
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("mscorlib", DynamicCodeSecurityLevel.FullAccess));
        }

        [Test]
        public void IsAssemblyAllowed_Level2_Allows_AssemblyCSharp()
        {
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("Assembly-CSharp", DynamicCodeSecurityLevel.FullAccess),
                "Level 2 (FullAccess) should allow Assembly-CSharp");
        }

        [Test]
        public void IsAssemblyAllowed_Level2_Allows_SystemReflectionEmit()
        {
            // New specification: Allow all assemblies in FullAccess mode
            // Act & Assert
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.Reflection.Emit", DynamicCodeSecurityLevel.FullAccess),
                "System.Reflection.Emit should be allowed in Level 2");
            Assert.IsTrue(AssemblyReferencePolicy.IsAssemblyAllowed("System.CodeDom", DynamicCodeSecurityLevel.FullAccess),
                "System.CodeDom should be allowed in Level 2");
        }

        [Test]
        public void IsAssemblyAllowed_Returns_False_For_Empty_Or_Null()
        {
            // Act & Assert
            Assert.IsFalse(AssemblyReferencePolicy.IsAssemblyAllowed("", DynamicCodeSecurityLevel.FullAccess));
            Assert.IsFalse(AssemblyReferencePolicy.IsAssemblyAllowed(null, DynamicCodeSecurityLevel.FullAccess));
            Assert.IsFalse(AssemblyReferencePolicy.IsAssemblyAllowed("  ", DynamicCodeSecurityLevel.FullAccess));
        }
    }
}