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

internal sealed class QueuedShimMethod
{
    public MethodDeclarationSyntax MethodDeclaration { get; set; }

    public IMethodSymbol MethodSymbol { get; set; }

    public MethodTransformDecision Decision { get; set; }

    public string ShimMethodName { get; set; }

    public ShimTypeBuilder ShimType { get; set; }

    public int SourceStartLine { get; set; }

    public int SourceEndLine { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public string MethodKey { get; set; }

    public bool IsAddedMethod { get; set; }

    public bool ReplacesCompiledMethod { get; set; }
}
