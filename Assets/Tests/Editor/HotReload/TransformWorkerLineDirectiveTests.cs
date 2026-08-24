using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies #line directives sit after comments/regions and immediately before the
    /// statement's first token in emitted shim source.
    /// </summary>
    public sealed class TransformWorkerLineDirectiveTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string ProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadLineDirectiveFixture.cs";

        /// <summary>
        /// What: // comments before a local do not consume the statement's #line numbers.
        /// </summary>
        [Test]
        public async Task Emit_LeadingSingleLineComments_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float leadingComments =",
                ProjectRelativePath);
        }

        /// <summary>
        /// What: a /* */ block before a local does not consume the statement's #line numbers.
        /// </summary>
        [Test]
        public async Task Emit_MultiLineComment_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float multiLineComment =",
                ProjectRelativePath);
        }

        /// <summary>
        /// What: an end-of-line comment on the statement itself does not move #line.
        /// </summary>
        [Test]
        public async Task Emit_TrailingSameLineComment_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float trailingSameLine =",
                ProjectRelativePath);
        }

        /// <summary>
        /// What: a method attribute does not shift the body's statement #line onto later text.
        /// </summary>
        [Test]
        public async Task Emit_AttributedMember_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float attributedMember =",
                ProjectRelativePath);
        }

        /// <summary>
        /// What: a statement that spans physical lines maps #line to its first token.
        /// </summary>
        [Test]
        public async Task Emit_MultiLineStatement_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float multiLineStatement =",
                ProjectRelativePath);
        }

        /// <summary>
        /// What: #region trivia stays above #line so the directive precedes the token.
        /// </summary>
        [Test]
        public async Task Emit_RegionWrappedStatement_LineDirectiveSitsImmediatelyBeforeToken()
        {
            string source = ReadFixtureSource();
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync(source);
            AssertLineDirectiveImmediatelyBeforeToken(
                result.Output.shimSource,
                source,
                "float regionWrapped =",
                ProjectRelativePath);
        }

        private static void AssertLineDirectiveImmediatelyBeforeToken(
            string shimSource,
            string originalSource,
            string token,
            string projectRelativePath)
        {
            Assert.That(shimSource, Is.Not.Null.And.Not.Empty);
            int expectedLine = FindLineNumberContaining(originalSource, token);
            Assert.That(expectedLine, Is.GreaterThan(0));

            int tokenIndex = shimSource.IndexOf(token, StringComparison.Ordinal);
            Assert.That(tokenIndex, Is.GreaterThan(0));

            string beforeToken = shimSource.Substring(0, tokenIndex);
            int directiveIndex = beforeToken.LastIndexOf("#line ", StringComparison.Ordinal);
            Assert.That(directiveIndex, Is.GreaterThanOrEqualTo(0));

            string expectedPrefix =
                "#line " + expectedLine + " \"" + projectRelativePath + "\"\n            ";
            string actualPrefix = shimSource.Substring(directiveIndex, tokenIndex - directiveIndex);
            Assert.That(actualPrefix, Is.EqualTo(expectedPrefix));
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnFixtureAsync(string source)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourcePath = Path.Combine(projectRoot, ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ProjectRelativePath,
                snapshotSource: null);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.shimSource, Is.Not.Null.And.Not.Empty);
            return result;
        }

        private static string ReadFixtureSource()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourcePath = Path.Combine(projectRoot, ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(sourcePath);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            List<string> referencePaths = new List<string>();
            if (compilationAssembly.allReferences != null)
            {
                foreach (string reference in compilationAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        referencePaths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!referencePaths.Contains(fullTarget))
            {
                referencePaths.Add(fullTarget);
            }

            List<string> assemblySourcePaths = new List<string>();
            if (compilationAssembly.sourceFiles != null)
            {
                foreach (string sourceFile in compilationAssembly.sourceFiles)
                {
                    assemblySourcePaths.Add(Path.GetFullPath(sourceFile));
                }
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = assemblySourcePaths.ToArray()
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return -1;
        }
    }
}
