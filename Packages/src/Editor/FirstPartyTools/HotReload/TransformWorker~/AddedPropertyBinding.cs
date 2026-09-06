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
/// One added property's accessor bindings and declaration state for shim emission.
/// </summary>
internal sealed class AddedPropertyBinding
{
    public string SourceProjectRelativePath { get; set; }

    public string PropertyKey { get; set; }

    public string SyntaxKey { get; set; }

    public string Name { get; set; }

    public INamedTypeSymbol HostType { get; set; }

    public ITypeSymbol ValueType { get; set; }

    public bool IsStatic { get; set; }

    public bool IsAuto { get; set; }

    public AddedMethodBinding Getter { get; set; }

    public AddedMethodBinding Setter { get; set; }

    public MethodTransformDecision GetterDecision { get; set; }

    public MethodTransformDecision SetterDecision { get; set; }

    public string StoreFieldKey { get; set; }

    public ExpressionSyntax Initializer { get; set; }

    public string UnavailableReason { get; set; }

    public PropertyDeclarationSyntax Declaration { get; set; }

    public IPropertySymbol Symbol { get; set; }
}
