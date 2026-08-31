using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class SiblingConstDriftCollector
{
    /// <summary>
    /// Parses each changed sibling source and reuses ConstDriftCollector against the compiled
    /// target-types assembly. The edited file is already scanned on its in-memory tree; siblings
    /// are the files TransformWorker's single-file compilation cannot see.
    /// </summary>
    internal static List<string> CollectConstDriftWarnings(
        string[] changedSiblingSourcePaths,
        CSharpParseOptions parseOptions,
        IReadOnlyList<MetadataReference> references,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        List<string> warnings = new List<string>();
        if (changedSiblingSourcePaths == null
            || changedSiblingSourcePaths.Length == 0
            || targetTypesAssemblySymbol == null)
        {
            return warnings;
        }

        for (int index = 0; index < changedSiblingSourcePaths.Length; index++)
        {
            string siblingPath = changedSiblingSourcePaths[index];
            if (string.IsNullOrEmpty(siblingPath) || !File.Exists(siblingPath))
            {
                continue;
            }

            string text = File.ReadAllText(
                siblingPath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                SourceText.From(text, Encoding.UTF8),
                parseOptions,
                path: siblingPath);
            CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();
            CSharpCompilation siblingCompilation = CSharpCompilation.Create(
                assemblyName: "UloopHotReloadSiblingConstDriftCompilation",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            SemanticModel semanticModel = siblingCompilation.GetSemanticModel(
                syntaxTree,
                ignoreAccessibility: true);
            warnings.AddRange(
                ConstDriftCollector.CollectConstDriftWarnings(
                    root,
                    semanticModel,
                    targetTypesAssemblySymbol));
        }

        return warnings;
    }
}
