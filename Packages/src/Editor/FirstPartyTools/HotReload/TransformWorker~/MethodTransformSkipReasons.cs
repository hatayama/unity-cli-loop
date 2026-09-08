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
/// What: skip strings for the compiled-method transform decision. Keep in the existing
/// "reason + run uloop compile" style; the worker cannot reference HotReloadConstants.
/// </summary>
internal static class MethodTransformSkipReasons
{
    public const string NoBody = "Methods without a body (abstract/extern) are skipped.";

    public const string BaseMemberCall =
        "Methods that call base. members are skipped; C# cannot express base calls outside the type.";

    public const string ClosureInaccessibleAccess =
        "Lambda, local-function, or query-expression bodies that access private/internal members "
        + "are skipped (closure methods JIT-compile normally and fail accessibility checks).";

    public const string AsyncIteratorInaccessibleAccess =
        "Async or iterator methods whose bodies access private/internal members are skipped "
        + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).";

    public const string PartialType =
        "Partial types are skipped because a single file cannot provide a complete semantic model.";

    public const string StructHost =
        "Struct (value type) methods are skipped; byref instance transplant is unverified.";

    public const string GenericMethodOrType =
        "Generic methods and methods inside generic types cannot be safely patched with Harmony. " + CompileCallToAction.Plain;

    public const string ExplicitInterfaceImplementation = "Explicit interface implementations are skipped.";

    // Why an infix and not EventAccessorRules.AccessorRewriteUnavailableReasonPrefix: that one
    // opens the sentence, while this one appends the rejection to a skip reason already written.
    public const string AccessorRewriteUnavailableInfix = " Accessor rewrite unavailable: ";
}
