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
        + "patched body. It takes effect only for newly created objects. A method hot reload added "
        + "or patched that calls it runs the patched body.";

    // Why OnEnable and OnDisable get their own note: toggling a component's enabled makes Unity
    // call both again on a live object, so "only for newly created objects" sent readers to a
    // compile and out of Play Mode for an edit they could run in place.
    public const string ReEnableDirectFormat =
        "{0} is a one-shot lifecycle method; objects that already ran it will not run the "
        + "patched body until Unity calls it again. To run it on live objects now, set the "
        + "component's `enabled` to false and back to true (for example with "
        + "`uloop execute-dynamic-code`): Unity calls OnDisable and then OnEnable. A method hot "
        + "reload added or patched that calls it runs the patched body.";

    public static string SelectDirectFormat(string methodName)
    {
        return methodName == "OnEnable" || methodName == "OnDisable" ? ReEnableDirectFormat : DirectFormat;
    }
}
