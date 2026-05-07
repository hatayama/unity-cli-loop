
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides External Compiler Paths behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class ExternalCompilerPaths
    {
        public string EditorContentsPath { get; }

        public string ScriptingRootPath { get; }

        public string DotnetHostPath { get; }

        public string CompilerDllPath { get; }

        public string CompilerRuntimeConfigPath { get; }

        public string CompilerDepsFilePath { get; }

        public string CodeAnalysisDllPath { get; }

        public string CodeAnalysisCSharpDllPath { get; }

        public string NetCoreRuntimeSharedDirectoryPath { get; }

        public ExternalCompilerPaths(
            string editorContentsPath,
            string scriptingRootPath,
            string dotnetHostPath,
            string compilerDllPath,
            string compilerRuntimeConfigPath,
            string compilerDepsFilePath,
            string codeAnalysisDllPath,
            string codeAnalysisCSharpDllPath,
            string netCoreRuntimeSharedDirectoryPath)
        {
            EditorContentsPath = editorContentsPath;
            ScriptingRootPath = scriptingRootPath;
            DotnetHostPath = dotnetHostPath;
            CompilerDllPath = compilerDllPath;
            CompilerRuntimeConfigPath = compilerRuntimeConfigPath;
            CompilerDepsFilePath = compilerDepsFilePath;
            CodeAnalysisDllPath = codeAnalysisDllPath;
            CodeAnalysisCSharpDllPath = codeAnalysisCSharpDllPath;
            NetCoreRuntimeSharedDirectoryPath = netCoreRuntimeSharedDirectoryPath;
        }
    }
}
