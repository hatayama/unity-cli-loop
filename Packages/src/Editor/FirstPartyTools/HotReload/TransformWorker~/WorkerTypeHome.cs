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
/// Where the patch target's already-compiled types live for one worker run: the assembly the
/// request named, and the symbol that assembly binds to. Every query for "does the target
/// already hold this type" goes through here, so the answer comes from one place instead of
/// each classifier holding its own assembly symbol.
///
/// Why not search the retained artifact assemblies here as well: IntroducedTypePlanner treats
/// a found compiled type as "already exists" and stops planning that declaration. If a lookup
/// also saw the artifacts, a changed introduced type would be found in its own previous
/// artifact and never reach the fingerprint comparison, so the "Changed introduced type
/// requires a compile" diagnostic would disappear. Artifacts are indexed separately by
/// IntroducedTypeArtifactMap.
///
/// Which callers take this instead of a bare IAssemblySymbol: the ones that look a compiled
/// type up in the target. Code that only compares assembly identity or feeds a symbol into a
/// fingerprint keeps taking IAssemblySymbol, because it needs no lookup and treats a missing
/// target assembly as an ordinary case.
/// </summary>
internal sealed class WorkerTypeHome
{
    internal WorkerTypeHome(string assemblyName, IAssemblySymbol assemblySymbol)
    {
        AssemblyName = assemblyName;
        AssemblySymbol = assemblySymbol;
    }

    internal string AssemblyName { get; }

    // Null when the run could not read the target assembly; every lookup then finds nothing,
    // which is what the callers' "not compiled yet" branches already expect.
    internal IAssemblySymbol AssemblySymbol { get; }

    /// <summary>
    /// The compiled counterpart of a source type, or null when this home does not hold it.
    /// </summary>
    internal INamedTypeSymbol FindCompiledType(INamedTypeSymbol sourceType)
    {
        if (sourceType == null)
        {
            return null;
        }

        return FindCompiledTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(sourceType));
    }

    /// <summary>
    /// The compiled type this home holds under a reflection metadata name, or null when it
    /// holds none. The name must already be in reflection form (nested types joined by '+').
    /// </summary>
    internal INamedTypeSymbol FindCompiledTypeByMetadataName(string reflectionMetadataName)
    {
        if (AssemblySymbol == null)
        {
            return null;
        }

        return AssemblySymbol.GetTypeByMetadataName(reflectionMetadataName);
    }
}
