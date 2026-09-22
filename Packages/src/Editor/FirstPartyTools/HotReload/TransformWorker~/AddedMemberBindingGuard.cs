using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Finds the added member bodies the worker's compilation could not bind, whose accessibility
/// the scanner therefore cannot judge.
/// </summary>
/// <remarks>
/// Why a separate check rather than a stricter scanner: the scanner reads the symbol each access
/// binds to, and an access that bound to nothing looks to it like no access at all. When another
/// file of the run declares from source a type the compiled assembly also holds, a compiled API
/// expects the compiled type, overload resolution fails, and a call to a private method vanishes
/// from the scan. The added member would then be emitted as an ordinary shim method and throw a
/// MethodAccessException on its first call while the run reported success.
/// </remarks>
internal static class AddedMemberBindingGuard
{
    /// <summary>
    /// The first error the compilation reports inside <paramref name="body"/>, or null when the
    /// body bound. Warnings are ignored: the source/metadata type conflict itself is one.
    /// </summary>
    internal static Diagnostic FindFirstBindingError(SemanticModel semanticModel, SyntaxNode body)
    {
        foreach (Diagnostic diagnostic in semanticModel.GetDiagnostics(body.Span))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return diagnostic;
            }
        }

        return null;
    }

    /// <summary>
    /// The spans of every error the compilation reports inside <paramref name="body"/>.
    /// </summary>
    internal static List<TextSpan> FindBindingErrorSpans(SemanticModel semanticModel, SyntaxNode body)
    {
        List<TextSpan> spans = new List<TextSpan>();
        foreach (Diagnostic diagnostic in semanticModel.GetDiagnostics(body.Span))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                spans.Add(diagnostic.Location.SourceSpan);
            }
        }

        return spans;
    }
}
