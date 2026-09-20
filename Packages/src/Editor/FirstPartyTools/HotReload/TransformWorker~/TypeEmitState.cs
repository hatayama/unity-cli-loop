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

internal sealed class TypeEmitState
{
    // The edited file this type was declared in. Emit reads its SemanticModel, root and
    // baseline from here so a group run never binds a type against another file's tree.
    public WorkerSourceUnit SourceUnit { get; set; }

    public TypeDeclarationSyntax TypeDeclaration { get; set; }

    public INamedTypeSymbol TypeSymbol { get; set; }

    // The matching type in the compiled assembly, or null when the type is not compiled yet.
    // Event rewrites need it to tell an event added in this edit from one with a backing field.
    public INamedTypeSymbol CompiledType { get; set; }

    public string TypeMetadataNameFromSyntax { get; set; }

    // Which private members of this type the reload adds, set once its added properties are
    // classified. Method decisions read it so the accessor plan leaves those members to the
    // added-member rewrite.
    public AddedMemberAccessLookup AddedMemberAccess { get; set; }

    public ShimTypeBuilder CurrentShimType { get; set; }

    public List<QueuedShimMethod> QueuedMethods { get; } = new List<QueuedShimMethod>();

    // Simple name of the assembly this type's methods are patched in, or null when that is the
    // patch target the request named. Set only for a type a retained artifact serves.
    public string HomeAssemblyName { get; set; }

    // The syntax method keys whose bodies this edit changed on a type a retained artifact serves,
    // or null when no artifact serves the type. An ordinary method outside the set still runs the
    // body the artifact holds, so patching it would replace a body with the same body.
    public HashSet<string> RetainedChangedMethodKeys { get; set; }

    // The syntax property keys whose getter body this edit changed on a type a retained artifact
    // serves, or null when no artifact serves the type. A property outside the set still runs the
    // getter the artifact holds. Set together with RetainedChangedMethodKeys, so either one being
    // null says the same thing about the type.
    public HashSet<string> RetainedChangedGetterPropertyKeys { get; set; }

    public bool TypeIsAbsentFromCompiledAssembly { get; set; }
}
