using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What kind of member a fingerprint key names, in the only terms the reload decides on: the
// kinds it can add to a type an artifact already serves, and everything else.
internal enum IntroducedTypeMemberKind
{
    Method,
    Field,
    Property,
    Other
}

// One reading of a declaration's members for everything the fingerprint comparison needs to be
// read as a decision: the keys in declared order, what kind of member each key names, the syntax
// method key the emit stages spell an ordinary method with, and the syntax property key of a
// property whose getter is the only accessor with a body. Built in one walk so the keys the four
// views use can never disagree.
internal sealed class IntroducedTypeDeclarationMemberIndex
{
    private static readonly string[] NoKeys = new string[0];

    private readonly IReadOnlyList<string> orderedKeys;

    private readonly IReadOnlyDictionary<string, IntroducedTypeMemberKind> kindsByMemberKey;

    private readonly IReadOnlyDictionary<string, string> syntaxMethodKeysByMemberKey;

    private readonly IReadOnlyDictionary<string, string> syntaxGetterPropertyKeysByMemberKey;

    private IntroducedTypeDeclarationMemberIndex(
        IReadOnlyList<string> orderedKeys,
        IReadOnlyDictionary<string, IntroducedTypeMemberKind> kindsByMemberKey,
        IReadOnlyDictionary<string, string> syntaxMethodKeysByMemberKey,
        IReadOnlyDictionary<string, string> syntaxGetterPropertyKeysByMemberKey)
    {
        this.orderedKeys = orderedKeys;
        this.kindsByMemberKey = kindsByMemberKey;
        this.syntaxMethodKeysByMemberKey = syntaxMethodKeysByMemberKey;
        this.syntaxGetterPropertyKeysByMemberKey = syntaxGetterPropertyKeysByMemberKey;
    }

    /// <summary>The member keys in the order the declaration declares them.</summary>
    internal IReadOnlyList<string> OrderedKeys
    {
        get { return orderedKeys; }
    }

    internal static IntroducedTypeDeclarationMemberIndex Build(
        BaseTypeDeclarationSyntax declaration,
        string typeMetadataName)
    {
        Dictionary<string, IntroducedTypeMemberKind> kinds =
            new Dictionary<string, IntroducedTypeMemberKind>(StringComparer.Ordinal);
        Dictionary<string, string> syntaxMethodKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string> syntaxGetterPropertyKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Why the values get their type name from the syntax and not from typeMetadataName: the
        // emit stages spell a method with the name the declaration itself carries, and an escaped
        // identifier such as `class @Sample` is written "@Sample" there while the symbol the
        // fingerprint was keyed from reports "Sample". A value built from the symbol's name would
        // never be found by the stage looking the key up, which reads an edited body as unchanged.
        if (!(declaration is TypeDeclarationSyntax typeDeclaration))
        {
            return new IntroducedTypeDeclarationMemberIndex(
                NoKeys,
                kinds,
                syntaxMethodKeys,
                syntaxGetterPropertyKeys);
        }

        string syntaxTypeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
        IReadOnlyList<MemberDeclarationSyntax> members = IntroducedTypeMemberRegions.CollectMembers(declaration);
        IReadOnlyList<string> keys = IntroducedTypeFingerprint.CollectOrderedMemberKeys(members, typeMetadataName);
        HashSet<string> mappedSyntaxMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> mappedSyntaxGetterPropertyKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < members.Count; index++)
        {
            MemberDeclarationSyntax member = members[index];
            kinds[keys[index]] = ReadMemberKind(member);
            if (member is MethodDeclarationSyntax methodDeclaration)
            {
                // Two methods spelling the same key do not compile, and the fingerprint numbers
                // the later one instead. Mapping only the first one keeps a changed body of
                // either read as a member the reload cannot patch, which is the refusal such a
                // source has to get anyway.
                string syntaxMethodKey =
                    WorkerSyntaxIndex.BuildSyntaxMethodKey(syntaxTypeMetadataName, methodDeclaration);
                if (mappedSyntaxMethodKeys.Add(syntaxMethodKey))
                {
                    syntaxMethodKeys[keys[index]] = syntaxMethodKey;
                }

                continue;
            }

            if (!(member is PropertyDeclarationSyntax propertyDeclaration)
                || !HasGetterAsTheOnlyBodiedAccessor(propertyDeclaration))
            {
                continue;
            }

            string syntaxPropertyKey =
                WorkerSyntaxIndex.BuildSyntaxPropertyKey(syntaxTypeMetadataName, propertyDeclaration);
            if (mappedSyntaxGetterPropertyKeys.Add(syntaxPropertyKey))
            {
                syntaxGetterPropertyKeys[keys[index]] = syntaxPropertyKey;
            }
        }

        return new IntroducedTypeDeclarationMemberIndex(
            keys,
            kinds,
            syntaxMethodKeys,
            syntaxGetterPropertyKeys);
    }

    // Whether the getter is the only accessor of this property that has a body of its own. The
    // fingerprint hashes every body of a property into one value, so an edit of a property with a
    // setter or init body cannot be told from an edit of the getter - and a setter body is not
    // something the reload can patch. A property that answers true has the one shape whose body
    // difference can only be the getter's.
    private static bool HasGetterAsTheOnlyBodiedAccessor(PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.ExpressionBody != null)
        {
            return true;
        }

        if (propertyDeclaration.AccessorList == null)
        {
            return false;
        }

        bool hasGetterBody = false;
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                return false;
            }

            hasGetterBody = true;
        }

        return hasGetterBody;
    }

    // An indexer is a BasePropertyDeclarationSyntax but not a PropertyDeclarationSyntax, so it
    // falls through to Other, which is the kind that refuses.
    private static IntroducedTypeMemberKind ReadMemberKind(MemberDeclarationSyntax member)
    {
        if (member is MethodDeclarationSyntax)
        {
            return IntroducedTypeMemberKind.Method;
        }

        if (member is FieldDeclarationSyntax)
        {
            return IntroducedTypeMemberKind.Field;
        }

        if (member is PropertyDeclarationSyntax)
        {
            return IntroducedTypeMemberKind.Property;
        }

        return IntroducedTypeMemberKind.Other;
    }

    /// <summary>
    /// What kind of member the key names in the declaration being read. A key the declaration
    /// does not hold is Other, which is the answer that refuses rather than the one that lets an
    /// unrecognised difference through.
    /// </summary>
    internal IntroducedTypeMemberKind FindMemberKind(string memberKey)
    {
        if (kindsByMemberKey.TryGetValue(memberKey, out IntroducedTypeMemberKind kind))
        {
            return kind;
        }

        return IntroducedTypeMemberKind.Other;
    }

    /// <summary>
    /// The syntax method key the emit stages spell the ordinary method with, or null when the key
    /// names a member that is not an ordinary method of this declaration.
    /// </summary>
    internal string FindSyntaxMethodKey(string memberKey)
    {
        if (syntaxMethodKeysByMemberKey.TryGetValue(memberKey, out string syntaxMethodKey))
        {
            return syntaxMethodKey;
        }

        return null;
    }

    /// <summary>
    /// The syntax property key the emit stages spell the property with, or null when the key names
    /// a member that is not a property of this declaration whose getter is its only accessor with
    /// a body. Kept apart from the method keys because the two are spelled in namespaces of their
    /// own, and a key found in the wrong one would name a member the emit stage never looks for.
    /// </summary>
    internal string FindSyntaxGetterPropertyKey(string memberKey)
    {
        if (syntaxGetterPropertyKeysByMemberKey.TryGetValue(memberKey, out string syntaxPropertyKey))
        {
            return syntaxPropertyKey;
        }

        return null;
    }
}
