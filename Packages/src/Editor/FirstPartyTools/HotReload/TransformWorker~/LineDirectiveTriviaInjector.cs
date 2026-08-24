using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class LineDirectiveTriviaInjector
{
    public static SyntaxNode Attach(SyntaxNode rewritten, string directiveText)
    {
        Debug.Assert(rewritten != null, "rewritten must not be null.");
        Debug.Assert(!string.IsNullOrEmpty(directiveText), "directiveText must not be empty.");

        SyntaxTriviaList directive = SyntaxFactory.ParseLeadingTrivia(directiveText);
        if (rewritten is MethodDeclarationSyntax method && method.AttributeLists.Count > 0)
        {
            return AttachAfterAttributes(method, directive);
        }

        (SyntaxTriviaList before, SyntaxTriviaList after) = SplitLeadingTrivia(rewritten.GetLeadingTrivia());
        return rewritten.WithLeadingTrivia(before.AddRange(directive).AddRange(after));
    }

    private static MethodDeclarationSyntax AttachAfterAttributes(
        MethodDeclarationSyntax method,
        SyntaxTriviaList directive)
    {
        // Why after attributes: AttributeLists are syntax, not trivia. A leading #line on the
        // method would assign the attribute lines the member's mapped number and shift the body.
        SyntaxToken firstAfterAttributes = method.Modifiers.Count > 0
            ? method.Modifiers[0]
            : method.ReturnType.GetFirstToken();
        (SyntaxTriviaList before, SyntaxTriviaList after) =
            SplitLeadingTrivia(firstAfterAttributes.LeadingTrivia);
        SyntaxToken updated = firstAfterAttributes.WithLeadingTrivia(
            before.AddRange(directive).AddRange(after));
        return method.ReplaceToken(firstAfterAttributes, updated);
    }

    private static (SyntaxTriviaList Before, SyntaxTriviaList After) SplitLeadingTrivia(
        SyntaxTriviaList leading)
    {
        // Why the final indent only: comments, #region, and blank-line EOLs after them
        // must stay above #line. Taking a single post-comment EOL left leftover blank
        // lines under the directive, which consume the statement's mapped numbers.
        int indentStart = leading.Count;
        for (int index = leading.Count - 1; index >= 0; index--)
        {
            if (!leading[index].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                break;
            }

            indentStart = index;
        }

        return (Slice(leading, 0, indentStart), Slice(leading, indentStart, leading.Count));
    }

    private static SyntaxTriviaList Slice(SyntaxTriviaList leading, int start, int endExclusive)
    {
        List<SyntaxTrivia> slice = new List<SyntaxTrivia>();
        for (int index = start; index < endExclusive; index++)
        {
            slice.Add(leading[index]);
        }

        return SyntaxFactory.TriviaList(slice);
    }
}
