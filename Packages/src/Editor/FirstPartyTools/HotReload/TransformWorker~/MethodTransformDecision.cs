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

internal sealed class MethodTransformDecision
{
    public string SkipReason { get; private set; }

    public string PatchKind { get; private set; }

    public bool UsesDelegation { get; private set; }

    public static MethodTransformDecision Skip(string reason)
    {
        return new MethodTransformDecision { SkipReason = reason };
    }

    public static MethodTransformDecision Transplant()
    {
        return new MethodTransformDecision { PatchKind = PatchKinds.Transplant };
    }

    public static MethodTransformDecision Delegation()
    {
        return new MethodTransformDecision
        {
            PatchKind = PatchKinds.Delegation,
            UsesDelegation = true
        };
    }

    public static MethodTransformDecision AddedMethod(bool usesDelegation)
    {
        return new MethodTransformDecision
        {
            PatchKind = PatchKinds.AddedMethod,
            UsesDelegation = usesDelegation
        };
    }
}
