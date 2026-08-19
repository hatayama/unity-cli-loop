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

internal static class LifecycleNotes
{
    public static readonly string[] OneShotLifecycleMethodNames =
    {
        "Awake",
        "Start",
        "OnEnable",
        "OnDisable",
        "OnDestroy"
    };

    public const string DirectFormat =
        "{0} is a one-shot lifecycle method; objects that already ran it will not run the "
        + "patched body. It takes effect only for newly created objects.";
}
