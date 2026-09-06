using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Produces the tree the transform binds against by taking the declarations a retained artifact
// already serves out of the source. Leaving them in place would bind every reference to the
// source declaration, and the transform would emit a type that a loaded assembly already holds.
internal static class IntroducedTypeBindingRewriter
{
    // Why blanked in place instead of removed from the syntax tree: emit reports the line range of
    // every shim method from the span of its declaration, so deleting text above a method would
    // move the lines that a shim compile error is attributed to. Overwriting the declaration with
    // spaces and keeping its newlines leaves every surviving node at exactly the offset and line
    // it has in the edited file.
    internal static void RemoveRetainedDeclarations(
        WorkerSourceUnit unit,
        IReadOnlyList<BaseTypeDeclarationSyntax> declarations,
        CSharpParseOptions parseOptions)
    {
        if (declarations.Count == 0)
        {
            return;
        }

        StringBuilder builder = new StringBuilder(unit.SyntaxTree.GetText().ToString());
        foreach (BaseTypeDeclarationSyntax declaration in declarations)
        {
            BlankSpan(builder, declaration.Span);
        }

        (SyntaxTree bindingTree, CompilationUnitSyntax _) = WorkerSourceAnnotator.ParseAndAnnotateSource(
            builder.ToString(),
            parseOptions,
            unit.Input.SourcePath,
            unit.ParseErrors);
        if (bindingTree == null)
        {
            return;
        }

        unit.BindingSyntaxTree = bindingTree;
        unit.BindingRoot = bindingTree.GetCompilationUnitRoot();
    }

    private static void BlankSpan(StringBuilder builder, TextSpan span)
    {
        for (int index = span.Start; index < span.End; index++)
        {
            if (builder[index] == '\n' || builder[index] == '\r')
            {
                continue;
            }

            builder[index] = ' ';
        }
    }
}
