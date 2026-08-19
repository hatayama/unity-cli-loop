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

internal sealed class AddedMethodBinding
{
    public string MethodKey { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }

    public string NamespaceName { get; set; }

    public bool IsStatic { get; set; }
}
