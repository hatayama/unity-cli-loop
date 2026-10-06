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
    // runUnits is every source of the run, transformUnits the ones the transform reads.
    internal static PartialTypeParts Load(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        IReadOnlyList<WorkerSourceUnit> runUnits,
        IReadOnlyList<WorkerSourceUnit> transformUnits)
    {
        NeededPartialTypes neededTypes = new NeededPartialTypes();
        foreach (WorkerSourceUnit unit in transformUnits)
        {
            neededTypes.CollectFrom(unit.Root);
        }

        // Why no file is read without a partial type: no other file can add a member to the run's
        // types then, and reading every source of the assembly would slow every such run down.
        if (neededTypes.MetadataNames.Count == 0)
        {
            return PartialTypeParts.None;
        }

        // Why before any file is read: an incomplete scan cannot tell a changed part from an
        // unchanged one, so no part on disk can be trusted to match the compiled type.
        if (!input.ChangedSiblingScanComplete)
        {
            return PartialTypeParts.Unverified;
        }

        Dictionary<string, string> changedPartPathByTypeMetadataName =
            new Dictionary<string, string>(StringComparer.Ordinal);
        RecordRunFilesLeftOutOfTheTransform(runUnits, transformUnits, neededTypes, changedPartPathByTypeMetadataName);

        List<SyntaxTree> bindingOnlyTrees = new List<SyntaxTree>();
        foreach (string assemblySourcePath in input.AssemblySourcePaths)
        {
            SyntaxTree bindingOnlyTree = LoadTrustedPartOrNull(
                input,
                parseOptions,
                assemblySourcePath,
                neededTypes,
                changedPartPathByTypeMetadataName);
            if (bindingOnlyTree != null)
            {
                bindingOnlyTrees.Add(bindingOnlyTree);
            }
        }

        return new PartialTypeParts(bindingOnlyTrees, changedPartPathByTypeMetadataName, everyPartialTypeUnverified: false);
    }

    // Why a run file the transform dropped is recorded: it does not reach the binding either, so
    // without this the run's other files would bind against its types without its part.
    private static void RecordRunFilesLeftOutOfTheTransform(
        IReadOnlyList<WorkerSourceUnit> runUnits,
        IReadOnlyList<WorkerSourceUnit> transformUnits,
        NeededPartialTypes neededTypes,
        Dictionary<string, string> changedPartPathByTypeMetadataName)
    {
        foreach (WorkerSourceUnit unit in runUnits)
        {
            if (transformUnits.Contains(unit))
            {
                continue;
            }

            // Why every type when the file could not be read: nothing tells which types it holds
            // parts of. A file that was read has syntax errors, so only the names in its text count.
            IEnumerable<string> untrustedMetadataNames = neededTypes.MetadataNames;
            if (unit.SyntaxTree != null)
            {
                untrustedMetadataNames = neededTypes.MentionedIn(unit.SyntaxTree.GetText().ToString());
            }

            RecordUntrusted(untrustedMetadataNames, unit.Input.ProjectRelativePath, changedPartPathByTypeMetadataName);
        }
    }

    // The reduced tree of one file of the assembly when it holds parts of the run's partial types
    // that match the compiled type. When the parts may not match, the types they belong to are
    // recorded as changed instead and no tree is returned.
    private static SyntaxTree LoadTrustedPartOrNull(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        string path,
        NeededPartialTypes neededTypes,
        Dictionary<string, string> changedPartPathByTypeMetadataName)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || IsRunFile(input, path))
        {
            return null;
        }

        // The same decoding WorkerUsingCollector reads the assembly's other files with.
        string text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        bool isChangedSibling = IsChangedSibling(input, path);
        if (!MayDeclareNeededPart(text, neededTypes, isChangedSibling))
        {
            return null;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, path);

        // Why the names in the text and not the declarations in the tree: a tree with syntax errors
        // can drop a declaration or nest it under the wrong type, so no type its text names is trusted.
        if (HasErrors(tree))
        {
            RecordUntrusted(neededTypes.MentionedIn(text), DescribePath(input, path), changedPartPathByTypeMetadataName);
            return null;
        }

        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        List<string> declaredMetadataNames = CollectDeclaredNeededTypes(root, neededTypes.MetadataNames);
        if (declaredMetadataNames.Count == 0)
        {
            return null;
        }

        // Why a changed part is never bound against: its text may declare members the compiled type
        // does not have, and a body bound to those would be applied to a type that cannot run it.
        if (isChangedSibling)
        {
            RecordUntrusted(declaredMetadataNames, DescribePath(input, path), changedPartPathByTypeMetadataName);
            return null;
        }

        CompilationUnitSyntax reduced =
            (CompilationUnitSyntax)new PartialTypePartReducer(neededTypes.MetadataNames).Visit(root);
        return CSharpSyntaxTree.Create(reduced, parseOptions, path, Encoding.UTF8);
    }

    // The first file recorded for a type is the one its skip reason names.
    private static void RecordUntrusted(
        IEnumerable<string> metadataNames,
        string shownPath,
        Dictionary<string, string> changedPartPathByTypeMetadataName)
    {
        foreach (string metadataName in metadataNames)
        {
            if (!changedPartPathByTypeMetadataName.ContainsKey(metadataName))
            {
                changedPartPathByTypeMetadataName[metadataName] = shownPath;
            }
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
    // assembly, and only a file that mentions one of the type names can hold a part. An unchanged
    // file also has to say "partial"; a changed one need not, because a misspelled "partial" is a
    // syntax error that still has to be recorded.
    private static bool MayDeclareNeededPart(string text, NeededPartialTypes neededTypes, bool isChangedSibling)
    {
        if (!isChangedSibling && text.IndexOf("partial", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        return neededTypes.MentionedIn(text).Count > 0;
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

/// <summary>
/// The partial types the run's files declare, by metadata name and by the simple name source
/// spells them with, which is all a file with syntax errors can be checked for.
/// </summary>
internal sealed class NeededPartialTypes
{
    private readonly Dictionary<string, string> _simpleNameByMetadataName =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal HashSet<string> MetadataNames { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal void CollectFrom(CompilationUnitSyntax root)
    {
        foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!PartialTypeParts.IsPartial(declaration))
            {
                continue;
            }

            string metadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(declaration);
            if (MetadataNames.Add(metadataName))
            {
                _simpleNameByMetadataName[metadataName] = declaration.Identifier.ValueText;
            }
        }
    }

    // Why any occurrence of the name counts, inside a longer word too: a looser match only makes
    // more types untrusted, which is the safe side when the declarations cannot be relied on.
    internal List<string> MentionedIn(string text)
    {
        List<string> mentioned = new List<string>();
        foreach (KeyValuePair<string, string> simpleNameByMetadataName in _simpleNameByMetadataName)
        {
            if (text.IndexOf(simpleNameByMetadataName.Value, StringComparison.Ordinal) >= 0)
            {
                mentioned.Add(simpleNameByMetadataName.Key);
            }
        }

        return mentioned;
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
