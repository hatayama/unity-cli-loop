using System.Diagnostics;
using System.IO;
using System.Linq;

using Mono.Cecil;
using Mono.Cecil.Cil;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies post-line site selection against the real Roslyn IL and portable PDB emitted
    /// for the editor test assembly.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointCecilPostLineSiteLocatorTests
    {
        private const string AssemblyRelativePath = "Library/ScriptAssemblies/UnityCLILoop.Tests.Editor.dll";
        private const string FixtureTypeName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.SourcePausePointPostLineSiteFixture";

        /// <summary>
        /// What: a one-line if assignment lands after its body at the next visible line in
        /// the real Debug or Release Roslyn IL layout.
        /// </summary>
        [Test]
        public void Locate_RealAssemblySameLineIfAssignment_LandsAtNextLine()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            MethodDefinition method = FindFixtureMethod(assembly, "AssignWhenTrue");
            SequencePoint selected = FindVisibleSequencePoint(method, 10);
            SequencePoint nextLine = FindNextVisibleLineSequencePoint(method, selected);

            SourcePausePointPostLineSite site = SourcePausePointCecilPostLineSiteLocator.Locate(method, selected);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.Fallthrough));
            Assert.That(method.Body.Instructions[site.InstructionIndex].Offset, Is.EqualTo(nextLine.Offset));
        }

        /// <summary>
        /// What: a one-line if return stays before its conditional branch so both outcomes
        /// can reach the capture in the real Debug or Release Roslyn IL layout.
        /// </summary>
        [Test]
        public void Locate_RealAssemblySameLineIfReturn_StaysBeforeConditionalBranch()
        {
            using AssemblyDefinition assembly = ReadEditorTestAssembly();
            MethodDefinition method = FindFixtureMethod(assembly, "ReturnWhenTrue");
            SequencePoint selected = FindVisibleSequencePoint(method, 16);

            SourcePausePointPostLineSite site = SourcePausePointCecilPostLineSiteLocator.Locate(method, selected);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(
                method.Body.Instructions[site.InstructionIndex].OpCode.FlowControl,
                Is.EqualTo(FlowControl.Cond_Branch));
        }

        private static AssemblyDefinition ReadEditorTestAssembly()
        {
            string assemblyPath = Path.GetFullPath(AssemblyRelativePath);
            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider()
            };

            return AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
        }

        private static MethodDefinition FindFixtureMethod(AssemblyDefinition assembly, string methodName)
        {
            TypeDefinition fixture = assembly.MainModule.GetType(FixtureTypeName);
            Debug.Assert(fixture != null, "The compiled fixture type must exist.");
            return fixture.Methods.Single(method => method.Name == methodName);
        }

        private static SequencePoint FindVisibleSequencePoint(MethodDefinition method, int sourceLine)
        {
            SequencePoint point = method.DebugInformation.SequencePoints
                .Where(candidate => !candidate.IsHidden && candidate.StartLine == sourceLine)
                .OrderBy(candidate => candidate.Offset)
                .FirstOrDefault();
            Debug.Assert(point != null, "The fixture source line must have a visible sequence point.");
            return point;
        }

        private static SequencePoint FindNextVisibleLineSequencePoint(
            MethodDefinition method,
            SequencePoint selected)
        {
            SequencePoint nextLine = method.DebugInformation.SequencePoints
                .Where(point => !point.IsHidden && point.Offset > selected.Offset && point.StartLine != selected.StartLine)
                .OrderBy(point => point.Offset)
                .FirstOrDefault();
            Debug.Assert(nextLine != null, "The fixture if statement must have a following visible line.");
            return nextLine;
        }
    }
}
