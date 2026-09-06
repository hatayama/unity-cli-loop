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

// Everything one edited source file contributes to a group run: its parse products, its
// baseline, and the rows and warnings that belong to that file alone. A group run holds one
// unit per source so per-file state never has to be threaded through the emit pipeline.
internal sealed class WorkerSourceUnit
{
    public WorkerSourceInput Input { get; set; }

    // Null when the source could not be read or its path is invalid; such a unit stays out of
    // the compilation and every later per-unit step skips it.
    public SyntaxTree SyntaxTree { get; set; }

    // Annotated root, bound to SyntaxTree. Emit needs the uloop-line annotations.
    public CompilationUnitSyntax Root { get; set; }

    // Unannotated root. Baseline comparison must use it on both sides.
    public CompilationUnitSyntax PlainRoot { get; set; }

    // The tree the transform compilation binds against: the annotated tree with the declarations
    // that are already served from a retained artifact removed. Equal to SyntaxTree when the run
    // has no such declaration. A SemanticModel only answers for nodes of the tree it came from,
    // so every consumer that uses SemanticModel must read BindingRoot, never Root.
    public SyntaxTree BindingSyntaxTree { get; set; }

    // Root of BindingSyntaxTree.
    public CompilationUnitSyntax BindingRoot { get; set; }

    public SemanticModel SemanticModel { get; set; }

    public BaselineSnapshotState Baseline { get; set; }

    public string SourceContentSha256 { get; set; }

    public List<string> ParseErrors { get; } = new List<string>();

    public List<string> DeclarationDriftWarnings { get; } = new List<string>();

    public List<WorkerRemovedMember> RemovedMembers { get; } = new List<WorkerRemovedMember>();

    public List<WorkerRemovedMethodSignature> RemovedMethodSignatures { get; } =
        new List<WorkerRemovedMethodSignature>();

    public List<WorkerIntroducedType> IntroducedTypes { get; } = new List<WorkerIntroducedType>();

    public List<string> IntroducedTypeDiagnostics { get; } = new List<string>();

    public CompiledMemberKindChangeWarnings.SyntaxKeys KindChangeSyntaxKeys { get; set; }

    public List<TypeEmitState> TypeEmitStates { get; set; } = new List<TypeEmitState>();
}
