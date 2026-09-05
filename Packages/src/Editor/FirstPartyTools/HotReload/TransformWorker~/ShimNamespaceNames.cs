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

// The namespace a shim type is emitted in. Shared by the emitter that declares the type and
// by every rewrite that references it by name: a rewrite that guessed the global namespace
// while the emitter synthesized one would emit a call that fails shim compilation with CS0246.
internal static class ShimNamespaceNames
{
    // Host namespace for shim types whose original type lives in the global namespace. The
    // orchestrator resolves shim types by short name, so the namespace stays invisible to it.
    internal const string GlobalNamespaceShimNamespaceName = "UloopHotReloadGlobalShim";

    internal static string ResolveShimNamespaceName(string originalNamespaceName)
    {
        return string.IsNullOrEmpty(originalNamespaceName)
            ? GlobalNamespaceShimNamespaceName
            : originalNamespaceName;
    }
}
