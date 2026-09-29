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

internal sealed class WorkerIntroducedType
{
    public string OriginalAssemblyName { get; set; }

    public string OriginalAssemblyMvid { get; set; }

    public string MetadataName { get; set; }

    public string OwnerProjectRelativePath { get; set; }

    public string DeclarationFingerprint { get; set; }

    public string Source { get; set; }

    // The methods whose bodies the source stubs because they call members this reload adds, keyed
    // the way the transform's entries are. The artifact may only be activated once every one of
    // them is patched, since until then those bodies throw instead of running.
    public string[] StubbedMethodKeys { get; set; } = Array.Empty<string>();
}
