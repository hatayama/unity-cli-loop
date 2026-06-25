using System;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Supplies the official CA1502 threshold through Roslyn analyzer additional files.
    /// </summary>
    public sealed class CodeMetricsAdditionalText : AdditionalText
    {
        private readonly string? _filePath;
        private readonly SourceText? _sourceText;

        private CodeMetricsAdditionalText(string path, string? content, string? filePath)
        {
            Path = path;
            _filePath = filePath;
            _sourceText = content == null ? null : SourceText.From(content);
        }

        public override string Path { get; }

        public static CodeMetricsAdditionalText FromThreshold(int maxComplexity)
        {
            return new CodeMetricsAdditionalText(
                "CodeMetricsConfig.txt",
                $"CA1502: {maxComplexity}\n",
                filePath: null);
        }

        public static CodeMetricsAdditionalText FromFile(string filePath)
        {
            return new CodeMetricsAdditionalText(
                filePath,
                content: null,
                filePath);
        }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            if (_sourceText != null)
            {
                return _sourceText;
            }

            string filePath = _filePath ?? throw new InvalidOperationException("A file-backed CodeMetricsConfig requires a file path.");
            return SourceText.From(File.ReadAllText(filePath));
        }
    }
}
