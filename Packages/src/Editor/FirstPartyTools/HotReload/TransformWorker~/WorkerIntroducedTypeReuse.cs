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

// Keep in sync with TransformWorkerDtos.cs TransformWorkerIntroducedTypeReuseDto.
internal sealed class WorkerIntroducedTypeReuse
{
    public string MetadataName { get; set; }

    public string OriginalAssemblyName { get; set; }

    public string OriginalAssemblyMvid { get; set; }
}
