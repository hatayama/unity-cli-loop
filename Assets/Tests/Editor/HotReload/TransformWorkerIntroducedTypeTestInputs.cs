using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Builds worker inputs against the same test assembly and Unity compilation references.
    /// </summary>
    internal static class TransformWorkerIntroducedTypeTestInputs
    {
        internal const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        // Builds a planning input for two edited sources. assemblySourcePaths stands in for the
        // other files of the assembly, whose global usings the worker collects; null means none.
        internal static TransformWorkerInputDto CreateInput(
            string firstSourcePath,
            string secondSourcePath,
            string[] assemblySourcePaths = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly DLL must exist.");
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = firstSourcePath,
                        projectRelativePath = "Assets/First.cs"
                    },
                    new TransformWorkerSourceDto
                    {
                        sourcePath = secondSourcePath,
                        projectRelativePath = "Assets/Second.cs"
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                targetAssemblyName = TestAssemblyName,
                targetAssemblyMvid = typeof(TransformWorkerIntroducedTypeTests).Assembly.ManifestModule.ModuleVersionId.ToString(),
                assemblySourcePaths = assemblySourcePaths ?? Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>()
            };
        }

        internal static TransformWorkerInputDto CreatePreparationInput(
            string sourcePath,
            string targetAssemblyPath,
            string targetAssemblyName,
            string targetAssemblyMvid,
            string[] extraReferencePaths,
            string[] changedSiblingSourcePaths)
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            List<string> referencePaths = new List<string>(
                BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetAssemblyPath));
            foreach (string extraReferencePath in extraReferencePaths)
            {
                referencePaths.Add(Path.GetFullPath(extraReferencePath));
            }

            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = "Assets/Edited.cs"
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = targetAssemblyPath,
                targetAssemblyName = targetAssemblyName,
                targetAssemblyMvid = targetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = changedSiblingSourcePaths
            };
        }

        internal static TransformWorkerInputDto CreateConstDriftInput(
            string sourcePath,
            string targetAssemblyPath,
            string targetAssemblyMvid)
        {
            return CreatePreparationInput(
                sourcePath,
                targetAssemblyPath,
                "ConstDriftTarget",
                targetAssemblyMvid,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        internal static TransformWorkerInputDto CreateConstDriftInputWithSiblings(
            string sourcePath,
            string targetAssemblyPath,
            string targetAssemblyMvid,
            string[] changedSiblingSourcePaths)
        {
            return CreatePreparationInput(
                sourcePath,
                targetAssemblyPath,
                "ConstDriftTarget",
                targetAssemblyMvid,
                Array.Empty<string>(),
                changedSiblingSourcePaths);
        }

        internal static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }

        internal static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            foreach (string reference in allReferences)
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string targetPath = Path.GetFullPath(targetDllPath);
            if (!paths.Contains(targetPath))
            {
                paths.Add(targetPath);
            }

            return paths.ToArray();
        }

        internal static string CreateSourceDirectory(string name)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, "Library", "UloopHotReload", "TestSources", "IntroducedTypes", name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static string CreateAssemblyWithType(
            string path,
            string assemblyName,
            string typeNamespace,
            string typeName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition type = new TypeDefinition(
                    typeNamespace,
                    typeName,
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                assembly.MainModule.Types.Add(type);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

        internal static List<HotReloadIntroducedTypeDescriptor> CreateDescriptors(
            TransformWorkerFileOutputDto[] files)
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors = new List<HotReloadIntroducedTypeDescriptor>();
            foreach (TransformWorkerFileOutputDto file in files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    descriptors.Add(
                        new HotReloadIntroducedTypeDescriptor(
                            introducedType.originalAssemblyName,
                            introducedType.originalAssemblyMvid,
                            introducedType.metadataName,
                            introducedType.ownerProjectRelativePath,
                            introducedType.declarationFingerprint,
                            introducedType.source));
                }
            }

            return descriptors;
        }
    }
}
