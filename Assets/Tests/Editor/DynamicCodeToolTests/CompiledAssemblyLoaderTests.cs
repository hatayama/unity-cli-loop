using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
    [TestFixture]
    public class CompiledAssemblyLoaderTests
    {
        private IPreloadAssemblySecurityValidator _previousValidator;
        private string _tempDirectoryPath;

        [SetUp]
        public void SetUp()
        {
            _previousValidator = PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(null);
            _tempDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"CompiledAssemblyLoaderTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectoryPath);
        }

        [TearDown]
        public void TearDown()
        {
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(_previousValidator);
            if (Directory.Exists(_tempDirectoryPath))
            {
                Directory.Delete(_tempDirectoryPath, true);
            }
        }

        [Test]
        public void Load_WhenCustomValidatorIsRegistered_ShouldStillRunMetadataFallbackValidation()
        {
            byte[] assemblyBytes = BuildAssemblyBytes(
                "using System.IO; public class DangerousMetadataType { public FileInfo DangerousField; public int Execute() { return 1; } }");
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(new AllowAllPreloadAssemblySecurityValidator());

            CompiledAssemblyLoadResult result = CompiledAssemblyLoader.Load(
                DynamicCodeSecurityLevel.Restricted,
                assemblyBytes);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.SecurityViolations.Any(violation => violation.ApiName == "System.IO.FileInfo"),
                Is.True,
                "Metadata fallback validation should keep rejecting dangerous field signatures even when a custom validator is registered.");
        }

        [Test]
        public void Load_WhenOverrideValidatorIsRegistered_ShouldStillApplyMandatoryPostLoadValidation()
        {
            byte[] assemblyBytes = BuildAssemblyBytes(
                "using System.IO; public class DangerousMetadataType { public FileInfo DangerousField; public int Execute() { return 1; } }");
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(new AllowAllOverridePreloadAssemblySecurityValidator());

            CompiledAssemblyLoadResult result = CompiledAssemblyLoader.Load(
                DynamicCodeSecurityLevel.Restricted,
                assemblyBytes);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.SecurityViolations.Any(violation => violation.ApiName == "System.IO.FileInfo"),
                Is.True);
        }

        private byte[] BuildAssemblyBytes(string source)
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            string sourcePath = Path.Combine(_tempDirectoryPath, "DangerousMetadataType.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "DangerousMetadataType.dll");
            string responsePath = Path.Combine(_tempDirectoryPath, "DangerousMetadataType.rsp");

            File.WriteAllText(sourcePath, source);
            WriteCompilerResponseFile(responsePath, sourcePath, dllPath, references);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = externalCompilerPaths.DotnetHostPath,
                Arguments = $"\"{externalCompilerPaths.CompilerDllPath}\" @\"{responsePath}\"",
                WorkingDirectory = _tempDirectoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = ProcessStartHelper.TryStart(startInfo);
            Assert.That(process, Is.Not.Null, "The external C# compiler should start for loader tests");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.That(process.ExitCode, Is.EqualTo(0), $"{stdout}\n{stderr}");
            return File.ReadAllBytes(dllPath);
        }

        private static void WriteCompilerResponseFile(
            string responsePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references)
        {
            List<string> lines = new List<string>
            {
                "-nologo",
                "-target:library",
                $"-out:\"{dllPath}\""
            };

            foreach (string reference in references)
            {
                lines.Add($"-r:\"{reference}\"");
            }

            lines.Add($"\"{sourcePath}\"");
            File.WriteAllLines(responsePath, lines);
        }

        private sealed class AllowAllPreloadAssemblySecurityValidator : IPreloadAssemblySecurityValidator
        {
            public SecurityValidationResult Validate(byte[] assemblyBytes)
            {
                return new SecurityValidationResult
                {
                    IsValid = true,
                    Violations = new List<SecurityViolation>(),
                    CompilationErrors = new List<string>()
                };
            }
        }

        private sealed class AllowAllOverridePreloadAssemblySecurityValidator :
            IPreloadAssemblySecurityValidator,
            IOverrideDefaultPreloadAssemblySecurityValidation
        {
            public SecurityValidationResult Validate(byte[] assemblyBytes)
            {
                return new SecurityValidationResult
                {
                    IsValid = true,
                    Violations = new List<SecurityViolation>(),
                    CompilationErrors = new List<string>()
                };
            }
        }
    }
}
