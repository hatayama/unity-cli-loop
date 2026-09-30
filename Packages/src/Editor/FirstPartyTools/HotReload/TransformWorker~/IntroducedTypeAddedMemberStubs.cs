using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Picks the bodies of a newly introduced declaration that its artifact cannot compile because they
// name members this reload's sources add to a type that already exists, and plans a throwing stub
// for each. The artifact binds against the compiled assembly and the retained artifacts, neither
// of which holds those members; the transform of the same run patches the real body onto the
// artifact instead, exactly as it patches a body edit of a type an earlier reload introduced.
// Only the bodies that patch can replace are stubbed - ordinary methods, and properties whose
// getter is the only accessor with a body - so no other body is ever left throwing.
internal static class IntroducedTypeAddedMemberStubs
{
    private const string StubExceptionTypeName = "global::System.InvalidOperationException";

    internal static IntroducedTypeStubPlan Plan(
        BaseTypeDeclarationSyntax declaration,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        AddedMemberReferenceClassifier classifier,
        string ownerProjectRelativePath)
    {
        IntroducedTypeDeclarationMemberIndex memberIndex = IntroducedTypeDeclarationMemberIndex.Build(
            declaration,
            CecilTypeNames.ToMetadataName(typeSymbol));
        IReadOnlyList<MemberDeclarationSyntax> members = IntroducedTypeMemberRegions.CollectMembers(declaration);
        List<string> fingerprintKeys = new List<string>();
        List<string> methodKeys = new List<string>();
        List<TextChange> bodyChanges = new List<TextChange>();
        for (int index = 0; index < memberIndex.OrderedKeys.Count; index++)
        {
            string memberKey = memberIndex.OrderedKeys[index];
            IMethodSymbol patchedMethod = FindPatchableMethod(members[index], memberKey, memberIndex, semanticModel);
            if (patchedMethod == null)
            {
                continue;
            }

            IReadOnlyList<SyntaxNode> bodyNodes = IntroducedTypeMemberRegions.CollectBodyNodes(members[index]);
            if (classifier.Classify(bodyNodes, semanticModel) != AddedMemberUse.MethodsFieldsOrProperties)
            {
                continue;
            }

            // Two members spelling one entry key, such as a getter and a method named after it, do
            // not compile; the later one keeps its body so the artifact reports that error.
            string methodKey = WorkerMethodKeys.BuildMethodKeyFromSymbol(patchedMethod);
            if (methodKeys.Contains(methodKey))
            {
                continue;
            }

            fingerprintKeys.Add(memberKey);
            methodKeys.Add(methodKey);
            string literal = SyntaxFactory.Literal(
                BuildStubMessage(typeSymbol, patchedMethod, ownerProjectRelativePath)).Text;
            foreach (SyntaxNode bodyNode in bodyNodes)
            {
                bodyChanges.Add(BuildStubChange(bodyNode, literal));
            }
        }

        if (fingerprintKeys.Count == 0)
        {
            return IntroducedTypeStubPlan.None;
        }

        return new IntroducedTypeStubPlan(fingerprintKeys, methodKeys, bodyChanges);
    }

    // The method whose patch replaces the member's body: the method itself, or the getter of a
    // property whose getter is its only accessor with a body. Null for every other member, which
    // is the answer the fingerprint comparison gives when it reads the member's body as one the
    // reload cannot patch.
    private static IMethodSymbol FindPatchableMethod(
        MemberDeclarationSyntax member,
        string memberKey,
        IntroducedTypeDeclarationMemberIndex memberIndex,
        SemanticModel semanticModel)
    {
        if (member is MethodDeclarationSyntax method && memberIndex.FindSyntaxMethodKey(memberKey) != null)
        {
            return semanticModel.GetDeclaredSymbol(method);
        }

        if (member is PropertyDeclarationSyntax property && memberIndex.FindSyntaxGetterPropertyKey(memberKey) != null)
        {
            return semanticModel.GetDeclaredSymbol(property)?.GetMethod;
        }

        return null;
    }

    private static string BuildStubMessage(
        INamedTypeSymbol typeSymbol,
        IMethodSymbol patchedMethod,
        string ownerProjectRelativePath)
    {
        string memberName = patchedMethod.AssociatedSymbol?.Name ?? patchedMethod.Name;
        return "'" + typeSymbol.ToDisplayString() + "." + memberName
            + "' calls members that a hot reload added, so its body runs only while hot reload patches it in. Reload '"
            + ownerProjectRelativePath + "' again, or run 'uloop compile'.";
    }

    // Keeps the form of the body it replaces, so an expression-bodied member keeps the semicolon
    // that closes it and a block keeps the braces the surrounding trivia is laid out around.
    private static TextChange BuildStubChange(SyntaxNode bodyNode, string messageLiteral)
    {
        string throwExpression = "throw new " + StubExceptionTypeName + "(" + messageLiteral + ")";
        if (bodyNode is BlockSyntax block)
        {
            return new TextChange(block.Span, "{ " + throwExpression + "; }");
        }

        if (bodyNode is ArrowExpressionClauseSyntax arrow)
        {
            return new TextChange(arrow.Span, "=> " + throwExpression);
        }

        throw new ArgumentException(
            "Only a block or an expression body can be stubbed: " + bodyNode.Kind(),
            nameof(bodyNode));
    }
}
