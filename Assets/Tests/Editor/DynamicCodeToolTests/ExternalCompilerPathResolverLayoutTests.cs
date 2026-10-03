using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the external compiler path resolver handles missing or partial editor layouts, orders SDK
    /// directories, and falls back to the NetCoreRuntime pair when the DotNetSdk pair is incomplete, on
    /// temporary directory trees.
    /// </summary>
    public sealed class ExternalCompilerPathResolverLayoutTests
    {
        private string _tempDirectoryPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"ExternalCompilerPathResolverLayoutTests_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectoryPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectoryPath))
            {
                Directory.Delete(_tempDirectoryPath, true);
            }
        }

        /// <summary>
        /// Verifies an empty contents path has no scripting root.
        /// </summary>
        [Test]
        public void ResolveScriptingRootPath_WithoutAContentsPath_ReturnsNull()
        {
            Assert.That(ExternalCompilerPathResolver.ResolveScriptingRootPath(null), Is.Null);
        }

        /// <summary>
        /// Verifies a contents path that does not exist has no scripting root.
        /// </summary>
        [Test]
        public void ResolveScriptingRootPath_WhenTheContentsPathDoesNotExist_ReturnsNull()
        {
            string missingPath = Path.Combine(_tempDirectoryPath, "Missing");

            Assert.That(ExternalCompilerPathResolver.ResolveScriptingRootPath(missingPath), Is.Null);
        }

        /// <summary>
        /// Verifies a compiler with neither a NetCoreRuntime nor a DotNetSdk shared runtime beside it is not a
        /// usable layout.
        /// </summary>
        [Test]
        public void ResolveScriptingRootPath_WithACompilerButNoRuntime_ReturnsNull()
        {
            string contentsPath = CreateDirectory("Contents");
            CreateFile(Path.Combine("Contents", "DotNetSdkRoslyn", "csc.dll"));

            Assert.That(ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath), Is.Null);
        }

        /// <summary>
        /// Verifies a missing shared framework root yields no runtime directory.
        /// </summary>
        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenTheRootDoesNotExist_ReturnsNull()
        {
            string missingPath = Path.Combine(_tempDirectoryPath, "Microsoft.NETCore.App");

            Assert.That(ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(missingPath), Is.Null);
        }

        /// <summary>
        /// Verifies an empty shared framework root yields no runtime directory.
        /// </summary>
        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenTheRootIsEmpty_ReturnsNull()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");

            Assert.That(ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath), Is.Null);
        }

        /// <summary>
        /// Verifies an empty scripting root has no compiler directory.
        /// </summary>
        [Test]
        public void ResolveCompilerDirectoryPath_WithoutAScriptingRoot_ReturnsNull()
        {
            Assert.That(ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(null), Is.Null);
        }

        /// <summary>
        /// Verifies SDK directories without a Roslyn compiler inside are not compiler directories.
        /// </summary>
        [Test]
        public void ResolveCompilerDirectoryPath_WhenNoSdkHoldsACompiler_ReturnsNull()
        {
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.100"));

            Assert.That(ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath), Is.Null);
        }

        /// <summary>
        /// Verifies a versioned SDK directory wins over a non-version name even when the version is not the
        /// highest one present.
        /// </summary>
        [Test]
        public void ResolveCompilerDirectoryPath_WithVersionAndNonVersionSdks_PrefersTheHighestVersionWithACompiler()
        {
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "9.0.100"));
            string expectedCompilerPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.100", "Roslyn", "bincore"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "preview", "Roslyn", "bincore"));

            Assert.That(
                ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath),
                Is.EqualTo(expectedCompilerPath));
        }

        /// <summary>
        /// Verifies non-version SDK names are tried in descending ordinal order.
        /// </summary>
        [Test]
        public void ResolveCompilerDirectoryPath_WithOnlyNonVersionSdks_PrefersTheLastNameInOrdinalOrder()
        {
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "alpha", "Roslyn", "bincore"));
            string expectedCompilerPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "beta", "Roslyn", "bincore"));

            Assert.That(
                ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath),
                Is.EqualTo(expectedCompilerPath));
        }

        /// <summary>
        /// Verifies a missing part of the layout makes its kind unknown.
        /// </summary>
        [Test]
        public void ResolveCompilerLayoutKind_WithAMissingPath_IsUnknown()
        {
            Assert.That(
                ExternalCompilerPathResolver.ResolveCompilerLayoutKind(_tempDirectoryPath, null, _tempDirectoryPath),
                Is.EqualTo(ExternalCompilerLayoutKind.Unknown));
        }

        /// <summary>
        /// Verifies a scripting root found elsewhere under the contents is reported as scanned.
        /// </summary>
        [Test]
        public void ResolveCompilerLayoutKind_WithAScriptingRootElsewhere_IsScanned()
        {
            string contentsPath = CreateDirectory("Contents");
            string scriptingRootPath = CreateDirectory(Path.Combine("Contents", "Other", "Scripting"));
            string compilerDirectoryPath = Path.Combine(scriptingRootPath, "DotNetSdkRoslyn");

            Assert.That(
                ExternalCompilerPathResolver.ResolveCompilerLayoutKind(contentsPath, scriptingRootPath, compilerDirectoryPath),
                Is.EqualTo(ExternalCompilerLayoutKind.Scanned));
        }

        /// <summary>
        /// Verifies a compiler without a runtime config keeps the NetCoreRuntime pair even beside a complete
        /// DotNetSdk pair.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WithoutARuntimeConfig_KeepsTheNetCoreRuntimePair()
        {
            string scriptingRootPath = CreateNetCoreRuntime("8.0.21");
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "10.0.301", "Roslyn", "bincore"));
            CreateFile(Path.Combine("Scripting", "DotNetSdk", "dotnet"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "shared", "Microsoft.NETCore.App", "10.0.9"));

            AssertNetCoreRuntimePair(scriptingRootPath, compilerDirectoryPath, "8.0.21");
        }

        /// <summary>
        /// Verifies a runtime config whose version cannot be parsed keeps the NetCoreRuntime pair.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WithAnUnparsableRuntimeVersion_KeepsTheNetCoreRuntimePair()
        {
            string scriptingRootPath = CreateNetCoreRuntime("8.0.21");
            string compilerDirectoryPath = CreateSdkCompiler("10.0.301", "latest");
            CreateFile(Path.Combine("Scripting", "DotNetSdk", "dotnet"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "shared", "Microsoft.NETCore.App", "10.0.9"));

            AssertNetCoreRuntimePair(scriptingRootPath, compilerDirectoryPath, "8.0.21");
        }

        /// <summary>
        /// Verifies a too-old NetCoreRuntime is kept when no DotNetSdk root sits above the compiler.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WhenNoDotNetSdkRootHoldsTheCompiler_KeepsTheNetCoreRuntimePair()
        {
            string scriptingRootPath = CreateNetCoreRuntime("8.0.21");
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdkRoslyn"));
            WriteCscRuntimeConfig(compilerDirectoryPath, "10.0.9");

            AssertNetCoreRuntimePair(scriptingRootPath, compilerDirectoryPath, "8.0.21");
        }

        /// <summary>
        /// Verifies a too-old NetCoreRuntime is kept when the DotNetSdk root has no dotnet host.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WhenTheDotNetSdkHostIsMissing_KeepsTheNetCoreRuntimePair()
        {
            string scriptingRootPath = CreateNetCoreRuntime("8.0.21");
            string compilerDirectoryPath = CreateSdkCompiler("10.0.301", "10.0.9");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "shared", "Microsoft.NETCore.App", "10.0.9"));

            AssertNetCoreRuntimePair(scriptingRootPath, compilerDirectoryPath, "8.0.21");
        }

        /// <summary>
        /// Verifies a too-old NetCoreRuntime is kept when the DotNetSdk shared runtime is too old as well.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WhenTheDotNetSdkRuntimeIsTooOld_KeepsTheNetCoreRuntimePair()
        {
            string scriptingRootPath = CreateNetCoreRuntime("8.0.21");
            string compilerDirectoryPath = CreateSdkCompiler("10.0.301", "10.0.9");
            CreateFile(Path.Combine("Scripting", "DotNetSdk", "dotnet"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "shared", "Microsoft.NETCore.App", "9.0.5"));

            AssertNetCoreRuntimePair(scriptingRootPath, compilerDirectoryPath, "8.0.21");
        }

        /// <summary>
        /// Verifies a missing NetCoreRuntime shared root does not satisfy the compiler, so a complete DotNetSdk
        /// pair is used.
        /// </summary>
        [Test]
        public void ResolveRuntimePairing_WithoutANetCoreRuntimeSharedRoot_UsesTheDotNetSdkPair()
        {
            string scriptingRootPath = CreateDirectory("Scripting");
            string compilerDirectoryPath = CreateSdkCompiler("8.0.318", "8.0.21");
            string expectedHostPath = CreateFile(Path.Combine("Scripting", "DotNetSdk", "dotnet"));
            string expectedSharedPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "shared", "Microsoft.NETCore.App", "8.0.21"));

            ExternalCompilerRuntimePairing pairing = ExternalCompilerPathResolver.ResolveRuntimePairing(
                scriptingRootPath,
                compilerDirectoryPath,
                "dotnet");

            Assert.That(pairing.DotnetHostPath, Is.EqualTo(expectedHostPath));
            Assert.That(pairing.NetCoreRuntimeSharedDirectoryPath, Is.EqualTo(expectedSharedPath));
        }

        private void AssertNetCoreRuntimePair(string scriptingRootPath, string compilerDirectoryPath, string runtimeVersion)
        {
            ExternalCompilerRuntimePairing pairing = ExternalCompilerPathResolver.ResolveRuntimePairing(
                scriptingRootPath,
                compilerDirectoryPath,
                "dotnet");

            Assert.That(pairing.DotnetHostPath, Is.EqualTo(Path.Combine(scriptingRootPath, "NetCoreRuntime", "dotnet")));
            Assert.That(
                pairing.NetCoreRuntimeSharedDirectoryPath,
                Is.EqualTo(Path.Combine(scriptingRootPath, "NetCoreRuntime", "shared", "Microsoft.NETCore.App", runtimeVersion)));
        }

        private string CreateNetCoreRuntime(string runtimeVersion)
        {
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateFile(Path.Combine("Scripting", "NetCoreRuntime", "dotnet"));
            CreateDirectory(Path.Combine("Scripting", "NetCoreRuntime", "shared", "Microsoft.NETCore.App", runtimeVersion));
            return scriptingRootPath;
        }

        private string CreateSdkCompiler(string sdkVersion, string requiredRuntimeVersion)
        {
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", sdkVersion, "Roslyn", "bincore"));
            CreateFile(Path.Combine("Scripting", "DotNetSdk", "sdk", sdkVersion, "Roslyn", "bincore", "csc.dll"));
            WriteCscRuntimeConfig(compilerDirectoryPath, requiredRuntimeVersion);
            return compilerDirectoryPath;
        }

        private string CreateDirectory(string relativePath)
        {
            string directoryPath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        private string CreateFile(string relativePath)
        {
            string filePath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, string.Empty);
            return filePath;
        }

        private static void WriteCscRuntimeConfig(string compilerDirectoryPath, string frameworkVersion)
        {
            string json =
                "{ \"runtimeOptions\": { \"framework\": { \"name\": \"Microsoft.NETCore.App\", \"version\": \""
                + frameworkVersion
                + "\" } } }";
            File.WriteAllText(Path.Combine(compilerDirectoryPath, "csc.runtimeconfig.json"), json);
        }
    }
}
