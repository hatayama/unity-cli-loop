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
/// Skip strings for added-property classification and call-site rewriting.
/// </summary>
internal static class AddedPropertySkipReasons
{
    public const string SetOnly =
        "Added properties with only a setter are skipped; the shim requires a getter identity. "
        + CompileCallToAction.ToAddThem;

    public const string VirtualOrAbstract =
        "Added virtual, override, abstract, or interface properties are skipped; the compiled type has no vtable slot. "
        + CompileCallToAction.ToAddThem;

    public const string ExplicitInterface =
        "Added explicit interface properties are skipped; the compiled type has no interface member slot. "
        + CompileCallToAction.ToAddThem;

    public const string InitAccessor =
        "Added properties with init accessors are skipped; the shim cannot preserve initialization-only assignment. "
        + CompileCallToAction.ToAddThem;

    public const string PropertyPattern =
        "Property patterns that match an added property are skipped; a pattern member name cannot "
        + "be replaced by an accessor shim call. " + CompileCallToAction.ToAddIt;

    public const string GenericHostType =
        "Added properties on generic types are skipped; one accessor identity and one store entry "
        + "cannot stand for every closed instantiation. " + CompileCallToAction.ToAddThem;

    public const string StructHost =
        "Added properties on struct types are skipped; the shim requires a reference-type instance. "
        + CompileCallToAction.ToAddThem;

    public const string ValueTypeUnresolvedFormat =
        "Added property type '{0}' could not be resolved; check for a missing using directive or a typo, "
        + "fix the declaration, and rerun. Run 'uloop compile' if the type is new.";

    public const string ValueTypeNotExternallyVisible =
        "Added property type is not visible to the shim assembly. " + CompileCallToAction.ToAddIt;

    public const string CompoundAssignment =
        "Compound assignment, increment, and decrement of an added property are skipped; the accessor shim cannot preserve the operation. "
        + CompileCallToAction.ToAddIt;

    public const string ConsumedWrite =
        "The value of an assignment to an added property is consumed; the setter shim returns void. "
        + CompileCallToAction.ToAddIt;

    public const string NameofReference =
        "References to added properties inside nameof are skipped; the member does not exist in the compiled assembly. "
        + CompileCallToAction.ToAddIt;

    public const string ObjectInitializer =
        "Object initializers that assign added properties are skipped; the setter shim cannot rewrite the initializer. "
        + CompileCallToAction.ToAddIt;

    public const string DeconstructionTarget =
        "Deconstruction assignment to an added property is skipped; the setter shim cannot stand as a "
        + "deconstruction target. " + CompileCallToAction.ToAddIt;

    public const string ConditionalAccess =
        "Conditional access to added properties is skipped; there is no rewrite shape. " + CompileCallToAction.ToAddIt;

    public const string RefOutIn =
        "Added properties cannot be passed by ref, out, or in. " + CompileCallToAction.ToAddThem;

    public const string UnavailableAddedProperty =
        "Uses an added property that hot reload cannot emit. " + CompileCallToAction.Plain;

    public const string CompiledMemberKindChanged =
        "Property '{0}' is declared as a field or an event in the compiled assembly. " + CompileCallToAction.Plain;

    public const string InitializerNotEmittable =
        "Added property initializer cannot run in the shim lambda. " + CompileCallToAction.ToAddIt;
}
