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
        WorkerInput input,
        CSharpParseOptions parseOptions,
        CompilationUnitSyntax plainRoot)
    {
        // Syntax-key maps for edited-method detection. Distinct from TransformWorkerProgram.BuildMethodKey (Cecil names):
        // same-file old/new comparison only needs syntax keys to stay consistent with each other.
        BaselineSnapshotState baseline = new BaselineSnapshotState();
        // Null disables comparison; empty string is a real (empty) baseline text.
        if (input.SnapshotSource == null)
        {
            return baseline;
        }

        baseline.SnapshotRoot = CSharpSyntaxTree.ParseText(
                SourceText.From(input.SnapshotSource, Encoding.UTF8),
                parseOptions)
            .GetCompilationUnitRoot();
        Dictionary<string, MethodDeclarationSyntax> snapMethods =
            WorkerSyntaxIndex.BuildSyntaxMethodMapOrNull(baseline.SnapshotRoot);
        // Why plainRoot: annotated current nodes break AreEquivalent for some shapes (see plainRoot above).
        Dictionary<string, MethodDeclarationSyntax> currentMethods = WorkerSyntaxIndex.BuildSyntaxMethodMapOrNull(plainRoot);
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
        baseline.SnapshotPropertyMap = WorkerSyntaxIndex.BuildSyntaxPropertyMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotIndexerMap = WorkerSyntaxIndex.BuildSyntaxIndexerMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotConstructorMap = WorkerSyntaxIndex.BuildSyntaxConstructorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotOperatorMap = WorkerSyntaxIndex.BuildSyntaxOperatorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotEventMap = WorkerSyntaxIndex.BuildSyntaxEventMapOrNull(baseline.SnapshotRoot);
        baseline.PlainCurrentPropertyMap = WorkerSyntaxIndex.BuildSyntaxPropertyMapOrNull(plainRoot);
        baseline.PlainCurrentIndexerMap = WorkerSyntaxIndex.BuildSyntaxIndexerMapOrNull(plainRoot);
        baseline.PlainCurrentConstructorMap = WorkerSyntaxIndex.BuildSyntaxConstructorMapOrNull(plainRoot);
        baseline.PlainCurrentOperatorMap = WorkerSyntaxIndex.BuildSyntaxOperatorMapOrNull(plainRoot);
        baseline.PlainCurrentEventMap = WorkerSyntaxIndex.BuildSyntaxEventMapOrNull(plainRoot);
        return baseline;
    }
}
