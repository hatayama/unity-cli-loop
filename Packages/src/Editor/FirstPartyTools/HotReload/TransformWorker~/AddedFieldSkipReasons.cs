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
/// What: the drift warning an added field reports. Unlike a skip reason this one is a warning
/// the worker words itself, because no reason code carries it.
/// </summary>
internal static class AddedFieldSkipReasons
{
    public const string SerializeWarningFormat =
        "Added field '{0}' has a serialization attribute, so it will not appear in the Inspector "
        + "or serialize until 'uloop compile'. To put a value in it now, see the added-field "
        + "wiring recipe in the hot-reload skill references.";
}
