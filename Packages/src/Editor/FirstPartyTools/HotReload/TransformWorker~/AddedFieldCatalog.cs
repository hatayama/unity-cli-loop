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

    /// <summary>
    /// The initializer text of each rewritten added field, in the order
    /// <see cref="ListRewrittenAddedFieldDisplayNames"/> returns their names. A field declared
    /// without an initializer contributes an empty entry.
    /// </summary>
    /// <remarks>
    /// Why the text travels with the names: the Editor compares it against what the previous
    /// reload recorded, and a field that gained or changed an initializer after it was already
    /// active cannot reach a value the store already holds.
    /// </remarks>
    public string[] ListRewrittenAddedFieldInitializers(string projectRelativePath)
    {
        List<AddedFieldBinding> bindings = ListSortedBindingsOfFile(_rewrittenAddedFieldKeys, projectRelativePath);
        string[] initializers = new string[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            initializers[index] = FormatInitializerText(bindings[index]);
        }

        return initializers;
    }

    /// <summary>
    /// One declaration per entry <see cref="ListRewrittenAddedFieldDisplayNames"/> reports, in the
    /// same order: the store key, the declaring type, the declared type, and staticness.
    /// </summary>
    /// <remarks>
    /// Why the same rewritten set as the names: a field that no emitted body reads is not served
    /// by the store, so describing it would offer the Editor a field nothing can read back.
    /// </remarks>
    public WorkerAddedFieldDeclaration[] ListRewrittenAddedFieldDeclarations(
        string projectRelativePath,
        AddedFieldDeclaredTypeNames declaredTypeNames)
    {
        List<AddedFieldBinding> bindings = ListSortedBindingsOfFile(_rewrittenAddedFieldKeys, projectRelativePath);
        WorkerAddedFieldDeclaration[] declarations = new WorkerAddedFieldDeclaration[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            declarations[index] = DescribeBinding(bindings[index], declaredTypeNames);
        }

        return declarations;
    }

    private static WorkerAddedFieldDeclaration DescribeBinding(
        AddedFieldBinding binding,
        AddedFieldDeclaredTypeNames declaredTypeNames)
    {
        return new WorkerAddedFieldDeclaration
        {
            FieldKey = binding.FieldKey,
            DeclaringTypeMetadataName = ExtractTypeMetadataName(binding.FieldKey),
            FieldName = binding.FieldName,
            DeclaredTypeAssemblyQualifiedName =
                declaredTypeNames.ToAssemblyQualifiedName(binding.FieldType),
            IsStatic = binding.IsStatic,
            HasSerializationAttribute = binding.HasSerializationAttribute
        };
    }

    private static string ExtractTypeMetadataName(string fieldKey)
    {
        int separatorIndex = fieldKey.IndexOf(
            TransformWorkerProgramMarker.AddedFieldKeySeparator,
            StringComparison.Ordinal);
        Debug.Assert(
            separatorIndex >= 0,
            "fieldKey is always built with AddedFieldClassifier.FormatAddedFieldStoreKey / WorkerSyntaxIndex.BuildSyntaxFieldKey.");
        if (separatorIndex < 0)
        {
            return string.Empty;
        }

        return fieldKey.Substring(0, separatorIndex);
    }

    private string[] ListDisplayNamesOfFile(HashSet<string> fieldKeys, string projectRelativePath)
    {
        List<AddedFieldBinding> bindings = ListSortedBindingsOfFile(fieldKeys, projectRelativePath);
        string[] names = new string[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            names[index] = FormatAddedFieldDisplayName(bindings[index].FieldKey);
        }

        return names;
    }

    // Why filter by the binding's file: the catalog spans a whole group, while addedFieldNames
    // and addedConstNames are per-file rows in the worker output.
    // Why sorted by display name: the names row and the initializers row are read as parallel
    // arrays, so both have to be built from one order.
    private List<AddedFieldBinding> ListSortedBindingsOfFile(
        HashSet<string> fieldKeys,
        string projectRelativePath)
    {
        List<AddedFieldBinding> bindings = new List<AddedFieldBinding>(fieldKeys.Count);
        foreach (string fieldKey in fieldKeys)
        {
            AddedFieldBinding binding = GetRegistered(fieldKey);
            if (!string.Equals(binding.SourceProjectRelativePath, projectRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            bindings.Add(binding);
        }

        bindings.Sort(
            (left, right) => string.CompareOrdinal(
                FormatAddedFieldDisplayName(left.FieldKey),
                FormatAddedFieldDisplayName(right.FieldKey)));
        return bindings;
    }

    // Why normalized: re-indenting a declaration must not read as a changed initializer, and the
    // Editor only ever compares this text against the text of the previous reload.
    private static string FormatInitializerText(AddedFieldBinding binding)
    {
        if (binding.Initializer == null)
        {
            return string.Empty;
        }

        return binding.Initializer.NormalizeWhitespace().ToFullString();
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
