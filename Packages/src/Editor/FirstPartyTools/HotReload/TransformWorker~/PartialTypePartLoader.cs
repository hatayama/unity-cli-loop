using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Finds, among the assembly's other source files, the declarations that complete the
/// partial types the run's files declare.
/// </summary>
internal static class PartialTypePartLoader
{
    internal static PartialTypeParts Load(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        IReadOnlyList<WorkerSourceUnit> transformUnits)
    {
        HashSet<string> neededMetadataNames = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> neededSimpleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerSourceUnit unit in transformUnits)
        {
            CollectPartialTypeNames(unit.Root, neededMetadataNames, neededSimpleNames);
        }

        // Why no file is read without a partial type: no other file can add a member to the run's
        // types then, and reading every source of the assembly would slow every such run down.
        if (neededMetadataNames.Count == 0)
        {
            return PartialTypeParts.None;
        }

        // Why before any file is read: an incomplete scan cannot tell a changed part from an
        // unchanged one, so no part on disk can be trusted to match the compiled type.
        if (!input.ChangedSiblingScanComplete)
        {
            return PartialTypeParts.Unverified;
        }

        List<SyntaxTree> bindingOnlyTrees = new List<SyntaxTree>();
        Dictionary<string, string> changedPartPathByTypeMetadataName =
            new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string assemblySourcePath in input.AssemblySourcePaths)
        {
            SyntaxTree bindingOnlyTree = LoadTrustedPartOrNull(
                input,
                parseOptions,
                assemblySourcePath,
                neededMetadataNames,
                neededSimpleNames,
                changedPartPathByTypeMetadataName);
            if (bindingOnlyTree != null)
            {
                bindingOnlyTrees.Add(bindingOnlyTree);
            }
        }

        return new PartialTypeParts(bindingOnlyTrees, changedPartPathByTypeMetadataName, everyPartialTypeUnverified: false);
    }

    // The reduced tree of one file of the assembly when it holds parts of the run's partial types
    // that match the compiled type. When the parts may not match, the types they belong to are
    // recorded as changed instead and no tree is returned.
    private static SyntaxTree LoadTrustedPartOrNull(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        string path,
        HashSet<string> neededMetadataNames,
        HashSet<string> neededSimpleNames,
        Dictionary<string, string> changedPartPathByTypeMetadataName)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || IsRunFile(input, path))
        {
            return null;
        }

        // The same decoding WorkerUsingCollector reads the assembly's other files with.
        string text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!MayDeclareNeededPart(text, neededSimpleNames))
        {
            return null;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, path);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        List<string> declaredMetadataNames = CollectDeclaredNeededTypes(root, neededMetadataNames);
        if (declaredMetadataNames.Count == 0)
        {
            return null;
        }

        // Why a changed or broken part is never bound against: its text may declare members the
        // compiled type does not have, and a body bound to those would be applied to a type that
        // cannot run it.
        if (IsChangedSibling(input, path) || HasErrors(tree))
        {
            string shownPath = DescribePath(input, path);
            foreach (string metadataName in declaredMetadataNames)
            {
                if (!changedPartPathByTypeMetadataName.ContainsKey(metadataName))
                {
                    changedPartPathByTypeMetadataName[metadataName] = shownPath;
                }
            }

            return null;
        }

        CompilationUnitSyntax reduced = (CompilationUnitSyntax)new PartialTypePartReducer(neededMetadataNames).Visit(root);
        return CSharpSyntaxTree.Create(reduced, parseOptions, path, Encoding.UTF8);
    }

    private static void CollectPartialTypeNames(
        CompilationUnitSyntax root,
        HashSet<string> metadataNames,
        HashSet<string> simpleNames)
    {
        foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!PartialTypeParts.IsPartial(declaration))
            {
                continue;
            }

            metadataNames.Add(WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(declaration));
            simpleNames.Add(declaration.Identifier.ValueText);
        }
    }

    private static List<string> CollectDeclaredNeededTypes(CompilationUnitSyntax root, HashSet<string> neededMetadataNames)
    {
        List<string> declared = new List<string>();
        foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!PartialTypeParts.IsPartial(declaration))
            {
                continue;
            }

            string metadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(declaration);
            if (neededMetadataNames.Contains(metadataName) && !declared.Contains(metadataName))
            {
                declared.Add(metadataName);
            }
        }

        return declared;
    }

    // A run file: the path the worker reads, or the real file an edited copy stands for.
    private static bool IsRunFile(WorkerInput input, string path)
    {
        foreach (WorkerSourceInput source in input.Sources)
        {
            if (WorkerUsingCollector.PathsReferToSameSourceFile(path, source.SourcePath))
            {
                return true;
            }

            if (HotReloadSourcePathMatching.EndsWithProjectRelativePath(path, source.ProjectRelativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsChangedSibling(WorkerInput input, string path)
    {
        foreach (string changedSiblingPath in input.ChangedSiblingSourcePaths)
        {
            if (WorkerUsingCollector.PathsReferToSameSourceFile(path, changedSiblingPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasErrors(SyntaxTree tree)
    {
        foreach (Diagnostic diagnostic in tree.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    // Why a text check before parsing: a run with a partial type reads every source of the
    // assembly, and only a file that mentions "partial" and one of the type names can hold a part.
    private static bool MayDeclareNeededPart(string text, HashSet<string> neededSimpleNames)
    {
        if (text.IndexOf("partial", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        foreach (string simpleName in neededSimpleNames)
        {
            if (text.IndexOf(simpleName, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // Project-relative when a run file anchors the root, else the file name.
    private static string DescribePath(WorkerInput input, string path)
    {
        foreach (string assemblySourcePath in input.AssemblySourcePaths)
        {
            foreach (WorkerSourceInput source in input.Sources)
            {
                string relative = HotReloadSourcePathMatching.ToProjectRelativeOrNull(
                    path,
                    assemblySourcePath,
                    source.ProjectRelativePath);
                if (relative != null)
                {
                    return relative;
                }
            }
        }

        return Path.GetFileName(path);
    }
}

/// <summary>Keeps the needed partial declarations of a file and drops everything else that declares something.</summary>
internal sealed class PartialTypePartReducer : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _neededMetadataNames;

    internal PartialTypePartReducer(HashSet<string> neededMetadataNames)
    {
        _neededMetadataNames = neededMetadataNames ?? throw new ArgumentNullException(nameof(neededMetadataNames));
    }

    // Why the global usings and the assembly and module attributes go: the run's global-using tree
    // already carries every global using of the assembly, and an attribute of the assembly would be
    // declared twice.
    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
    {
        CompilationUnitSyntax visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node);
        SyntaxList<UsingDirectiveSyntax> ordinaryUsings = SyntaxFactory.List(
            visited.Usings.Where(usingDirective => !usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)));
        return visited
            .WithUsings(ordinaryUsings)
            .WithAttributeLists(default(SyntaxList<AttributeListSyntax>));
    }

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        return Keep(node);
    }

    public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
    {
        return Keep(node);
    }

    public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        return Keep(node);
    }

    public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        return Keep(node);
    }

    public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        return null;
    }

    public override SyntaxNode VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        return null;
    }

    private SyntaxNode Keep(TypeDeclarationSyntax node)
    {
        // Why the node is returned whole, without descending: a needed type's members,
        // nested types included, are all part of the type the edited bodies bind against.
        if (PartialTypeParts.IsPartial(node)
            && _neededMetadataNames.Contains(WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(node)))
        {
            return node;
        }

        return null;
    }
}
