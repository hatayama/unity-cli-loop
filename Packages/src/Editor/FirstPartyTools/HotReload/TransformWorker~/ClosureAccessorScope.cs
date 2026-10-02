using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
/// What: the closure bodies of one transplanted method, which are the only places its shim
/// rewrites private accesses into accessors.
/// </summary>
internal sealed class ClosureAccessorScope
{
    private readonly List<SyntaxNode> _closureBodies;

    private ClosureAccessorScope(List<SyntaxNode> closureBodies)
    {
        _closureBodies = closureBodies;
    }

    // Why the same set the decision used: the plan registered exactly these closures' accesses,
    // so rewriting any other access would bind an accessor the plan never checked.
    internal static ClosureAccessorScope Of(SyntaxNode methodBody)
    {
        return new ClosureAccessorScope(MethodTransformDecider.FindClosureBodies(methodBody));
    }

    // A nested closure lies inside its outer closure's body, so the ancestor walk covers it.
    internal bool Contains(SyntaxNode node)
    {
        foreach (SyntaxNode closureBody in _closureBodies)
        {
            if (node.SyntaxTree == closureBody.SyntaxTree
                && closureBody.FullSpan.Contains(node.Span)
                && node.AncestorsAndSelf().Contains(closureBody))
            {
                return true;
            }
        }

        return false;
    }
}
