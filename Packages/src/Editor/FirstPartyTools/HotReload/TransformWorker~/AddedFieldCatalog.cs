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
internal sealed class AddedFieldCatalog
{
    private readonly Dictionary<string, AddedFieldBinding> _byKey =
        new Dictionary<string, AddedFieldBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _rewrittenAddedFieldKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _foldedConstKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    public bool HasClassifiedAdded => _classifiedAddedKeys.Count > 0;

    public bool HasStoreRewrites { get; private set; }

    public void MarkClassifiedAdded(string fieldKey)
    {
        if (fieldKey != null)
        {
            _classifiedAddedKeys.Add(fieldKey);
        }
    }

    public void AddAddedSyntaxKey(string syntaxKey)
    {
        _addedSyntaxKeys.Add(syntaxKey);
    }

    public void AddRemovedSyntaxKey(string syntaxKey)
    {
        _removedSyntaxKeys.Add(syntaxKey);
    }

    public void RegisterStore(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    public void RegisterConst(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    // Why rewritten keys, not RegisterStore/RegisterConst: those fire at declaration
    // classification, so unused fields and isolation-excluded bodies would still list.
    // Excluded methods are dropped in TypeEmitPlanner.QueueTypeMethods before rewrite, so a file-wide
    // rewrite set matches emitted entries without per-entry tracking.
    public string[] ListRewrittenAddedFieldDisplayNames()
    {
        List<string> names = new List<string>(_rewrittenAddedFieldKeys.Count);
        foreach (string fieldKey in _rewrittenAddedFieldKeys)
        {
            names.Add(FormatAddedFieldDisplayName(fieldKey));
        }

        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }

    public string[] ListFoldedConstDisplayNames()
    {
        List<string> names = new List<string>(_foldedConstKeys.Count);
        foreach (string fieldKey in _foldedConstKeys)
        {
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

    public void RegisterUnavailable(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    public AddedFieldBinding FindOrNull(string fieldKey)
    {
        if (fieldKey == null)
        {
            return null;
        }

        return _byKey.TryGetValue(fieldKey, out AddedFieldBinding binding) ? binding : null;
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
