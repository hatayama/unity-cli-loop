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
/// What: per-shim-type registry of Harmony accessor delegates to emit and bind.
/// </summary>
internal sealed class AccessorPlan
{
    private readonly List<AccessorEntry> _entries = new List<AccessorEntry>();
    private readonly Dictionary<string, AccessorEntry> _byKey =
        new Dictionary<string, AccessorEntry>(StringComparer.Ordinal);

    public IReadOnlyList<AccessorEntry> Entries => _entries;

    public AccessorEntry GetOrAddField(IFieldSymbol fieldSymbol)
    {
        string key = "F:" + BuildMemberKey(fieldSymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__F_" + SanitizeIdentifier(fieldSymbol.Name));
        AccessorEntry entry = AccessorEntry.ForField(fieldSymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddMethod(IMethodSymbol methodSymbol)
    {
        string key = "M:" + BuildMemberKey(methodSymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__M_" + SanitizeIdentifier(methodSymbol.Name));
        AccessorEntry entry = AccessorEntry.ForMethod(methodSymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddPropertyGetter(IPropertySymbol propertySymbol)
    {
        string key = "PG:" + BuildMemberKey(propertySymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__P_get_" + SanitizeIdentifier(propertySymbol.Name));
        AccessorEntry entry = AccessorEntry.ForPropertyGetter(propertySymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddPropertySetter(IPropertySymbol propertySymbol)
    {
        string key = "PS:" + BuildMemberKey(propertySymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__P_set_" + SanitizeIdentifier(propertySymbol.Name));
        AccessorEntry entry = AccessorEntry.ForPropertySetter(propertySymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    private string AllocateName(string preferred)
    {
        if (!_entries.Any(entry => entry.DelegateFieldName == preferred))
        {
            return preferred;
        }

        int suffix = 2;
        while (_entries.Any(entry => entry.DelegateFieldName == preferred + suffix))
        {
            suffix++;
        }

        return preferred + suffix;
    }

    /// <summary>
    /// What: stable identity for a member across overloads and same-named members on different
    /// types — containing type FQ + name + (parameter type FQs).
    /// </summary>
    public static string BuildMemberKey(ISymbol symbol)
    {
        string typePart = symbol.ContainingType == null
            ? string.Empty
            : symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string name = symbol.Name;

        if (symbol is IMethodSymbol methodSymbol)
        {
            string args = string.Join(
                ",",
                methodSymbol.Parameters.Select(
                    parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return typePart + "." + name + "(" + args + ")";
        }

        if (symbol is IPropertySymbol propertySymbol)
        {
            string args = string.Join(
                ",",
                propertySymbol.Parameters.Select(
                    parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return typePart + "." + name + "(" + args + ")";
        }

        return typePart + "." + name + "()";
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "member";
        }

        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? "member" : builder.ToString();
    }
}
