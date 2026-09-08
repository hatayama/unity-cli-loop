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

/// <summary>
/// What: file-wide catalog of added fields, syntax keys for drift strip, store-rewrite
/// presence, and display names of fields rewritten in emitted shim bodies.
/// </summary>
internal sealed class AddedFieldCatalog : AddedMemberCatalog<AddedFieldBinding>
{
    private readonly HashSet<string> _rewrittenAddedFieldKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _foldedConstKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    public bool HasStoreRewrites { get; private set; }

    protected override string KeyOf(AddedFieldBinding binding)
    {
        return binding.FieldKey;
    }

    public void AddAddedSyntaxKey(string syntaxKey)
    {
        _addedSyntaxKeys.Add(syntaxKey);
    }

    public void AddRemovedSyntaxKey(string syntaxKey)
    {
        _removedSyntaxKeys.Add(syntaxKey);
    }

    // Why rewritten keys, not Register: registration fires at declaration
    // classification, so unused fields and isolation-excluded bodies would still list.
    // Excluded methods are dropped in TypeEmitPlanner.QueueTypeMethods before rewrite, so a file-wide
    // rewrite set matches emitted entries without per-entry tracking.
    public string[] ListRewrittenAddedFieldDisplayNames(string projectRelativePath)
    {
        return ListDisplayNamesOfFile(_rewrittenAddedFieldKeys, projectRelativePath);
    }

    public string[] ListFoldedConstDisplayNames(string projectRelativePath)
    {
        return ListDisplayNamesOfFile(_foldedConstKeys, projectRelativePath);
    }

    // Why filter by the binding's file: the catalog spans a whole group, while addedFieldNames
    // and addedConstNames are per-file rows in the worker output.
    private string[] ListDisplayNamesOfFile(HashSet<string> fieldKeys, string projectRelativePath)
    {
        List<string> names = new List<string>(fieldKeys.Count);
        foreach (string fieldKey in fieldKeys)
        {
            AddedFieldBinding binding = GetRegistered(fieldKey);
            if (!string.Equals(binding.SourceProjectRelativePath, projectRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            names.Add(FormatAddedFieldDisplayName(fieldKey));
        }

        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }

    // Why this shape: method labels replace '/' with '+' then join with '.', so field
    // names stay comparable to Methods[].Method (Ns.Type.field).
    private static string FormatAddedFieldDisplayName(string fieldKey)
    {
        int separatorIndex = fieldKey.IndexOf(
            TransformWorkerProgramMarker.AddedFieldKeySeparator,
            StringComparison.Ordinal);
        Debug.Assert(
            separatorIndex >= 0,
            "fieldKey is always built with AddedFieldClassifier.FormatAddedFieldStoreKey / WorkerSyntaxIndex.BuildSyntaxFieldKey.");

        string typeMetadataName = fieldKey.Substring(0, separatorIndex).Replace('/', '+');
        string fieldName = fieldKey.Substring(
            separatorIndex + TransformWorkerProgramMarker.AddedFieldKeySeparator.Length);
        return typeMetadataName + "." + fieldName;
    }

    public void MarkStoreRewrite(string fieldKey)
    {
        Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be null or empty.");
        HasStoreRewrites = true;
        _rewrittenAddedFieldKeys.Add(fieldKey);
    }

    public void MarkConstFold(string fieldKey)
    {
        Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be null or empty.");
        _foldedConstKeys.Add(fieldKey);
    }
}
