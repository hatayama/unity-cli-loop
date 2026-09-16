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

internal sealed class WorkerUnchangedMethod
{
    // Project-relative forward-slash path of the file this row was produced from.
    public string SourceProjectRelativePath { get; set; }

    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public int GenericArity { get; set; }

    // Simple name of the assembly this method's type is served from, or null when it is the
    // assembly the edited file belongs to.
    public string HomeAssemblyName { get; set; }
}
