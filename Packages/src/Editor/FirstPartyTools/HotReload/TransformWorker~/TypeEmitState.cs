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

internal sealed class TypeEmitState
{
    public TypeDeclarationSyntax TypeDeclaration { get; set; }

    public INamedTypeSymbol TypeSymbol { get; set; }

    // The matching type in the compiled assembly, or null when the type is not compiled yet.
    // Event rewrites need it to tell an event added in this edit from one with a backing field.
    public INamedTypeSymbol CompiledType { get; set; }

    public string TypeMetadataNameFromSyntax { get; set; }

    public ShimTypeBuilder CurrentShimType { get; set; }

    public List<QueuedShimMethod> QueuedMethods { get; } = new List<QueuedShimMethod>();

    public bool TypeIsAbsentFromCompiledAssembly { get; set; }
}
