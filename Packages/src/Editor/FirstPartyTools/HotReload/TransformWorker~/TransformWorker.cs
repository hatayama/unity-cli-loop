// Hot-reload transform worker: parse + semantic analysis of the edited C# files of one
// compilation assembly, emit static shim method sources (no Prefix wrappers) plus a per-method
// manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

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

public static class TransformWorkerProgram
{
    private const string RoslynDirectorySidecarFileName = "roslyn-directory.txt";

    // SyntaxAnnotation kind for original-source 1-based lines; survive rewriter + NormalizeWhitespace
    // so Emit can inject #line directives after formatting.
    internal const string UloopLineAnnotationKind = "uloop-line";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: TransformWorker <input-json-path> <output-json-path>");
            return 2;
        }

        string roslynDirectoryPath = ReadRoslynDirectorySidecar();
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            string candidatePath = Path.Combine(roslynDirectoryPath, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }

            return null;
        };

        return RunTransform(args[0], args[1]);
    }

    private static string ReadRoslynDirectorySidecar()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string sidecarPath = Path.Combine(baseDirectory, RoslynDirectorySidecarFileName);
        if (!File.Exists(sidecarPath))
        {
            throw new InvalidOperationException(
                "Roslyn directory sidecar not found next to worker.dll: " + sidecarPath);
        }

        string roslynDirectoryPath = File.ReadAllText(sidecarPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).Trim();
        if (string.IsNullOrEmpty(roslynDirectoryPath) || !Directory.Exists(roslynDirectoryPath))
        {
            throw new InvalidOperationException("Roslyn directory from sidecar is missing: " + roslynDirectoryPath);
        }

        return roslynDirectoryPath;
    }

    private static int RunTransform(string inputJsonPath, string outputJsonPath)
    {
        WorkerInput input = ReadInput(inputJsonPath);
        WorkerOutput invalidSources = TryCreateInvalidSourcesOutput(input);
        WorkerOutput output = invalidSources ?? WorkerGroupPipeline.Run(input);
        WriteOutput(outputJsonPath, output);
        return 0;
    }

    // Why a run-level failure output (not a throw): the sources array crosses a process boundary
    // via JSON, so it is untrusted input rather than a broken internal contract. Both failures
    // below belong to no single source, which is what the run-level ParseErrors channel is for.
    private static WorkerOutput TryCreateInvalidSourcesOutput(WorkerInput input)
    {
        if (input.Sources.Length == 0)
        {
            return CreateRunFailureOutput("sources must contain at least one edited source.");
        }

        HashSet<string> projectRelativePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerSourceInput source in input.Sources)
        {
            if (source == null)
            {
                return CreateRunFailureOutput("sources must not contain a null entry.");
            }

            if (!projectRelativePaths.Add(source.ProjectRelativePath ?? string.Empty))
            {
                return CreateRunFailureOutput(
                    "sources must not repeat a projectRelativePath within one run.");
            }
        }

        return null;
    }

    private static WorkerOutput CreateRunFailureOutput(string parseError)
    {
        return new WorkerOutput
        {
            ShimSource = string.Empty,
            Entries = Array.Empty<WorkerEntry>(),
            Skipped = Array.Empty<WorkerSkipped>(),
            Files = Array.Empty<WorkerFileOutput>(),
            ParseErrors = new[] { parseError }
        };
    }

    private static WorkerInput ReadInput(string inputJsonPath)
    {
        byte[] bytes = File.ReadAllBytes(inputJsonPath);
        WorkerInput input = JsonSerializer.Deserialize<WorkerInput>(bytes, JsonOptions);
        if (input == null)
        {
            throw new InvalidOperationException("Failed to deserialize worker input JSON.");
        }

        input.Sources ??= Array.Empty<WorkerSourceInput>();
        input.Defines ??= Array.Empty<string>();
        input.ReferencePaths ??= Array.Empty<string>();
        input.ExcludedMethodKeys ??= Array.Empty<string>();
        input.ExcludedAddedMethodKeys ??= Array.Empty<string>();
        input.AssemblySourcePaths ??= Array.Empty<string>();
        input.ChangedSiblingSourcePaths ??= Array.Empty<string>();
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
    }

    internal static IEnumerable<TypeDeclarationSyntax> EnumerateTypeDeclarations(CompilationUnitSyntax root)
    {
        // Why interfaces: a new interface (including default methods) is absent from the compiled
        // assembly; enumerating it registers the type syntax key so the strip rewriter can drop
        // it instead of firing a false outside-body drift warning.
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(static typeDeclaration =>
                typeDeclaration is ClassDeclarationSyntax
                || typeDeclaration is StructDeclarationSyntax
                || typeDeclaration is RecordDeclarationSyntax
                || typeDeclaration is InterfaceDeclarationSyntax);
    }
}
