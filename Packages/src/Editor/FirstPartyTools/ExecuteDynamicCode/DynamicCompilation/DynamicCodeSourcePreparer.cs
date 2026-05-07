using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Code Source Preparer behavior for Unity CLI Loop.
    /// </summary>
    internal static class DynamicCodeSourcePreparer
    {
        private const string InterpolatedStringMarker = "$\"";
        private const string InterpolatedVerbatimStringMarker = "$@\"";
        private const string VerbatimInterpolatedStringMarker = "@$\"";

        public static PreparedDynamicCode PrepareWithoutLiteralHoisting(
            string source,
            string namespaceName,
            string className)
        {
            return Prepare(source, namespaceName, className, false);
        }

        public static PreparedDynamicCode Prepare(
            string source,
            string namespaceName,
            string className)
        {
            return Prepare(source, namespaceName, className, true);
        }

        private static PreparedDynamicCode Prepare(
            string source,
            string namespaceName,
            string className,
            bool enableLiteralHoisting)
        {
            SourceShapeResult shape = SourceShaper.Analyze(source);

            if ((shape.HasNamespaceDeclaration || shape.HasTypeDeclaration) && !shape.HasTopLevelStatements)
            {
                return new PreparedDynamicCode(source, false, new List<HoistedLiteralBinding>());
            }

            if ((shape.HasNamespaceDeclaration || shape.HasTypeDeclaration) && shape.HasTopLevelStatements)
            {
                return new PreparedDynamicCode(null, false, new List<HoistedLiteralBinding>());
            }

            string body = shape.TopLevelBodyBuilder.ToString().TrimEnd();
            bool hasReturn = TopLevelReturnDetector.HasTopLevelReturn(body);
            if (!hasReturn)
            {
                body = string.IsNullOrWhiteSpace(body)
                    ? "return null;"
                    : body + "\nreturn null;";
            }

            HoistedLiteralRewriteResult hoistedResult = !enableLiteralHoisting || ShouldSkipLiteralHoisting(body)
                ? new HoistedLiteralRewriteResult(body, new List<HoistedLiteralBinding>(), new List<string>())
                : DynamicCodeLiteralHoister.Rewrite(body);
            string preparedSource = WrapperTemplate.Build(
                shape.UsingDirectives,
                namespaceName,
                className,
                hoistedResult.RewrittenSource,
                hoistedResult.DeclarationLines);

            return new PreparedDynamicCode(preparedSource, true, hoistedResult.Bindings);
        }

        private static bool ShouldSkipLiteralHoisting(string body)
        {
            return body.Contains(InterpolatedStringMarker)
                || body.Contains(InterpolatedVerbatimStringMarker)
                || body.Contains(VerbatimInterpolatedStringMarker)
                || ContainsInterpolatedRawStringMarker(body);
        }

        private static bool ContainsInterpolatedRawStringMarker(string body)
        {
            int quoteIndex = body.IndexOf("\"\"\"", StringComparison.Ordinal);
            while (quoteIndex >= 0)
            {
                int dollarCount = 0;
                int markerIndex = quoteIndex - 1;
                while (markerIndex >= 0 && body[markerIndex] == '$')
                {
                    dollarCount++;
                    markerIndex--;
                }

                if (dollarCount > 0)
                {
                    return true;
                }

                quoteIndex = body.IndexOf("\"\"\"", quoteIndex + 3, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
