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

// One retained introduced-type assembly this run may bind against. Identity and reference path
// travel in the same record so the worker can confirm the file it resolved is the assembly the
// record claims before normalizing any reference through it.
// Keep in sync with TransformWorkerDtos.cs TransformWorkerIntroducedTypeArtifactDto.
internal sealed class WorkerIntroducedTypeArtifact
{
    public string AssemblyFullName { get; set; }

    public string ReferencePath { get; set; }

    public WorkerIntroducedTypeArtifactType[] Types { get; set; }
}

// One retained type inside an artifact assembly, with the original identity it normalizes back to.
// Keep in sync with TransformWorkerDtos.cs TransformWorkerIntroducedTypeArtifactTypeDto.
internal sealed class WorkerIntroducedTypeArtifactType
{
    public string MetadataName { get; set; }

    public string OriginalAssemblyName { get; set; }

    public string OriginalAssemblyMvid { get; set; }
}
