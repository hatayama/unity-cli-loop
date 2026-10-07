using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Builds the syntax tree that carries the assembly's global using directives into a worker
// compilation. The compilations hold only the edited files, so a type a sibling file imports
// with a global using would otherwise not bind, and an existing method whose signature names
// such a type would be classified as added.
internal static class WorkerGlobalUsingBindingTree
{
    internal const string TreePath = "UloopHotReloadAssemblyGlobalUsings.cs";

    // assemblyGlobalUsings is the list WorkerUsingCollector.CollectAssemblyGlobalUsings returns
    // (global keyword already stripped). Returns null when every directive is already declared
    // as a global using by one of the roots that go into the same compilation, so the tree never
    // repeats a directive the compilation already has.
    internal static SyntaxTree Build(
        IReadOnlyList<UsingDirectiveSyntax> assemblyGlobalUsings,
        IReadOnlyList<CompilationUnitSyntax> rootsInCompilation,
        CSharpParseOptions parseOptions)
    {
        List<UsingDirectiveSyntax> declaredByRoots = CollectRootGlobalUsings(rootsInCompilation);
        List<UsingDirectiveSyntax> remaining = new List<UsingDirectiveSyntax>();
        foreach (UsingDirectiveSyntax assemblyUsing in assemblyGlobalUsings)
        {
            // Why compare the stripped directive as it is: UsingDirectivesMatch ignores the global
            // keyword, so it matches the roots' own global directives directly.
            if (WorkerUsingCollector.ContainsEquivalentUsing(declaredByRoots, assemblyUsing))
            {
                continue;
            }

            remaining.Add(assemblyUsing.WithGlobalKeyword(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)));
        }

        if (remaining.Count == 0)
        {
            return null;
        }

        // Why directives only: a declaration here would collide with the compiled reference
        // (CS0436) and change what the edited files bind to.
        CompilationUnitSyntax unit = SyntaxFactory.CompilationUnit()
            .WithUsings(SyntaxFactory.List(remaining))
            .NormalizeWhitespace();
        return CSharpSyntaxTree.Create(unit, parseOptions, TreePath, Encoding.UTF8);
    }

    // Returns the trees with the global-using tree appended, or the trees unchanged when there is none.
    internal static List<SyntaxTree> Append(IReadOnlyList<SyntaxTree> trees, SyntaxTree globalUsingTree)
    {
        List<SyntaxTree> appended = new List<SyntaxTree>(trees.Count + 1);
        appended.AddRange(trees);
        if (globalUsingTree != null)
        {
            appended.Add(globalUsingTree);
        }

        return appended;
    }

    private static List<UsingDirectiveSyntax> CollectRootGlobalUsings(
        IReadOnlyList<CompilationUnitSyntax> rootsInCompilation)
    {
        List<UsingDirectiveSyntax> declaredByRoots = new List<UsingDirectiveSyntax>();
        foreach (CompilationUnitSyntax root in rootsInCompilation)
        {
            foreach (UsingDirectiveSyntax usingDirective in root.Usings)
            {
                if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                {
                    declaredByRoots.Add(usingDirective);
                }
            }
        }

        return declaredByRoots;
    }
}
