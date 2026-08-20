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

    internal const string OutsideMethodBodyNamedDriftWarningFormat =
        "Edits outside method bodies in {0} ({1}) are not applied by hot reload; run uloop compile to pick them up.";

    internal static void AppendOutsideMethodBodyDriftWarningIfNeeded(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot,
        string fileName,
        List<string> declarationDriftWarnings,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        IReadOnlyCollection<string> kindChangePropertySyntaxKeys,
        IReadOnlyCollection<string> kindChangeEventSyntaxKeys)
    {
        HashSet<string> snapshotHandled = CollectHandledSnapshotKeys(addedMethodCatalog, addedFieldCatalog);
        HashSet<string> currentHandled = CollectHandledCurrentKeys(addedMethodCatalog, addedFieldCatalog);

        StripMethodBodiesRewriter bodyStripper = new StripMethodBodiesRewriter();
        CompilationUnitSyntax snapshotBodiesStripped =
            (CompilationUnitSyntax)bodyStripper.Visit(snapshotRoot);
        CompilationUnitSyntax currentBodiesStripped =
            (CompilationUnitSyntax)bodyStripper.Visit(currentRoot);
        OutsideMethodBodyDeclarationDiff.Result diff = OutsideMethodBodyDeclarationDiff.Diff(
            snapshotBodiesStripped,
            currentBodiesStripped,
            snapshotHandled,
            currentHandled);
        if (diff.DuplicateKeys)
        {
            // Why fail-open: a colliding syntax key makes pairing ambiguous. Suppressing the
            // warning would hide real declaration drift (#2241).
            AppendFileOnlyWarningIfTreesDiffer(
                snapshotRoot,
                currentRoot,
                fileName,
                declarationDriftWarnings,
                snapshotHandled,
                currentHandled,
                addedMethodCatalog.AddedTypeSyntaxKeys,
                addedMethodCatalog.AddedPropertySyntaxKeys);
            return;
        }

        AppendNamedWarningIfNeeded(fileName, declarationDriftWarnings, diff.ChangedLabels);

        HashSet<string> snapshotResidual = new HashSet<string>(snapshotHandled, StringComparer.Ordinal);
        UnionInto(snapshotResidual, diff.PairedSyntaxKeys);
        UnionInto(snapshotResidual, kindChangePropertySyntaxKeys);
        UnionInto(snapshotResidual, kindChangeEventSyntaxKeys);
        HashSet<string> currentResidual = new HashSet<string>(currentHandled, StringComparer.Ordinal);
        UnionInto(currentResidual, diff.PairedSyntaxKeys);
        AppendFileOnlyWarningIfTreesDiffer(
            snapshotRoot,
            currentRoot,
            fileName,
            declarationDriftWarnings,
            snapshotResidual,
            currentResidual,
            addedMethodCatalog.AddedTypeSyntaxKeys,
            addedMethodCatalog.AddedPropertySyntaxKeys);
    }

    private static HashSet<string> CollectHandledSnapshotKeys(
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        HashSet<string> snapshotHandled = new HashSet<string>(
            addedMethodCatalog.RemovedSyntaxKeys,
            StringComparer.Ordinal);
        UnionInto(snapshotHandled, addedFieldCatalog.RemovedSyntaxKeys);
        return snapshotHandled;
    }

    private static HashSet<string> CollectHandledCurrentKeys(
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        HashSet<string> currentHandled = new HashSet<string>(
            addedMethodCatalog.AddedSyntaxKeys,
            StringComparer.Ordinal);
        UnionInto(currentHandled, addedFieldCatalog.AddedSyntaxKeys);
        return currentHandled;
    }

    private static void AppendNamedWarningIfNeeded(
        string fileName,
        List<string> declarationDriftWarnings,
        List<string> changedLabels)
    {
        if (changedLabels.Count == 0)
        {
            return;
        }

        changedLabels.Sort(StringComparer.Ordinal);
        declarationDriftWarnings.Add(
            string.Format(
                CultureInfo.InvariantCulture,
                OutsideMethodBodyNamedDriftWarningFormat,
                fileName,
                string.Join("; ", changedLabels)));
    }

    private static void UnionInto(HashSet<string> target, IEnumerable<string> source)
    {
        foreach (string key in source)
        {
            target.Add(key);
        }
    }

    private static void AppendFileOnlyWarningIfTreesDiffer(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot,
        string fileName,
        List<string> declarationDriftWarnings,
        HashSet<string> snapshotKeys,
        HashSet<string> currentKeys,
        IReadOnlyCollection<string> addedTypeSyntaxKeys,
        IReadOnlyCollection<string> addedPropertySyntaxKeys)
    {
        StripHandledMemberDeclarationsRewriter stripSnapshot =
            new StripHandledMemberDeclarationsRewriter(
                snapshotKeys,
                Array.Empty<string>(),
                Array.Empty<string>());
        StripHandledMemberDeclarationsRewriter stripCurrent =
            new StripHandledMemberDeclarationsRewriter(
                currentKeys,
                addedTypeSyntaxKeys,
                addedPropertySyntaxKeys);
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
