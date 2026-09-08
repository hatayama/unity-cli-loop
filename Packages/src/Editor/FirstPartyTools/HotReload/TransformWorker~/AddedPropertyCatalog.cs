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
internal sealed class AddedPropertyCatalog : AddedMemberCatalog<AddedPropertyBinding>
{
    public IEnumerable<AddedPropertyBinding> Bindings => RegisteredBindings;

    protected override string KeyOf(AddedPropertyBinding binding)
    {
        return binding.PropertyKey;
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
}
