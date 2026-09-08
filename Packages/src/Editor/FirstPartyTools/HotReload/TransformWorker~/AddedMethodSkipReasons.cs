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

internal static class AddedMethodSkipReasons
{
    public const string VirtualOrAbstract =
        "Added virtual, override, or abstract methods are skipped; the compiled type has no vtable slot. "
        + CompileCallToAction.ToAddThem;

    public const string Generic =
        "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
        + CompileCallToAction.Plain;

    public const string MethodGroupReference =
        "Methods that capture an added method as a method group or delegate are skipped; "
        + "the shim signature does not match. " + CompileCallToAction.Plain;

    public const string ConditionalAccess =
        "Added-method calls through conditional access are skipped; there is no rewrite shape. "
        + CompileCallToAction.Plain;

    public const string UnavailableAddedCall =
        "Calls an added method that hot reload cannot emit. " + CompileCallToAction.Plain;

    public const string TypeNotIntroduced =
        "Declared on a type that is not in the compiled assembly and was not introduced by this "
        + "run. Run 'uloop compile'; when the file's introduced-type diagnostics name this type, "
        + "that line gives the reason it was not introduced.";

    public const string InterfaceMember =
        "Interface members are not patchable. " + CompileCallToAction.Plain;

    public const string InaccessibleAccessNoRewrite =
        "Added methods whose bodies access private/internal members are skipped when the access "
        + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
        + "checks).";
}
