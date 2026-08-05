using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

using NUnit.Framework;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for portable-PDB document checksums used by hot-reload source snapshots.
    /// </summary>
    public class HotReloadSourceSnapshotTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";

        /// <summary>
        /// What: the portable PDB next to a script assembly carries a per-document checksum that matches the hash of the source file bytes, which the snapshot baseline validation relies on.
        /// </summary>
        [Test]
        public void PortablePdb_DocumentChecksum_MatchesSourceFileBytes()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            string fixtureAbsolutePath = Path.Combine(projectRoot, FixtureProjectRelativePath);

            Assert.That(File.Exists(dllPath), Is.True, "Test assembly DLL must exist at " + dllPath);
            Assert.That(File.Exists(pdbPath), Is.True, "Portable PDB must exist next to the test assembly DLL.");
            Assert.That(File.Exists(fixtureAbsolutePath), Is.True, "E2E fixture source must exist.");

            Document document = FindDocumentForProjectRelativePath(dllPath, pdbPath, FixtureProjectRelativePath);
            Assert.That(document, Is.Not.Null, "PDB must contain a document for " + FixtureProjectRelativePath);
            Assert.That(document.Hash, Is.Not.Null.And.Not.Empty, "Document.Hash must be non-empty for baseline validation.");

            byte[] sourceBytes = File.ReadAllBytes(fixtureAbsolutePath);
            byte[] expectedHash = ComputeDocumentHash(document.HashAlgorithm, sourceBytes);
            Assert.That(
                expectedHash.SequenceEqual(document.Hash),
                Is.True,
                "Document.Hash must equal the hash of the on-disk source bytes (algorithm="
                + document.HashAlgorithm + ").");
        }

        private static Document FindDocumentForProjectRelativePath(
            string dllPath,
            string pdbPath,
            string projectRelativePath)
        {
            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };

            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllStream, readerParameters);
            HashSet<Document> seenDocuments = new HashSet<Document>();

            foreach (TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    MethodDebugInformation debugInformation = method.DebugInformation;
                    if (debugInformation == null || !debugInformation.HasSequencePoints)
                    {
                        continue;
                    }

                    foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
                    {
                        if (sequencePoint.IsHidden || sequencePoint.Document == null)
                        {
                            continue;
                        }

                        Document document = sequencePoint.Document;
                        if (!seenDocuments.Add(document))
                        {
                            continue;
                        }

                        if (PathsReferToSameFile(document.Url, projectRelativePath))
                        {
                            return document;
                        }
                    }
                }
            }

            return null;
        }

        // Same semantics as SourcePausePointPathNormalizer.PathsReferToSameFile; kept local so this
        // gate test does not depend on HotReload production helpers that land in later commits.
        private static bool PathsReferToSameFile(string documentUrl, string projectRelativePath)
        {
            string normalizedDocumentUrl = documentUrl.Replace('\\', '/');
            string normalizedRelativePath = projectRelativePath.Replace('\\', '/');
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(normalizedDocumentUrl, normalizedRelativePath, comparison))
            {
                return true;
            }

            return normalizedDocumentUrl.EndsWith("/" + normalizedRelativePath, comparison);
        }

        private static byte[] ComputeDocumentHash(DocumentHashAlgorithm algorithm, byte[] sourceBytes)
        {
            switch (algorithm)
            {
                case DocumentHashAlgorithm.SHA1:
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(sourceBytes);
                    }
                case DocumentHashAlgorithm.SHA256:
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(sourceBytes);
                    }
                default:
                    Assert.Fail("Unsupported Document.HashAlgorithm: " + algorithm);
                    return null;
            }
        }
    }
}
