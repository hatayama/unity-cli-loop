using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Builds a Roslyn solution from tracked Unity asmdef files instead of Unity-generated csproj files.
    /// </summary>
    public sealed class AsmdefWorkspaceBuilder
    {
        private static readonly string[] DefaultPreprocessorSymbols =
        {
            "UNITY_EDITOR",
            "UNITY_EDITOR_OSX",
            "UNITY_2022_3_OR_NEWER",
            "UNITY_6000_0_OR_NEWER",
            "ULOOP_HAS_INPUT_SYSTEM"
        };

        public WorkspaceBuildResult Build(string rootPath)
        {
            string editorRoot = Path.Combine(rootPath, "Packages", "src", "Editor");
            IReadOnlyList<AsmdefProjectInfo> asmdefs = LoadAsmdefs(editorRoot);
            Dictionary<string, ProjectId> projectIdsByName = new(StringComparer.Ordinal);
            Dictionary<string, ProjectId> projectIdsByGuid = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<ProjectId, ProjectAnalysisInfo> projectInfoById = new();
            AdhocWorkspace workspace = new(MefHostServices.DefaultHost);
            Solution solution = workspace.CurrentSolution;
            ImmutableArray<MetadataReference> metadataReferences = CreateMetadataReferences();

            foreach (AsmdefProjectInfo asmdef in asmdefs)
            {
                ProjectId projectId = ProjectId.CreateNewId(asmdef.Name);
                projectIdsByName[asmdef.Name] = projectId;
                string guid = ReadAsmdefGuid(asmdef.DirectoryPath, asmdef.Name);
                if (!string.IsNullOrEmpty(guid))
                {
                    projectIdsByGuid[guid] = projectId;
                }

                ProjectInfo projectInfo = ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    asmdef.Name,
                    asmdef.Name,
                    LanguageNames.CSharp,
                    parseOptions: CreateParseOptions(asmdef.PreprocessorSymbols),
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    metadataReferences: metadataReferences);

                solution = solution.AddProject(projectInfo);
                projectInfoById[projectId] = new ProjectAnalysisInfo(projectId, isProduction: true);

                foreach (string sourceFile in asmdef.SourceFiles)
                {
                    DocumentId documentId = DocumentId.CreateNewId(projectId, sourceFile);
                    SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile));
                    solution = solution.AddDocument(documentId, sourceFile, sourceText, filePath: sourceFile);
                }
            }

            solution = AddAsmdefProjectReferences(solution, asmdefs, projectIdsByName, projectIdsByGuid);
            solution = AddAssetsProject(rootPath, solution, projectIdsByName.Values.ToArray(), projectInfoById, metadataReferences);
            return new WorkspaceBuildResult(solution, projectInfoById);
        }

        private static IReadOnlyList<AsmdefProjectInfo> LoadAsmdefs(string editorRoot)
        {
            string[] asmdefPaths = Directory.GetFiles(editorRoot, "*.asmdef", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            List<AsmdefProjectInfo> projects = new();
            HashSet<string> asmdefDirectories = asmdefPaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string asmdefPath in asmdefPaths)
            {
                AsmdefJson json = ReadAsmdefJson(asmdefPath);
                string directoryPath = Path.GetDirectoryName(asmdefPath) ?? editorRoot;
                string[] sourceFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                    .Where(path => IsOwnedByAsmdefDirectory(path, directoryPath, asmdefDirectories))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                string[] preprocessorSymbols = CreatePreprocessorSymbols(json);
                projects.Add(new AsmdefProjectInfo(
                    json.Name,
                    directoryPath,
                    json.References,
                    preprocessorSymbols,
                    sourceFiles));
            }

            return projects;
        }

        private static bool IsOwnedByAsmdefDirectory(
            string sourcePath,
            string currentAsmdefDirectory,
            HashSet<string> asmdefDirectories)
        {
            string? directory = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrEmpty(directory))
            {
                if (asmdefDirectories.Contains(directory))
                {
                    return string.Equals(directory, currentAsmdefDirectory, StringComparison.Ordinal);
                }

                directory = Path.GetDirectoryName(directory);
            }

            return false;
        }

        private static AsmdefJson ReadAsmdefJson(string asmdefPath)
        {
            string json = File.ReadAllText(asmdefPath);
            AsmdefJson? asmdef = JsonSerializer.Deserialize<AsmdefJson>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (asmdef == null || string.IsNullOrEmpty(asmdef.Name))
            {
                throw new InvalidOperationException($"Invalid asmdef file: {asmdefPath}");
            }

            return asmdef;
        }

        private static string[] CreatePreprocessorSymbols(AsmdefJson asmdef)
        {
            List<string> symbols = new(DefaultPreprocessorSymbols);
            symbols.AddRange(asmdef.DefineConstraints.Where(symbol => !string.IsNullOrEmpty(symbol)));
            symbols.AddRange(asmdef.VersionDefines
                .Select(define => define.Define)
                .Where(symbol => !string.IsNullOrEmpty(symbol)));
            return symbols.Distinct(StringComparer.Ordinal).OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray();
        }

        private static CSharpParseOptions CreateParseOptions(string[] symbols)
        {
            return CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(symbols);
        }

        private static Solution AddAsmdefProjectReferences(
            Solution solution,
            IReadOnlyList<AsmdefProjectInfo> asmdefs,
            IReadOnlyDictionary<string, ProjectId> projectIdsByName,
            IReadOnlyDictionary<string, ProjectId> projectIdsByGuid)
        {
            foreach (AsmdefProjectInfo asmdef in asmdefs)
            {
                ProjectId projectId = projectIdsByName[asmdef.Name];
                foreach (string reference in asmdef.References)
                {
                    ProjectId? referencedProjectId = ResolveProjectReference(reference, projectIdsByName, projectIdsByGuid);
                    if (referencedProjectId == null)
                    {
                        continue;
                    }

                    solution = solution.AddProjectReference(projectId, new ProjectReference(referencedProjectId));
                }
            }

            return solution;
        }

        private static ProjectId? ResolveProjectReference(
            string reference,
            IReadOnlyDictionary<string, ProjectId> projectIdsByName,
            IReadOnlyDictionary<string, ProjectId> projectIdsByGuid)
        {
            if (reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                string guid = reference.Substring("GUID:".Length);
                return projectIdsByGuid.TryGetValue(guid, out ProjectId? guidProjectId) ? guidProjectId : null;
            }

            return projectIdsByName.TryGetValue(reference, out ProjectId? namedProjectId) ? namedProjectId : null;
        }

        private static Solution AddAssetsProject(
            string rootPath,
            Solution solution,
            ProjectId[] productionProjectIds,
            Dictionary<ProjectId, ProjectAnalysisInfo> projectInfoById,
            ImmutableArray<MetadataReference> metadataReferences)
        {
            string assetsRoot = Path.Combine(rootPath, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                return solution;
            }

            string[] sourceFiles = Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (sourceFiles.Length == 0)
            {
                return solution;
            }

            ProjectId projectId = ProjectId.CreateNewId("Assets.NonProduction");
            ProjectInfo projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Assets.NonProduction",
                "Assets.NonProduction",
                LanguageNames.CSharp,
                parseOptions: CreateParseOptions(DefaultPreprocessorSymbols),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: metadataReferences);

            solution = solution.AddProject(projectInfo);
            projectInfoById[projectId] = new ProjectAnalysisInfo(projectId, isProduction: false);
            foreach (ProjectId productionProjectId in productionProjectIds)
            {
                solution = solution.AddProjectReference(projectId, new ProjectReference(productionProjectId));
            }

            foreach (string sourceFile in sourceFiles)
            {
                DocumentId documentId = DocumentId.CreateNewId(projectId, sourceFile);
                SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile));
                solution = solution.AddDocument(documentId, sourceFile, sourceText, filePath: sourceFile);
            }

            return solution;
        }

        private static ImmutableArray<MetadataReference> CreateMetadataReferences()
        {
            string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(trustedPlatformAssemblies))
            {
                return ImmutableArray<MetadataReference>.Empty;
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(File.Exists)
                .Distinct(StringComparer.Ordinal)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToImmutableArray();
        }

        private static string ReadAsmdefGuid(string asmdefDirectory, string asmdefName)
        {
            string metaPath = Path.Combine(asmdefDirectory, $"{asmdefName}.asmdef.meta");
            if (!File.Exists(metaPath))
            {
                return string.Empty;
            }

            foreach (string line in File.ReadLines(metaPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return trimmed.Substring("guid:".Length).Trim();
                }
            }

            return string.Empty;
        }

        private sealed class AsmdefJson
        {
            public string Name { get; set; } = string.Empty;
            public string[] References { get; set; } = Array.Empty<string>();
            public string[] DefineConstraints { get; set; } = Array.Empty<string>();
            public AsmdefVersionDefine[] VersionDefines { get; set; } = Array.Empty<AsmdefVersionDefine>();
        }

        private sealed class AsmdefVersionDefine
        {
            public string Define { get; set; } = string.Empty;
        }
    }
}
