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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

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

    // One entry per AddedFieldNames entry, in the same order: the normalized initializer text,
    // empty when the field is declared without one.
    public string[] AddedFieldInitializers { get; set; }

    // One entry per AddedFieldNames entry, in the same order: the store key, the declaring type,
    // the declared type and staticness of that field.
    public WorkerAddedFieldDeclaration[] AddedFieldDeclarations { get; set; }

    public string[] AddedConstNames { get; set; }

    public WorkerIntroducedType[] IntroducedTypes { get; set; }

    public WorkerReason[] IntroducedTypeDiagnostics { get; set; }

    public WorkerIntroducedTypeReuse[] IntroducedTypeReuses { get; set; }

    // Prepare run only: names of members this source declares on a compiled or retained type
    // that the type does not hold yet. Empty from the transform run.
    public string[] PlannedAddedMemberNames { get; set; }

    // Prepare run only: names of members this source adds to a compiled enum. Kept apart from
    // PlannedAddedMemberNames because hot reload cannot add an enum member at all, so a failed
    // introduced-type compilation has to point at the enum-member warning instead.
    public string[] PlannedAddedEnumMemberNames { get; set; }

    // Prepare run only: the const and enum-member drift warnings of this source. The transform
    // run reports them itself, so the Editor surfaces these only when the run stops before it.
    public string[] PreparedDeclarationDriftWarnings { get; set; }
}
