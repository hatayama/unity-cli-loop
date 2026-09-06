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
/// Added-property bindings indexed by property and accessor identity for classification and rewrite.
/// </summary>
internal sealed class AddedPropertyCatalog
{
    private readonly Dictionary<string, AddedPropertyBinding> _byPropertyKey =
        new Dictionary<string, AddedPropertyBinding>(StringComparer.Ordinal);
    private readonly Dictionary<string, AddedPropertyBinding> _byAccessorKey =
        new Dictionary<string, AddedPropertyBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);

    public IEnumerable<AddedPropertyBinding> Bindings => _byPropertyKey.Values;

    public void MarkClassifiedAdded(string propertyKey)
    {
        if (propertyKey != null)
        {
            _classifiedAddedKeys.Add(propertyKey);
        }
    }

    public bool IsClassifiedAdded(string propertyKey)
    {
        return propertyKey != null && _classifiedAddedKeys.Contains(propertyKey);
    }

    public void Register(AddedPropertyBinding binding)
    {
        Debug.Assert(binding != null, "binding must not be null.");
        Debug.Assert(!string.IsNullOrEmpty(binding.PropertyKey), "binding.PropertyKey must not be null or empty.");
        _byPropertyKey[binding.PropertyKey] = binding;
        MarkClassifiedAdded(binding.PropertyKey);
        RegisterAccessor(binding.Getter, binding);
        RegisterAccessor(binding.Setter, binding);
    }

    public AddedPropertyBinding FindOrNull(string propertyKey)
    {
        if (propertyKey == null)
        {
            return null;
        }

        return _byPropertyKey.TryGetValue(propertyKey, out AddedPropertyBinding binding) ? binding : null;
    }

    public AddedPropertyBinding FindByAccessorKeyOrNull(string accessorKey)
    {
        if (accessorKey == null)
        {
            return null;
        }

        return _byAccessorKey.TryGetValue(accessorKey, out AddedPropertyBinding binding) ? binding : null;
    }

    public AddedPropertyBinding FindBySymbolOrNull(IPropertySymbol propertySymbol)
    {
        if (propertySymbol == null || propertySymbol.ContainingType == null)
        {
            return null;
        }

        return FindOrNull(FormatPropertyKey(
            CecilTypeNames.ToMetadataName(propertySymbol.ContainingType),
            propertySymbol.Name));
    }

    public void MarkUnavailable(string propertyKey, string reason)
    {
        AddedPropertyBinding binding = FindOrNull(propertyKey);
        if (binding != null)
        {
            binding.UnavailableReason = reason;
        }
    }

    public static string FormatPropertyKey(string typeMetadataName, string propertyName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + propertyName;
    }

    private void RegisterAccessor(AddedMethodBinding accessor, AddedPropertyBinding binding)
    {
        if (accessor != null && accessor.MethodKey != null)
        {
            _byAccessorKey[accessor.MethodKey] = binding;
        }
    }
}
