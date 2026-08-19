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
    public string SourcePath { get; set; }

    public string[] Defines { get; set; }

    public string[] ReferencePaths { get; set; }

    public string TargetTypesAssemblyPath { get; set; }

    // Method keys (see WorkerMethodKeys.BuildMethodKey) that the orchestrator already
    // reported Failed from a first compile round; the retry excludes them so it does not fail
    // on the same error again.
    public string[] ExcludedMethodKeys { get; set; }

    // Added-method keys whose shim bodies failed the first compile. Distinct from
    // ExcludedMethodKeys so a healthy added shim is not dropped when an existing method fails.
    public string[] ExcludedAddedMethodKeys { get; set; }

    // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
    // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
    // read that would crash the whole file under the no-try-catch policy.
    public string SnapshotSource { get; set; }

    // Project-relative forward-slash path embedded in #line document names.
    public string ProjectRelativePath { get; set; }

    // Absolute paths of every source file in the edited file's compilation assembly.
    // Null/omitted is treated as empty (no sibling global usings collected).
    public string[] AssemblySourcePaths { get; set; }
}
