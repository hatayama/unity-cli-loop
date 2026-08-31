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
/// What: skip and warning strings for added-field classification. Keep in the existing
/// "reason + run uloop compile" style; the worker cannot reference HotReloadConstants.
/// </summary>
internal static class AddedFieldSkipReasons
{
    public const string StructHost =
        "Added fields on struct types are skipped; the store requires a reference-type instance. "
        + "Run 'uloop compile' to add them.";

    public const string InitializerNotLiteralOrExternalStatic =
        "Added field initializer is not a literal or externally visible static member; "
        + "the shim lambda cannot use instance, host-type, or same-file added members. "
        + "Run 'uloop compile'.";

    public const string FieldTypeNotExternallyVisible =
        "Added field type is not visible to the shim assembly. Run 'uloop compile'.";

    public const string IncrementNotNumeric =
        "Increment or decrement of an added field is skipped unless the type is a numeric "
        + "primitive or enum. Run 'uloop compile'.";

    public const string RefOutIn =
        "Added fields cannot be passed by ref, out, or in. Run 'uloop compile'.";

    public const string ConsumedWrite =
        "The value of an assignment to an added field is consumed; the store write returns void. "
        + "Run 'uloop compile'.";

    public const string DoubleEvalReceiver =
        "Assignment to an added field would evaluate a receiver with possible side effects twice. "
        + "Run 'uloop compile'.";

    public const string ValueTypeMemberWrite =
        "Writes to members of an added value-type field, and instance method calls on that field, "
        + "cannot be rewritten. Run 'uloop compile'.";

    public const string UnavailableAddedField =
        "Uses an added field that hot reload cannot emit. Run 'uloop compile'.";

    public const string FieldTypeChanged =
        "Field '{0}' has a different type in the compiled assembly. Run 'uloop compile'.";

    public const string FieldModifiersChanged =
        "Field '{0}' changed its static or const modifier in the compiled assembly. Run 'uloop compile'.";

    public const string MemberKindChanged =
        "Field '{0}' is declared as a property or an event in the compiled assembly. Run 'uloop compile'.";

    public const string SerializeWarningFormat =
        "Added field '{0}' has a serialization attribute, so it will not appear in the Inspector "
        + "or serialize until 'uloop compile'.";
}
