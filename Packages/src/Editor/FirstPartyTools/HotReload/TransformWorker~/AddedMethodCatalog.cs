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

internal sealed class AddedMethodCatalog
{
    private readonly Dictionary<string, AddedMethodBinding> _byKey =
        new Dictionary<string, AddedMethodBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedTypeSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedPropertySyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> AddedTypeSyntaxKeys => _addedTypeSyntaxKeys;

    public IReadOnlyCollection<string> AddedPropertySyntaxKeys => _addedPropertySyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    public void Register(AddedMethodBinding binding)
    {
        _byKey[binding.MethodKey] = binding;
        MarkClassifiedAdded(binding.MethodKey);
    }

    public void MarkClassifiedAdded(string methodKey)
    {
        if (methodKey != null)
        {
            _classifiedAddedKeys.Add(methodKey);
        }
    }

    public bool IsClassifiedAdded(string methodKey)
    {
        return methodKey != null && _classifiedAddedKeys.Contains(methodKey);
    }

    public bool IsUnavailableAdded(string methodKey)
    {
        return IsClassifiedAdded(methodKey) && !Contains(methodKey);
    }

    public void AddAddedSyntaxKey(string syntaxKey)
    {
        _addedSyntaxKeys.Add(syntaxKey);
    }

    public void AddAddedTypeSyntaxKey(string typeSyntaxKey)
    {
        if (typeSyntaxKey != null)
        {
            _addedTypeSyntaxKeys.Add(typeSyntaxKey);
        }
    }

    public void AddAddedPropertySyntaxKey(string propertySyntaxKey)
    {
        if (propertySyntaxKey != null)
        {
            _addedPropertySyntaxKeys.Add(propertySyntaxKey);
        }
    }

    public void AddRemovedSyntaxKey(string syntaxKey)
    {
        _removedSyntaxKeys.Add(syntaxKey);
    }

    public bool Contains(string methodKey)
    {
        return methodKey != null && _byKey.ContainsKey(methodKey);
    }

    public AddedMethodBinding FindOrNull(string methodKey)
    {
        if (methodKey == null)
        {
            return null;
        }

        return _byKey.TryGetValue(methodKey, out AddedMethodBinding binding) ? binding : null;
    }

    public void Unregister(string methodKey)
    {
        if (methodKey != null)
        {
            _byKey.Remove(methodKey);
        }
    }
}
