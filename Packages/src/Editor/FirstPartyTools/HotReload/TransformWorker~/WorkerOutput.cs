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

internal sealed class WorkerOutput
{
    public string ShimSource { get; set; }

    public WorkerEntry[] Entries { get; set; }

    public WorkerSkipped[] Skipped { get; set; }

    public string[] DeclarationDriftWarnings { get; set; }

    // Keep in sync with TransformWorkerOutputDto.siblingConstDriftWarnings.
    public string[] SiblingConstDriftWarnings { get; set; }

    public string[] ParseErrors { get; set; }

    public WorkerUnchangedMethod[] UnchangedMethods { get; set; }

    public bool BaselineDisabledByDuplicateKeys { get; set; }

    public WorkerRemovedMember[] RemovedMembers { get; set; }

    public WorkerRemovedMethodSignature[] RemovedMethodSignatures { get; set; }

    public bool HasAccessorDelegates { get; set; }

    // True when shim bodies rewrite added-field accesses to HotReloadAddedFieldStore.
    // Keep in sync with TransformWorkerOutputDto.hasAddedFieldRewrites.
    public bool HasAddedFieldRewrites { get; set; }

    // Keep in sync with TransformWorkerOutputDto.addedFieldNames.
    public string[] AddedFieldNames { get; set; }

    // Keep in sync with TransformWorkerOutputDto.addedConstNames.
    public string[] AddedConstNames { get; set; }

    // Keep in sync with TransformWorkerOutputDto.sourceContentSha256.
    public string SourceContentSha256 { get; set; }
}
