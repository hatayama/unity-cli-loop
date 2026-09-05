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

// Turns one wire source into a WorkerSourceUnit: validates the path, reads the text, hashes it,
// then parses and annotates it. Failures land in the unit's ParseErrors with a null SyntaxTree
// so the group run can keep going for the other sources.
internal static class WorkerSourceLoader
{
    internal static WorkerSourceUnit Load(WorkerSourceInput source, CSharpParseOptions parseOptions)
    {
        WorkerSourceUnit unit = new WorkerSourceUnit { Input = source, SourceContentSha256 = string.Empty };
        // Why ParseErrors (not Debug.Assert): ProjectRelativePath crosses a process boundary via
        // JSON, and the worker is built without a DEBUG define so Conditional Asserts are stripped.
        if (string.IsNullOrEmpty(source.ProjectRelativePath)
            || source.ProjectRelativePath.IndexOf('\\') >= 0
            || source.ProjectRelativePath.IndexOf('"') >= 0)
        {
            unit.ParseErrors.Add(
                "Invalid projectRelativePath: must be a non-empty forward-slash path without quotes.");
            return unit;
        }

        (string sourceText, string sourceContentSha256, string readError) = TryReadSourceText(source.SourcePath);
        if (readError != null)
        {
            unit.ParseErrors.Add(readError);
            return unit;
        }

        unit.SourceContentSha256 = sourceContentSha256;
        (SyntaxTree syntaxTree, CompilationUnitSyntax plainRoot) = WorkerSourceAnnotator.ParseAndAnnotateSource(
            sourceText,
            parseOptions,
            source.SourcePath,
            unit.ParseErrors);
        unit.SyntaxTree = syntaxTree;
        unit.Root = syntaxTree.GetCompilationUnitRoot();
        unit.PlainRoot = plainRoot;
        return unit;
    }

    private static (string SourceText, string SourceContentSha256, string ReadError) TryReadSourceText(
        string sourcePath)
    {
        try
        {
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            string sourceContentSha256 = ComputeSourceContentSha256(sourceBytes);
            using MemoryStream memoryStream = new MemoryStream(sourceBytes, writable: false);
            using StreamReader reader = new StreamReader(
                memoryStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return (reader.ReadToEnd(), sourceContentSha256, null);
        }
        catch (Exception exception)
        {
            return (null, null, "Failed to read sourcePath: " + exception.Message);
        }
    }

    // Keep in sync with HotReloadAppliedSourceLedger.ComputeContentHash (lowercase hex SHA-256).
    private static string ComputeSourceContentSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (int index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2"));
        }

        return builder.ToString();
    }
}
