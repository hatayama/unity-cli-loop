using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for completing the reasons that name compiled types with the files
    /// declaring them, read from the target assembly's debug data.
    /// </summary>
    public class TransformWorkerCompiledTypeFileCompleterTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        // Any other assembly of this project: its PDB carries a different build id.
        private const string OtherAssemblyName = "Assembly-CSharp";
        private const string RegistryTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitRegistry";
        private const string RegistryProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadBindingSplitRegistry.cs";

        /// <summary>
        /// What: a split reason carried as the detail of another reason, as when a member reads an
        /// added property whose own body split, is completed with the declaring file too, so the
        /// composed row renders instead of throwing on a missing value.
        /// </summary>
        [Test]
        public void Complete_SplitReasonAsDetail_CompletesTheDetailWithTheDeclaringFile()
        {
            TransformWorkerReasonDto split = SplitReason();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                skipped = new[]
                {
                    new TransformWorkerSkippedDto
                    {
                        method = "Example.Reader.Read()",
                        reason = new TransformWorkerReasonDto
                        {
                            code = HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                            detail = split
                        }
                    }
                }
            };

            new TransformWorkerCompiledTypeFileCompleter().Complete(
                new TransformWorkerInputDto { targetTypesAssemblyPath = TargetAssemblyPath() },
                output);

            string text = HotReloadWorkerReasonText.Render(output.skipped[0].reason);
            Assert.That(text, Does.Contain("Pass '" + RegistryProjectRelativePath + "'"), text);
        }

        /// <summary>
        /// What: when the PDB beside the target assembly belongs to another build, the reason names
        /// the type alone instead of failing the reload, because the file only sharpens the hint.
        /// </summary>
        [Test]
        public void Complete_PdbOfAnotherBuild_NamesTheTypeInsteadOfThrowing()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompletedTypeFileMismatch_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string dllPath = Path.Combine(directory, TestAssemblyName + ".dll");
                File.Copy(TargetAssemblyPath(), dllPath);
                File.Copy(OtherAssemblyPdbPath(), Path.ChangeExtension(dllPath, ".pdb"));
                TransformWorkerReasonDto split = SplitReason();
                TransformWorkerOutputDto output = new TransformWorkerOutputDto
                {
                    skipped = new[] { new TransformWorkerSkippedDto { method = "Example.Host.Wire()", reason = split } }
                };

                new TransformWorkerCompiledTypeFileCompleter().Complete(
                    new TransformWorkerInputDto { targetTypesAssemblyPath = dllPath },
                    output);

                string text = HotReloadWorkerReasonText.Render(split);
                Assert.That(text, Does.Contain("Pass the file that declares '" + RegistryTypeMetadataName + "'"), text);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static TransformWorkerReasonDto SplitReason()
        {
            return new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                args = new[] { "CS1503", "'Example.Payload'", "'" + RegistryTypeMetadataName + "'" },
                typeMetadataNames = new[] { RegistryTypeMetadataName }
            };
        }

        private static string OtherAssemblyPdbPath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", OtherAssemblyName + ".pdb"));
            Assert.That(File.Exists(path), Is.True, "Other assembly pdb missing: " + path);
            return path;
        }

        private static string TargetAssemblyPath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", TestAssemblyName + ".dll"));
            Assert.That(File.Exists(path), Is.True, "Test assembly dll missing: " + path);
            return path;
        }
    }
}
