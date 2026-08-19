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

internal sealed class WorkerEntry
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public int GenericArity { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }

    // "transplant" | "delegation" | "addedMethod" — see PatchKinds.
    public string PatchKind { get; set; }

    // Method keys of added methods this entry's body invokes. Empty when none.
    public string[] CalledAddedMethodKeys { get; set; }

    // 1-based, both ends inclusive, within the original edited source file.
    public int SourceStartLine { get; set; }

    public int SourceEndLine { get; set; }

    // Null when the method is not a one-shot lifecycle method and is not only called from them.
    public string LifecycleNote { get; set; }

    public bool ReplacesCompiledMethod { get; set; }
}
