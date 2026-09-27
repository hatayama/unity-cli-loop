using System;
using System.IO;

using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Writes an isolated metadata fixture without adding source files to Unity's compilation.
    /// </summary>
    internal sealed class InternalsExposureTestImage : IDisposable
    {
        internal string ProjectRoot { get; }
        internal string DllPath { get; }
        internal AssemblyDefinition Definition { get; }
        internal HotReloadTypeHome Home { get; }

        internal InternalsExposureTestImage(Action<TypeDefinition> configure = null)
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assemblyName = "ExposedReferenceFixture_" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(ProjectRoot,
                HotReloadConstants.IntroducedTypeArtifactsRelativeDirectory, "ReferenceTests", assemblyName);
            Directory.CreateDirectory(directory);
            DllPath = Path.Combine(directory, assemblyName + ".dll");
            Definition = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName, ModuleKind.Dll);
            TypeDefinition candidate = new TypeDefinition("", "Candidate",
                TypeAttributes.Public | TypeAttributes.BeforeFieldInit, Definition.MainModule.TypeSystem.Object);
            Definition.MainModule.Types.Add(candidate);
            MethodDefinition constructor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                Definition.MainModule.TypeSystem.Void);
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
                Definition.MainModule.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes))));
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            candidate.Methods.Add(constructor);
            configure?.Invoke(candidate);
            Definition.Write(DllPath);
            Home = HotReloadTypeHome.ScriptAssemblies(assemblyName, DllPath);
        }

        internal static MethodDefinition AddReadMethod(TypeDefinition type, string name, MethodAttributes access)
        {
            MethodDefinition method = new MethodDefinition(name,
                access | MethodAttributes.Static | MethodAttributes.HideBySig, type.Module.TypeSystem.Int32);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 17));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
            return method;
        }

        public void Dispose()
        {
            Definition.Dispose();
            if (File.Exists(DllPath))
            {
                File.Delete(DllPath);
            }
        }
    }
}
