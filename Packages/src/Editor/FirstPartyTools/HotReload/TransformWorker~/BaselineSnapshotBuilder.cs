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

internal static class BaselineSnapshotBuilder
{
    internal static BaselineSnapshotState BuildBaselineSnapshotState(
        string snapshotSource,
        CSharpParseOptions parseOptions,
        CompilationUnitSyntax plainRoot)
    {
        // Syntax-key maps for edited-method detection. Distinct from WorkerMethodKeys.BuildMethodKey (Cecil names):
        // same-file old/new comparison only needs syntax keys to stay consistent with each other.
        BaselineSnapshotState baseline = new BaselineSnapshotState();
        // Null disables comparison; empty string is a real (empty) baseline text.
        if (snapshotSource == null)
        {
            return baseline;
        }

        baseline.SnapshotRoot = CSharpSyntaxTree.ParseText(
                SourceText.From(snapshotSource, Encoding.UTF8),
                parseOptions)
            .GetCompilationUnitRoot();
        Dictionary<string, MethodDeclarationSyntax> snapMethods =
            WorkerSyntaxMemberMaps.BuildSyntaxMethodMapOrNull(baseline.SnapshotRoot);
        // Why plainRoot: annotated current nodes break AreEquivalent for some shapes (see plainRoot above).
        Dictionary<string, MethodDeclarationSyntax> currentMethods = WorkerSyntaxMemberMaps.BuildSyntaxMethodMapOrNull(plainRoot);
        if (snapMethods == null || currentMethods == null)
        {
            // Why surface: previously a colliding key silently disabled baseline and patched all.
            baseline.BaselineDisabledByDuplicateKeys = true;
            return baseline;
        }

        // Why both maps: a duplicate key on either side makes AreEquivalent matching
        // ambiguous, so fail closed to no-baseline (patch all) instead of guessing.
        baseline.HasBaseline = true;
        baseline.SnapshotMethodMap = snapMethods;
        baseline.PlainCurrentMethodMap = currentMethods;
        // Why null is kept as-is: a colliding property/indexer key only disables accessor
        // gating for this file; method-level baseline matching still applies.
        baseline.SnapshotPropertyMap = WorkerSyntaxMemberMaps.BuildSyntaxPropertyMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotIndexerMap = WorkerSyntaxMemberMaps.BuildSyntaxIndexerMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotConstructorMap = WorkerSyntaxMemberMaps.BuildSyntaxConstructorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotOperatorMap = WorkerSyntaxMemberMaps.BuildSyntaxOperatorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotEventMap = WorkerSyntaxMemberMaps.BuildSyntaxEventMapOrNull(baseline.SnapshotRoot);
        baseline.PlainCurrentPropertyMap = WorkerSyntaxMemberMaps.BuildSyntaxPropertyMapOrNull(plainRoot);
        baseline.PlainCurrentIndexerMap = WorkerSyntaxMemberMaps.BuildSyntaxIndexerMapOrNull(plainRoot);
        baseline.PlainCurrentConstructorMap = WorkerSyntaxMemberMaps.BuildSyntaxConstructorMapOrNull(plainRoot);
        baseline.PlainCurrentOperatorMap = WorkerSyntaxMemberMaps.BuildSyntaxOperatorMapOrNull(plainRoot);
        baseline.PlainCurrentEventMap = WorkerSyntaxMemberMaps.BuildSyntaxEventMapOrNull(plainRoot);
        return baseline;
    }
}
