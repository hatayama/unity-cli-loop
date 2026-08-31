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
        + "Run 'uloop compile' to add them.";

    public const string AddedProperty =
        "Added properties are out of scope for hot reload; the compiled assembly has no such member. "
        + "Use a 'const' or a plain added field for the value, or run 'uloop compile'.";

    public const string Generic =
        "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
        + "Run 'uloop compile'.";

    public const string MethodGroupReference =
        "Methods that capture an added method as a method group or delegate are skipped; "
        + "the shim signature does not match. Run 'uloop compile'.";

    public const string ConditionalAccess =
        "Added-method calls through conditional access are skipped; there is no rewrite shape. "
        + "Run 'uloop compile'.";

    public const string UnavailableAddedCall =
        "Calls an added method that hot reload cannot emit. Run 'uloop compile'.";

    public const string NewTypeOutOfScope =
        "New types are out of scope for hot reload; run 'uloop compile' to add them.";

    public const string InterfaceMember =
        "Interface members are not patchable. Run 'uloop compile'.";

    public const string InaccessibleAccessNoRewrite =
        "Added methods whose bodies access private/internal members are skipped when the access "
        + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
        + "checks).";
}
