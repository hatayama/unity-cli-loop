using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies that bundled Roslyn support plugins stay private to Unity CLI Loop.
    /// </summary>
    public sealed class CodeAnalysisPluginImporterTests
    {
        private const string PackageRootPath = "Packages/io.github.hatayama.uloopmcp";
        private const string CodeAnalysisPluginRootPath =
            PackageRootPath + "/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis";
        private const int ComImageFlagsStrongNameSigned = 0x8;
        private const int CliHeaderDataDirectoryIndex = 14;

        private sealed class PrivateAssembly
        {
            public PrivateAssembly(string fileName, string assemblyName, string[] assemblyReferences)
            {
                FileName = fileName;
                AssemblyName = assemblyName;
                AssemblyReferences = assemblyReferences;
            }

            public string FileName { get; }
            public string AssemblyName { get; }
            public string[] AssemblyReferences { get; }
            public string AssetPath => CodeAnalysisPluginRootPath + "/" + FileName;
            public string MetaPath => AssetPath + ".meta";
        }

        private static readonly PrivateAssembly[] PrivateAssemblies =
        {
            new PrivateAssembly(
                "UnityCliLoop.System.Collections.Immutable.dll",
                "UnityCliLoop.System.Collections.Immutable",
                new[] { "UnityCliLoop.System.Runtime.CompilerServices.Unsafe" }),
            new PrivateAssembly(
                "UnityCliLoop.System.Reflection.Metadata.dll",
                "UnityCliLoop.System.Reflection.Metadata",
                new[]
                {
                    "UnityCliLoop.System.Collections.Immutable",
                    "UnityCliLoop.System.Runtime.CompilerServices.Unsafe"
                }),
            new PrivateAssembly(
                "UnityCliLoop.System.Runtime.CompilerServices.Unsafe.dll",
                "UnityCliLoop.System.Runtime.CompilerServices.Unsafe",
                Array.Empty<string>())
        };

        private static readonly string[] OldCodeAnalysisPluginPaths =
        {
            CodeAnalysisPluginRootPath + "/System.Collections.Immutable.dll",
            CodeAnalysisPluginRootPath + "/System.Reflection.Metadata.dll",
            CodeAnalysisPluginRootPath + "/System.Runtime.CompilerServices.Unsafe.dll"
        };

        [Test]
        public void CodeAnalysisPlugins_WhenLoaded_AreEditorOnly()
        {
            // Tests that Roslyn support plugins cannot leak into player assembly resolution.
            for (int assemblyIndex = 0; assemblyIndex < PrivateAssemblies.Length; assemblyIndex++)
            {
                PrivateAssembly assembly = PrivateAssemblies[assemblyIndex];
                PluginImporter importer = AssetImporter.GetAtPath(assembly.AssetPath) as PluginImporter;

                Assert.That(importer, Is.Not.Null, assembly.AssetPath);
                Assert.That(importer!.GetCompatibleWithAnyPlatform(), Is.False, assembly.AssetPath);
                Assert.That(importer.GetCompatibleWithEditor(), Is.True, assembly.AssetPath);
            }
        }

        [Test]
        public void CodeAnalysisPlugins_WhenLoaded_AreExplicitlyReferenced()
        {
            // Tests that Unity does not add bundled dependency assemblies to implicit project references.
            for (int assemblyIndex = 0; assemblyIndex < PrivateAssemblies.Length; assemblyIndex++)
            {
                PrivateAssembly assembly = PrivateAssemblies[assemblyIndex];
                string metaText = File.ReadAllText(assembly.MetaPath);

                Assert.That(metaText, Does.Contain("isExplicitlyReferenced: 1"), assembly.MetaPath);
            }
        }

        [Test]
        public void CodeAnalysisPlugins_WhenInspected_UsePrivateAssemblyIdentities()
        {
            // Tests that private DLL file names also match the managed assembly identities and references.
            for (int assemblyIndex = 0; assemblyIndex < PrivateAssemblies.Length; assemblyIndex++)
            {
                PrivateAssembly assembly = PrivateAssemblies[assemblyIndex];
                byte[] dllBytes = File.ReadAllBytes(assembly.AssetPath);

                Assert.That(ContainsAsciiText(dllBytes, assembly.AssemblyName), Is.True, assembly.AssetPath);
                for (int referenceIndex = 0; referenceIndex < assembly.AssemblyReferences.Length; referenceIndex++)
                {
                    string assemblyReference = assembly.AssemblyReferences[referenceIndex];

                    Assert.That(ContainsAsciiText(dllBytes, assemblyReference), Is.True, assembly.AssetPath);
                }
            }
        }

        [Test]
        public void CodeAnalysisPlugins_WhenInspected_DoNotClaimStrongNameSigning()
        {
            // Tests that rewritten unsigned dependency DLLs do not keep a stale StrongNameSigned flag.
            for (int assemblyIndex = 0; assemblyIndex < PrivateAssemblies.Length; assemblyIndex++)
            {
                PrivateAssembly assembly = PrivateAssemblies[assemblyIndex];
                byte[] dllBytes = File.ReadAllBytes(assembly.AssetPath);

                Assert.That(ReadCorHeaderFlags(dllBytes) & ComImageFlagsStrongNameSigned, Is.Zero, assembly.AssetPath);
            }
        }

        [Test]
        public void CodeAnalysisPlugins_WhenLoaded_DoNotKeepOldSystemNamedAssets()
        {
            // Tests that Unity cannot resolve the old public System.* plugin asset names from this package.
            for (int pathIndex = 0; pathIndex < OldCodeAnalysisPluginPaths.Length; pathIndex++)
            {
                string oldPath = OldCodeAnalysisPluginPaths[pathIndex];

                Assert.That(AssetImporter.GetAtPath(oldPath), Is.Null, oldPath);
                Assert.That(File.Exists(oldPath), Is.False, oldPath);
                Assert.That(File.Exists(oldPath + ".meta"), Is.False, oldPath + ".meta");
            }
        }

        private static bool ContainsAsciiText(byte[] bytes, string expectedText)
        {
            byte[] expectedBytes = Encoding.ASCII.GetBytes(expectedText);
            for (int byteIndex = 0; byteIndex <= bytes.Length - expectedBytes.Length; byteIndex++)
            {
                if (ContainsBytesAt(bytes, expectedBytes, byteIndex))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsBytesAt(byte[] bytes, byte[] expectedBytes, int byteIndex)
        {
            for (int expectedIndex = 0; expectedIndex < expectedBytes.Length; expectedIndex++)
            {
                if (bytes[byteIndex + expectedIndex] != expectedBytes[expectedIndex])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadCorHeaderFlags(byte[] dllBytes)
        {
            int peSignatureOffset = ReadInt32(dllBytes, 0x3c);
            int coffHeaderOffset = peSignatureOffset + 4;
            int numberOfSections = ReadInt16(dllBytes, coffHeaderOffset + 2);
            int sizeOfOptionalHeader = ReadInt16(dllBytes, coffHeaderOffset + 16);
            int optionalHeaderOffset = coffHeaderOffset + 20;
            bool isPe32Plus = ReadInt16(dllBytes, optionalHeaderOffset) == 0x20b;
            int dataDirectoriesOffset = optionalHeaderOffset + (isPe32Plus ? 112 : 96);
            int cliHeaderRva = ReadInt32(dllBytes, dataDirectoriesOffset + CliHeaderDataDirectoryIndex * 8);
            int sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;

            for (int sectionIndex = 0; sectionIndex < numberOfSections; sectionIndex++)
            {
                int sectionOffset = sectionTableOffset + sectionIndex * 40;
                int virtualSize = ReadInt32(dllBytes, sectionOffset + 8);
                int virtualAddress = ReadInt32(dllBytes, sectionOffset + 12);
                int pointerToRawData = ReadInt32(dllBytes, sectionOffset + 20);
                if (cliHeaderRva < virtualAddress || cliHeaderRva >= virtualAddress + virtualSize)
                {
                    continue;
                }

                int cliHeaderOffset = cliHeaderRva - virtualAddress + pointerToRawData;
                return ReadInt32(dllBytes, cliHeaderOffset + 16);
            }

            Assert.Fail("CLI header RVA does not fall inside any PE section.");
            return 0;
        }

        private static int ReadInt16(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt16(bytes, offset);
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return BitConverter.ToInt32(bytes, offset);
        }
    }
}
