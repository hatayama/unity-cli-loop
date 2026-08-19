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

// What: syntax-key maps for one file's baseline-vs-current comparison during TransformFile.
internal sealed class BaselineSnapshotState
{
    public bool HasBaseline { get; set; }

    public bool BaselineDisabledByDuplicateKeys { get; set; }

    public CompilationUnitSyntax SnapshotRoot { get; set; }

    public Dictionary<string, MethodDeclarationSyntax> SnapshotMethodMap { get; set; }

    public Dictionary<string, MethodDeclarationSyntax> PlainCurrentMethodMap { get; set; }

    public Dictionary<string, PropertyDeclarationSyntax> SnapshotPropertyMap { get; set; }

    public Dictionary<string, IndexerDeclarationSyntax> SnapshotIndexerMap { get; set; }

    public Dictionary<string, ConstructorDeclarationSyntax> SnapshotConstructorMap { get; set; }

    public Dictionary<string, MemberDeclarationSyntax> SnapshotOperatorMap { get; set; }

    public Dictionary<string, EventDeclarationSyntax> SnapshotEventMap { get; set; }

    public Dictionary<string, PropertyDeclarationSyntax> PlainCurrentPropertyMap { get; set; }

    public Dictionary<string, IndexerDeclarationSyntax> PlainCurrentIndexerMap { get; set; }

    public Dictionary<string, ConstructorDeclarationSyntax> PlainCurrentConstructorMap { get; set; }

    public Dictionary<string, MemberDeclarationSyntax> PlainCurrentOperatorMap { get; set; }

    public Dictionary<string, EventDeclarationSyntax> PlainCurrentEventMap { get; set; }

    public (
        Dictionary<string, PropertyDeclarationSyntax> SnapshotPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> SnapshotIndexerMap,
        Dictionary<string, PropertyDeclarationSyntax> PlainCurrentPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> PlainCurrentIndexerMap)
        GetAccessorBaselineMaps()
    {
        if (!HasBaseline)
        {
            return (null, null, null, null);
        }

        return (SnapshotPropertyMap, SnapshotIndexerMap, PlainCurrentPropertyMap, PlainCurrentIndexerMap);
    }

    public (
        Dictionary<string, ConstructorDeclarationSyntax> SnapshotConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> SnapshotOperatorMap,
        Dictionary<string, EventDeclarationSyntax> SnapshotEventMap,
        Dictionary<string, ConstructorDeclarationSyntax> PlainCurrentConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> PlainCurrentOperatorMap,
        Dictionary<string, EventDeclarationSyntax> PlainCurrentEventMap)
        GetUnsupportedMemberBaselineMaps()
    {
        if (!HasBaseline)
        {
            return (null, null, null, null, null, null);
        }

        return (
            SnapshotConstructorMap,
            SnapshotOperatorMap,
            SnapshotEventMap,
            PlainCurrentConstructorMap,
            PlainCurrentOperatorMap,
            PlainCurrentEventMap);
    }
}
