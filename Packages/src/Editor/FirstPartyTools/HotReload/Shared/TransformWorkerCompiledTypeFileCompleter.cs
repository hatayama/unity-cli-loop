using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Completes the skipped reasons that name compiled types with the files declaring them, read
    /// from the debug data of the compiled assembly the run targets.
    /// </summary>
    // Why on the Editor side: the worker only binds the compiled assembly as metadata and never
    // reads its PDB, while the reader needs a file it can pass to the next reload.
    internal sealed class TransformWorkerCompiledTypeFileCompleter
    {
        internal void Complete(TransformWorkerInputDto input, TransformWorkerOutputDto output)
        {
            Dictionary<string, List<string>> filesByType = null;
            foreach (TransformWorkerSkippedDto skipped in output.skipped)
            {
                // Why the whole detail chain: a member whose own body split is recorded as
                // unavailable, and a member reading it is skipped with that reason as its detail.
                for (TransformWorkerReasonDto reason = skipped?.reason; reason != null; reason = reason.detail)
                {
                    if (reason.typeMetadataNames == null)
                    {
                        continue;
                    }

                    filesByType ??= ReadDeclaringFiles(input.targetTypesAssemblyPath);
                    reason.args = AppendPassTarget(reason.args, reason.typeMetadataNames, filesByType);
                }
            }
        }

        private static string[] AppendPassTarget(
            string[] args,
            string[] typeMetadataNames,
            Dictionary<string, List<string>> filesByType)
        {
            List<string> targets = new List<string>();
            foreach (string typeMetadataName in typeMetadataNames)
            {
                if (!filesByType.TryGetValue(typeMetadataName, out List<string> files) || files.Count == 0)
                {
                    targets.Add("the file that declares '" + typeMetadataName + "'");
                    continue;
                }

                foreach (string file in files)
                {
                    string quoted = "'" + file + "'";
                    if (!targets.Contains(quoted))
                    {
                        targets.Add(quoted);
                    }
                }
            }

            List<string> completed = new List<string>(args ?? Array.Empty<string>());
            completed.Add(string.Join(" and ", targets));
            return completed.ToArray();
        }

        // Keyed by the metadata name Cecil reports, which is the form the worker sends, so a nested
        // type needs no conversion here. A type is missing when the assembly or its PDB is.
        private static Dictionary<string, List<string>> ReadDeclaringFiles(string dllPath)
        {
            Dictionary<string, List<string>> filesByType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string pdbPath = string.IsNullOrEmpty(dllPath) ? null : Path.ChangeExtension(dllPath, ".pdb");
            if (pdbPath == null || !File.Exists(dllPath) || !File.Exists(pdbPath))
            {
                return filesByType;
            }

            // Why not let a read failure escape: the files only sharpen a hint, and a row naming the
            // types alone still tells the reader what to pass, so the reload must not fail on it.
            try
            {
                ReadDocumentsInto(dllPath, pdbPath, filesByType);
            }
            // InvalidOperationException covers Cecil's SymbolsNotMatchingException, thrown when the
            // PDB beside the assembly belongs to another build of it.
            catch (Exception exception) when (exception is IOException
                || exception is BadImageFormatException
                || exception is InvalidOperationException)
            {
                filesByType.Clear();
            }

            return filesByType;
        }

        private static void ReadDocumentsInto(string dllPath, string pdbPath, Dictionary<string, List<string>> filesByType)
        {
            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dllStream, readerParameters);
            foreach (TypeDefinition type in assembly.MainModule.GetTypes())
            {
                filesByType[type.FullName] = CollectDocumentPaths(type);
            }
        }

        private static List<string> CollectDocumentPaths(TypeDefinition type)
        {
            List<string> paths = new List<string>();
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody || method.DebugInformation == null || !method.DebugInformation.HasSequencePoints)
                {
                    continue;
                }

                foreach (SequencePoint sequencePoint in method.DebugInformation.SequencePoints)
                {
                    if (sequencePoint.IsHidden || sequencePoint.Document == null)
                    {
                        continue;
                    }

                    string path = ToProjectRelativePath(sequencePoint.Document.Url);
                    if (!paths.Contains(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            return paths;
        }

        // Why the current directory: the Editor runs with the project root as its working
        // directory, and a rooted document path outside it is still worth showing as it is.
        private static string ToProjectRelativePath(string documentUrl)
        {
            string path = HotReloadSourcePathNormalizer.ToForwardSlashes(documentUrl);
            if (!Path.IsPathRooted(path))
            {
                return path;
            }

            string root = HotReloadSourcePathNormalizer.ToForwardSlashes(Directory.GetCurrentDirectory()).TrimEnd('/') + "/";
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return path.StartsWith(root, comparison) ? path.Substring(root.Length) : path;
        }
    }
}
