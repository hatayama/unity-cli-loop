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

internal sealed class WorkerInput
{
    // Null/empty retains the existing transform operation for older callers.
    public string Operation { get; set; }

    // Edited files this run transforms together; all belong to the same compilation assembly.
    // Keep in sync with TransformWorkerDtos.cs TransformWorkerInputDto.sources.
    public WorkerSourceInput[] Sources { get; set; }

    public string[] Defines { get; set; }

    public string[] ReferencePaths { get; set; }

    public string TargetTypesAssemblyPath { get; set; }

    public string TargetAssemblyName { get; set; }

    public string TargetAssemblyMvid { get; set; }

    // Method keys (see WorkerMethodKeys.BuildMethodKey) that the orchestrator already
    // reported Failed from a first compile round; the retry excludes them so it does not fail
    // on the same error again.
    public string[] ExcludedMethodKeys { get; set; }

    // Added-method keys whose shim bodies failed the first compile. Distinct from
    // ExcludedMethodKeys so a healthy added shim is not dropped when an existing method fails.
    public string[] ExcludedAddedMethodKeys { get; set; }

    // Absolute paths of every source file in the edited file's compilation assembly.
    // Null/omitted is treated as empty (no sibling global usings collected).
    public string[] AssemblySourcePaths { get; set; }

    // Absolute paths of snapshot-mismatched sibling sources in the same compilation assembly.
    // Null/omitted is treated as empty (no sibling const-drift scan).
    public string[] ChangedSiblingSourcePaths { get; set; }

    // Retained introduced-type assemblies this run may bind against.
    // Null/omitted is treated as empty (nothing is normalized through an artifact).
    public WorkerIntroducedTypeArtifact[] IntroducedTypeArtifacts { get; set; }
}
