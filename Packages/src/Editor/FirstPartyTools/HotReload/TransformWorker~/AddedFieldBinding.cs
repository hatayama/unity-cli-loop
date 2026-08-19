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
/// What: one added field's store/const/unavailable binding used by skip evaluation and rewrite.
/// </summary>
internal sealed class AddedFieldBinding
{
    public string FieldKey { get; set; }

    public string SyntaxKey { get; set; }

    public string FieldName { get; set; }

    public ITypeSymbol FieldType { get; set; }

    public bool IsStatic { get; set; }

    public bool IsConst { get; set; }

    public object ConstantValue { get; set; }

    public ExpressionSyntax Initializer { get; set; }

    public string UnavailableReason { get; set; }

    public bool IsStoreRewriteable => UnavailableReason == null && !IsConst;
}
