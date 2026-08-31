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

// Nested access to the instance parameter name without exposing TransformWorkerProgram fields
// to the rewriter as a circular partial. Kept as a tiny marker type so the rewriter stays free
// of string literals scattered across Visit overrides.
internal static class TransformWorkerProgramMarker
{
    // Why "__uloopInstance": the shim prepends this receiver parameter to the user's own
    // parameter list verbatim, so a plain name like "instance" collides (CS0100) with any
    // user parameter or local of that name. The uloop-prefixed name makes collisions
    // practically impossible.
    public const string InstanceParameterName = "__uloopInstance";

    // Keep in sync with HotReloadAddedFieldStore in ToolContracts.
    public const string AddedFieldStoreTypeName =
        "global::io.github.hatayama.UnityCliLoop.ToolContracts.HotReloadAddedFieldStore";

    public const string AddedFieldGetOrInitMethodName = "GetOrInit";

    public const string AddedFieldSetMethodName = "Set";

    public const string AddedFieldGetOrInitStaticMethodName = "GetOrInitStatic";

    public const string AddedFieldSetStaticMethodName = "SetStatic";

    // Keep in sync with HotReloadAddedFieldStore.FieldKeySeparator.
    public const string AddedFieldKeySeparator = "::";
}
