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

/// <summary>
/// What: one added field of a file, described well enough for the Editor to validate a value
/// written into the store from outside a shim.
/// </summary>
/// <remarks>
/// Why alongside AddedFieldNames rather than replacing it: the names row feeds the ledger, the
/// warnings and the --status rows, and rebuilding those consumers on this shape would spread a
/// reporting change across the run for no gain.
/// </remarks>
internal sealed class WorkerAddedFieldDeclaration
{
    // The store key the emitted shims pass to the added-field store: the declaring type's
    // metadata name (nested types with '/'), the separator, then the field name.
    public string FieldKey { get; set; }

    public string DeclaringTypeMetadataName { get; set; }

    public string FieldName { get; set; }

    // Assembly-qualified so the Editor can resolve the type without guessing its assembly.
    // Empty when the compilation could not name the type.
    public string DeclaredTypeAssemblyQualifiedName { get; set; }

    public bool IsStatic { get; set; }

    // SerializeField, SerializeReference or FormerlySerializedAs on the declaration.
    public bool HasSerializationAttribute { get; set; }
}
