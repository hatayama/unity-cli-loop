using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class OutsideMethodBodyDriftChecker
{
    internal const string OutsideMethodBodyDriftWarningFormat =
        "Edits outside method bodies in {0} (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up.";

    internal static void AppendOutsideMethodBodyDriftWarningIfNeeded(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot,
        string fileName,
        List<string> declarationDriftWarnings,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        HashSet<string> snapshotKeys = new HashSet<string>(
            addedMethodCatalog.RemovedSyntaxKeys,
            StringComparer.Ordinal);
        foreach (string key in addedFieldCatalog.RemovedSyntaxKeys)
        {
            snapshotKeys.Add(key);
        }

        HashSet<string> currentKeys = new HashSet<string>(
            addedMethodCatalog.AddedSyntaxKeys,
            StringComparer.Ordinal);
        foreach (string key in addedFieldCatalog.AddedSyntaxKeys)
        {
            currentKeys.Add(key);
        }

        StripHandledMemberDeclarationsRewriter stripSnapshot =
            new StripHandledMemberDeclarationsRewriter(
                snapshotKeys,
                Array.Empty<string>(),
                Array.Empty<string>());
        StripHandledMemberDeclarationsRewriter stripCurrent =
            new StripHandledMemberDeclarationsRewriter(
                currentKeys,
                addedMethodCatalog.AddedTypeSyntaxKeys,
                addedMethodCatalog.AddedPropertySyntaxKeys);
        StripMethodBodiesRewriter bodyStripper = new StripMethodBodiesRewriter();
        SyntaxNode strippedSnapshot = bodyStripper.Visit(stripSnapshot.Visit(snapshotRoot));
        SyntaxNode strippedCurrent = bodyStripper.Visit(stripCurrent.Visit(currentRoot));
        if (!SyntaxFactory.AreEquivalent(strippedSnapshot, strippedCurrent, topLevel: false))
        {
            declarationDriftWarnings.Add(
                string.Format(CultureInfo.InvariantCulture, OutsideMethodBodyDriftWarningFormat, fileName));
        }
    }
}
