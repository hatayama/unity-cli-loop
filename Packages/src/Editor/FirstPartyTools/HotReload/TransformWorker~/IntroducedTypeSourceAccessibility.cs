using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// The accessibility an introduced declaration is compiled with. The artifact is an assembly of
// its own, so a type the user left internal, or wrote without an access modifier, has to be public
// there for the compiled assembly to name it. Only that one access token changes: comments,
// attributes, directives and line endings reach the artifact exactly as the user wrote them, and
// the fingerprint keeps describing the declaration as written.
internal static class IntroducedTypeSourceAccessibility
{
    private const string FileModifierText = "file";

    /// <summary>Whether the declaration is visible only inside the file that declares it.</summary>
    /// <remarks>
    /// Why the token text rather than a syntax kind: the compiler bundled with older Editors has no
    /// kind for this modifier, and there the declaration fails to parse before planning.
    /// </remarks>
    internal static bool IsFileLocal(BaseTypeDeclarationSyntax declaration)
    {
        return declaration.Modifiers.Any(modifier => modifier.ValueText == FileModifierText);
    }

    /// <summary>The declaration text with its implicit or internal accessibility made public.</summary>
    internal static string ToArtifactDeclarationText(BaseTypeDeclarationSyntax declaration)
    {
        string original = declaration.ToFullString();
        TextChange? promotion = PromotionChange(declaration);
        if (promotion == null)
        {
            return original;
        }

        int start = promotion.Value.Span.Start - declaration.FullSpan.Start;
        return original.Remove(start, promotion.Value.Span.Length).Insert(start, promotion.Value.NewText);
    }

    /// <summary>
    /// The one change that makes the declaration's implicit or internal accessibility public, at
    /// positions of the tree that holds the declaration, or null when its accessibility stays.
    /// </summary>
    internal static TextChange? PromotionChange(BaseTypeDeclarationSyntax declaration)
    {
        SyntaxTokenList modifiers = declaration.Modifiers;
        // Why private and protected stay: neither is valid on a top-level type, and quietly making
        // the artifact public would load a type the next real compile rejects.
        if (modifiers.Any(SyntaxKind.PublicKeyword)
            || modifiers.Any(SyntaxKind.PrivateKeyword)
            || modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return null;
        }

        SyntaxToken internalModifier = modifiers.FirstOrDefault(modifier => modifier.IsKind(SyntaxKind.InternalKeyword));
        if (internalModifier.IsKind(SyntaxKind.InternalKeyword))
        {
            return new TextChange(internalModifier.Span, "public");
        }

        SyntaxToken firstHeaderToken = modifiers.Count > 0 ? modifiers[0] : DeclarationKeyword(declaration);
        return new TextChange(new TextSpan(firstHeaderToken.SpanStart, 0), "public ");
    }

    private static SyntaxToken DeclarationKeyword(BaseTypeDeclarationSyntax declaration)
    {
        return declaration switch
        {
            EnumDeclarationSyntax enumDeclaration => enumDeclaration.EnumKeyword,
            TypeDeclarationSyntax typeDeclaration => typeDeclaration.Keyword,
            _ => throw new ArgumentException(
                "Unexpected introduced declaration kind: " + declaration.Kind(), nameof(declaration))
        };
    }
}
