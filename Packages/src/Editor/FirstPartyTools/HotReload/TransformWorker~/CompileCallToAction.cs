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
/// What: the "compile it properly" call to action appended to a skip reason. The worker cannot
/// reference HotReloadConstants, so the three wordings live here instead of being retyped.
/// </summary>
internal static class CompileCallToAction
{
    public const string Plain = "Run 'uloop compile'.";

    public const string ToAddIt = "Run 'uloop compile' to add it.";

    public const string ToAddThem = "Run 'uloop compile' to add them.";
}
