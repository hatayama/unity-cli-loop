using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for which file a type notice marks as declaring a refused type, the mark
    /// that keeps the missing-baseline warning off a file whose notice already asks for a compile.
    /// </summary>
    public class HotReloadIntroducedTypeOutcomeSinkTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string OwnerPath = "Assets/Example.cs";

        /// <summary>
        /// Verifies that a notice about a declaration in its owner file marks that file as
        /// declaring a refused type.
        /// </summary>
        [Test]
        public void AppendNotices_DeclarationNotice_MarksOwnerFile()
        {
            HotReloadGroupFile owner = CreateFile(OwnerPath);

            HotReloadIntroducedTypeOutcomeSink.AppendNotices(
                new[] { owner },
                new[] { new HotReloadIntroducedTypeNotice(OwnerPath, "refused", namesDeclaration: true) });

            Assert.That(owner.DeclaresRefusedIntroducedType, Is.True);
        }

        /// <summary>
        /// Verifies that a run-scoped notice, which the worker attaches to every file it parsed,
        /// does not mark its owner file: that file may declare no type at all, and still needs
        /// its missing-baseline warning.
        /// </summary>
        [Test]
        public void AppendNotices_RunScopedNotice_DoesNotMarkOwnerFile()
        {
            HotReloadGroupFile owner = CreateFile(OwnerPath);

            HotReloadIntroducedTypeOutcomeSink.AppendNotices(
                new[] { owner },
                new[] { new HotReloadIntroducedTypeNotice(OwnerPath, "run scoped", namesDeclaration: false) });

            Assert.That(
                owner.Sinks.Warnings,
                Has.Member(OwnerPath + ": run scoped"),
                "The notice itself must still reach the owner file.");
            Assert.That(owner.DeclaresRefusedIntroducedType, Is.False);
        }

        private static HotReloadGroupFile CreateFile(string projectRelativePath)
        {
            return new HotReloadGroupFile(
                projectRelativePath,
                projectRelativePath,
                projectRelativePath,
                AssemblyName,
                FindCompilationAssembly(),
                HotReloadTypeHome.ScriptAssembliesUnderProject(ProjectRoot, AssemblyName),
                ProjectRoot,
                new HotReloadFileSinks(new List<string>(), null));
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static UnityCompilationAssembly FindCompilationAssembly()
        {
            foreach (UnityCompilationAssembly assembly
                in UnityEditor.Compilation.CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == AssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("The test assembly must be a compilation assembly.");
            return null;
        }
    }
}
