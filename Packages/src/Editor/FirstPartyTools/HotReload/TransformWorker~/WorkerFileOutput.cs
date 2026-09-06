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

// Keep in sync with TransformWorkerDtos.cs TransformWorkerFileOutputDto.
internal sealed class WorkerFileOutput
{
    // Echoes WorkerSourceInput.ProjectRelativePath of the source this row set describes.
    public string ProjectRelativePath { get; set; }

    // SHA-256 (lowercase hex) of the raw source bytes the worker actually read.
    // Empty when the worker returned before reading the file.
    public string SourceContentSha256 { get; set; }

    public string[] ParseErrors { get; set; }

    public string[] DeclarationDriftWarnings { get; set; }

    public bool BaselineDisabledByDuplicateKeys { get; set; }

    public WorkerRemovedMember[] RemovedMembers { get; set; }

    public WorkerRemovedMethodSignature[] RemovedMethodSignatures { get; set; }

    public string[] AddedFieldNames { get; set; }

    public string[] AddedConstNames { get; set; }

    // Keep in sync with TransformWorkerDtos.cs TransformWorkerFileOutputDto.introducedTypes.
    public WorkerIntroducedType[] IntroducedTypes { get; set; }

    // Keep in sync with TransformWorkerDtos.cs TransformWorkerFileOutputDto.introducedTypeDiagnostics.
    public string[] IntroducedTypeDiagnostics { get; set; }
}
