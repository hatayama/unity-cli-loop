using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Produces the tree the transform binds against. The declarations a retained artifact already
// serves unchanged are taken out of the source, and the ones this run keeps to transform their
// bodies get the accessibility their artifact was compiled with. Leaving the first in place would
// bind every reference to the source declaration and emit a type a loaded assembly already holds;
// leaving the second as written would judge every use of an internal one by an accessibility the
// type the domain runs does not have.
internal static class IntroducedTypeBindingRewriter
{
    // Why blanked in place instead of removed from the syntax tree: emit reports the line range of
    // every shim method from the span of its declaration, so deleting text above a method would
    // move the lines that a shim compile error is attributed to. Overwriting the declaration with
    // spaces and keeping its newlines leaves every surviving node at exactly the offset and line
    // it has in the edited file. A promotion rewrites one header token, so it moves columns on that
    // line only and keeps every line, which is all the transform reads positions for.
    internal static void RewriteRetainedDeclarations(
        WorkerSourceUnit unit,
        IReadOnlyList<BaseTypeDeclarationSyntax> removable,
        IReadOnlyList<BaseTypeDeclarationSyntax> kept,
        CSharpParseOptions parseOptions,
        List<string> bindingParseErrors)
    {
        List<TextChange> promotions = CollectPromotions(kept);
        if (removable.Count == 0 && promotions.Count == 0)
        {
            return;
        }

        StringBuilder builder = new StringBuilder(unit.SyntaxTree.GetText().ToString());
        // Why blanking first: it keeps every length, so each promotion still finds its token at the
        // position the loaded tree gives it.
        foreach (BaseTypeDeclarationSyntax declaration in removable)
        {
            BlankSpan(builder, declaration.Span);
        }

        // Why from the end: a promotion changes the length of the text after it, and applying the
        // later ones first leaves the earlier positions valid.
        foreach (TextChange promotion in promotions.OrderByDescending(change => change.Span.Start))
        {
            builder.Remove(promotion.Span.Start, promotion.Span.Length);
            builder.Insert(promotion.Span.Start, promotion.NewText);
        }

        (SyntaxTree bindingTree, CompilationUnitSyntax _) = WorkerSourceAnnotator.ParseAndAnnotateSource(
            builder.ToString(),
            parseOptions,
            unit.Input.SourcePath,
            bindingParseErrors);
        // A preprocessor region that starts outside the declaration and ends inside it leaves the
        // blanked text unparseable. The run stops rather than transforming a file whose binding
        // tree is a guess: the caller advances to revert and compile whenever a run succeeds.
        if (bindingTree == null || bindingParseErrors.Count > 0)
        {
            return;
        }

        unit.BindingSyntaxTree = bindingTree;
        unit.BindingRoot = bindingTree.GetCompilationUnitRoot();
    }

    private static List<TextChange> CollectPromotions(IReadOnlyList<BaseTypeDeclarationSyntax> kept)
    {
        List<TextChange> promotions = new List<TextChange>();
        foreach (BaseTypeDeclarationSyntax declaration in kept)
        {
            TextChange? promotion = IntroducedTypeSourceAccessibility.PromotionChange(declaration);
            if (promotion != null)
            {
                promotions.Add(promotion.Value);
            }
        }

        return promotions;
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
