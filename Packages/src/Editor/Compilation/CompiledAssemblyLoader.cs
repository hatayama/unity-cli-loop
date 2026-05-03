using System.Collections.Generic;
using System.Diagnostics;
using Assembly = System.Reflection.Assembly;
using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class CompiledAssemblyLoader
    {
        public static CompiledAssemblyLoadResult Load(
            DynamicCodeSecurityLevel securityLevel,
            byte[] assemblyBytes)
        {
            Debug.Assert(assemblyBytes != null, "assemblyBytes must not be null");

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (securityLevel == DynamicCodeSecurityLevel.Restricted)
            {
                SecurityValidationResult preloadValidationResult = ValidateBeforeAssemblyLoad(assemblyBytes);
                if (!preloadValidationResult.IsValid)
                {
                    stopwatch.Stop();
                    return new CompiledAssemblyLoadResult(
                        false,
                        null,
                        preloadValidationResult.Violations,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            Assembly compiledAssembly = Assembly.Load(assemblyBytes);
            if (securityLevel == DynamicCodeSecurityLevel.Restricted)
            {
                IlSecurityValidator postLoadValidator = new IlSecurityValidator();
                SecurityValidationResult postLoadValidationResult = postLoadValidator.Validate(compiledAssembly);
                if (!postLoadValidationResult.IsValid)
                {
                    stopwatch.Stop();
                    return new CompiledAssemblyLoadResult(
                        false,
                        null,
                        postLoadValidationResult.Violations,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            stopwatch.Stop();
            return new CompiledAssemblyLoadResult(
                true,
                compiledAssembly,
                new List<SecurityViolation>(),
                stopwatch.Elapsed.TotalMilliseconds);
        }

        private static SecurityValidationResult ValidateBeforeAssemblyLoad(byte[] assemblyBytes)
        {
            SecurityValidationResult aggregatedResult = new SecurityValidationResult
            {
                IsValid = true,
                Violations = new List<SecurityViolation>(),
                CompilationErrors = new List<string>()
            };
            bool shouldRunMetadataFallback = true;

            IPreloadAssemblySecurityValidator registeredValidator;
            if (PreloadAssemblySecurityValidatorRegistry.TryGetValidator(out registeredValidator))
            {
                MergeValidationResult(aggregatedResult, registeredValidator.Validate(assemblyBytes));
                shouldRunMetadataFallback = registeredValidator is not IOverrideDefaultPreloadAssemblySecurityValidation;
            }

            if (shouldRunMetadataFallback)
            {
                SystemReflectionMetadataPreloadValidator fallbackValidator = new SystemReflectionMetadataPreloadValidator();
                MergeValidationResult(aggregatedResult, fallbackValidator.Validate(assemblyBytes));
            }

            PreloadIlSecurityValidator ilValidator = new PreloadIlSecurityValidator();
            MergeValidationResult(aggregatedResult, ilValidator.Validate(assemblyBytes));

            aggregatedResult.IsValid = aggregatedResult.Violations.Count == 0 &&
                                       aggregatedResult.CompilationErrors.Count == 0;
            return aggregatedResult;
        }

        private static void MergeValidationResult(
            SecurityValidationResult target,
            SecurityValidationResult source)
        {
            if (source == null)
            {
                return;
            }

            HashSet<string> seenViolations = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (SecurityViolation violation in target.Violations)
            {
                seenViolations.Add(CreateViolationKey(violation));
            }

            if (source.Violations != null)
            {
                foreach (SecurityViolation violation in source.Violations)
                {
                    if (seenViolations.Add(CreateViolationKey(violation)))
                    {
                        target.Violations.Add(violation);
                    }
                }
            }

            if (source.CompilationErrors == null)
            {
                return;
            }

            foreach (string error in source.CompilationErrors)
            {
                if (!target.CompilationErrors.Contains(error))
                {
                    target.CompilationErrors.Add(error);
                }
            }
        }

        private static string CreateViolationKey(SecurityViolation violation)
        {
            return $"{violation.Location}|{violation.ApiName}|{violation.Message}";
        }
    }
}
