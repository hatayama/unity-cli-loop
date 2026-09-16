using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Splits one type declaration into the regions a fingerprint records separately: which nodes are
// its members, which part of a member is its body, and which key names that member.
internal static class IntroducedTypeMemberRegions
{
    internal static IReadOnlyList<MemberDeclarationSyntax> CollectMembers(BaseTypeDeclarationSyntax declaration)
    {
        if (declaration is TypeDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration.Members;
        }

        if (declaration is EnumDeclarationSyntax enumDeclaration)
        {
            return enumDeclaration.Members;
        }

        return new List<MemberDeclarationSyntax>();
    }

    // The part of a member that only implements it. A constructor initializer counts as body:
    // it runs, it is not part of how the member is called.
    internal static IReadOnlyList<SyntaxNode> CollectBodyNodes(MemberDeclarationSyntax member)
    {
        List<SyntaxNode> bodyNodes = new List<SyntaxNode>();
        if (member is MethodDeclarationSyntax method)
        {
            AddNonNull(bodyNodes, method.Body);
            AddNonNull(bodyNodes, method.ExpressionBody);
            return bodyNodes;
        }

        if (member is ConstructorDeclarationSyntax constructor)
        {
            AddNonNull(bodyNodes, constructor.Initializer);
            AddNonNull(bodyNodes, constructor.Body);
            AddNonNull(bodyNodes, constructor.ExpressionBody);
            return bodyNodes;
        }

        if (member is DestructorDeclarationSyntax destructor)
        {
            AddNonNull(bodyNodes, destructor.Body);
            AddNonNull(bodyNodes, destructor.ExpressionBody);
            return bodyNodes;
        }

        if (member is OperatorDeclarationSyntax operatorDeclaration)
        {
            AddNonNull(bodyNodes, operatorDeclaration.Body);
            AddNonNull(bodyNodes, operatorDeclaration.ExpressionBody);
            return bodyNodes;
        }

        if (member is ConversionOperatorDeclarationSyntax conversionDeclaration)
        {
            AddNonNull(bodyNodes, conversionDeclaration.Body);
            AddNonNull(bodyNodes, conversionDeclaration.ExpressionBody);
            return bodyNodes;
        }

        if (member is PropertyDeclarationSyntax property)
        {
            AddNonNull(bodyNodes, property.ExpressionBody);
            AddAccessorBodies(bodyNodes, property.AccessorList);
            return bodyNodes;
        }

        if (member is IndexerDeclarationSyntax indexer)
        {
            AddNonNull(bodyNodes, indexer.ExpressionBody);
            AddAccessorBodies(bodyNodes, indexer.AccessorList);
            return bodyNodes;
        }

        if (member is EventDeclarationSyntax eventDeclaration)
        {
            AddAccessorBodies(bodyNodes, eventDeclaration.AccessorList);
            return bodyNodes;
        }

        // Fields, event fields, enum members, delegates and nested types carry no body: whatever
        // they declare, including an initializer or a constant value, describes the type itself.
        return bodyNodes;
    }

    // The semicolon that closes an expression-bodied member belongs with that body: rewriting
    // `=> value;` as a block changes nothing a caller can see, and leaving the semicolon on the
    // declaration side would report that rewrite as a declaration change.
    internal static SyntaxToken ReadExpressionBodyTerminator(MemberDeclarationSyntax member)
    {
        if (member is MethodDeclarationSyntax method && method.ExpressionBody != null)
        {
            return method.SemicolonToken;
        }

        if (member is PropertyDeclarationSyntax property && property.ExpressionBody != null)
        {
            return property.SemicolonToken;
        }

        if (member is IndexerDeclarationSyntax indexer && indexer.ExpressionBody != null)
        {
            return indexer.SemicolonToken;
        }

        if (member is ConstructorDeclarationSyntax constructor && constructor.ExpressionBody != null)
        {
            return constructor.SemicolonToken;
        }

        if (member is DestructorDeclarationSyntax destructor && destructor.ExpressionBody != null)
        {
            return destructor.SemicolonToken;
        }

        if (member is OperatorDeclarationSyntax operatorDeclaration && operatorDeclaration.ExpressionBody != null)
        {
            return operatorDeclaration.SemicolonToken;
        }

        if (member is ConversionOperatorDeclarationSyntax conversionDeclaration
            && conversionDeclaration.ExpressionBody != null)
        {
            return conversionDeclaration.SemicolonToken;
        }

        return default;
    }

    // Names a member the same way the rest of the worker names it, so a fingerprint difference
    // points at a member key a reader can look up. The index is the fallback for an operator shape
    // the key builders do not cover, and it only has to be stable within one declaration.
    internal static string BuildMemberKey(MemberDeclarationSyntax member, string typeMetadataName, int index)
    {
        if (member is MethodDeclarationSyntax method)
        {
            return WorkerSyntaxIndex.BuildSyntaxMethodKey(typeMetadataName, method);
        }

        if (member is FieldDeclarationSyntax field)
        {
            return "field:" + JoinDeclaratorNames(field.Declaration);
        }

        if (member is PropertyDeclarationSyntax property)
        {
            return WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataName, property);
        }

        if (member is IndexerDeclarationSyntax indexer)
        {
            return WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataName, indexer);
        }

        if (member is ConstructorDeclarationSyntax constructor)
        {
            return WorkerSyntaxIndex.BuildSyntaxConstructorKey(typeMetadataName, constructor);
        }

        if (member is OperatorDeclarationSyntax || member is ConversionOperatorDeclarationSyntax)
        {
            string operatorKey = WorkerSyntaxIndex.TryBuildSyntaxOperatorMemberKey(typeMetadataName, member);
            return operatorKey ?? "operator:" + index.ToString(CultureInfo.InvariantCulture);
        }

        if (member is EventDeclarationSyntax eventDeclaration)
        {
            return WorkerSyntaxIndex.BuildSyntaxEventKey(typeMetadataName, eventDeclaration);
        }

        if (member is EventFieldDeclarationSyntax eventField)
        {
            return "event-field:" + JoinDeclaratorNames(eventField.Declaration);
        }

        if (member is EnumMemberDeclarationSyntax enumMember)
        {
            return "enum:" + enumMember.Identifier.Text;
        }

        return "nested:" + member.Kind().ToString() + ":" + ReadMemberIdentifier(member, index);
    }

    private static string ReadMemberIdentifier(MemberDeclarationSyntax member, int index)
    {
        if (member is BaseTypeDeclarationSyntax nestedType)
        {
            return nestedType.Identifier.Text;
        }

        if (member is DelegateDeclarationSyntax delegateDeclaration)
        {
            return delegateDeclaration.Identifier.Text;
        }

        return index.ToString(CultureInfo.InvariantCulture);
    }

    // One field declaration is one member even when it declares several names, because the key
    // the rest of the worker uses is per declarator and would not name the declaration itself.
    private static string JoinDeclaratorNames(VariableDeclarationSyntax declaration)
    {
        List<string> names = new List<string>();
        foreach (VariableDeclaratorSyntax declarator in declaration.Variables)
        {
            names.Add(declarator.Identifier.Text);
        }

        return string.Join(",", names);
    }

    private static void AddAccessorBodies(List<SyntaxNode> bodyNodes, AccessorListSyntax accessorList)
    {
        if (accessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in accessorList.Accessors)
        {
            AddNonNull(bodyNodes, accessor.Body);
            AddNonNull(bodyNodes, accessor.ExpressionBody);
        }
    }

    private static void AddNonNull(List<SyntaxNode> bodyNodes, SyntaxNode node)
    {
        if (node != null)
        {
            bodyNodes.Add(node);
        }
    }
}
