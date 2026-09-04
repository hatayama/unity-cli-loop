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

// Keep in sync with TransformWorkerDtos.cs TransformWorkerSourceDto.
internal sealed class WorkerSourceInput
{
    // Absolute path the worker reads the edited text from. May be a temp override copy.
    public string SourcePath { get; set; }

    // Project-relative forward-slash path embedded in #line document names.
    public string ProjectRelativePath { get; set; }

    // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
    // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
    // read that would crash the whole file under the no-try-catch policy.
    public string SnapshotSource { get; set; }
}
