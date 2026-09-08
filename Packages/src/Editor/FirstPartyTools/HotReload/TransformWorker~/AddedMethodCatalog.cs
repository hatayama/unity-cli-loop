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

internal sealed class AddedMethodCatalog : AddedMemberCatalog<AddedMethodBinding>
{
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedTypeSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedPropertySyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> AddedTypeSyntaxKeys => _addedTypeSyntaxKeys;

    public IReadOnlyCollection<string> AddedPropertySyntaxKeys => _addedPropertySyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    protected override string KeyOf(AddedMethodBinding binding)
    {
        return binding.MethodKey;
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

    // A metadata receiver cannot bind to a source-only added method, so the rewriter
    // matches on type name, method name, and argument count when GetSymbolInfo is unbound.
    public AddedMethodBinding FindUniqueByReceiverOrNull(
        string typeMetadataName,
        string methodName,
        int argumentCount)
    {
        Debug.Assert(typeMetadataName != null, "typeMetadataName");
        Debug.Assert(methodName != null, "methodName");

        string prefix = typeMetadataName + "::" + methodName + "(";
        AddedMethodBinding unique = null;
        int matches = 0;
        foreach (AddedMethodBinding binding in RegisteredBindings)
        {
            if (binding.MethodKey == null
                || !binding.MethodKey.StartsWith(prefix, StringComparison.Ordinal)
                || binding.ParameterCount != argumentCount)
            {
                continue;
            }

            matches++;
            unique = binding;
            if (matches > 1)
            {
                return null;
            }
        }

        return matches == 1 ? unique : null;
    }

    // Why the classified key stays: IsUnavailableAdded reads "classified but not registered".
    public void Unregister(string methodKey)
    {
        if (methodKey != null)
        {
            RemoveRegistered(methodKey);
        }
    }
}
