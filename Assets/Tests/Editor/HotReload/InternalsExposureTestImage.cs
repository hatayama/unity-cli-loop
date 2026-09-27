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

        // Why a resolver can be supplied: an image whose metadata needs another assembly while it
        // is written, such as an enum-typed constant, has to find that assembly now, even when the
        // test later makes it unresolvable for the copy under test.
        internal InternalsExposureTestImage(
            Action<TypeDefinition> configure = null,
            IAssemblyResolver writeResolver = null)
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assemblyName = "ExposedReferenceFixture_" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(ProjectRoot,
                HotReloadConstants.IntroducedTypeArtifactsRelativeDirectory, "ReferenceTests", assemblyName);
            Directory.CreateDirectory(directory);
            DllPath = Path.Combine(directory, assemblyName + ".dll");
            Definition = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
                assemblyName,
                new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = writeResolver });
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

        // Why an enum-typed constant: Cecil resolves the enum a constant is typed with to find its
        // underlying type while it writes a module, so once the enum's assembly is gone, writing a
        // copy of this image fails the way a copy fails when a reference it needs cannot be found.
        internal static InternalsExposureTestImage CreateWithConstantOfMissingEnum(
            string externalName,
            Action<TypeDefinition> configure = null)
        {
            string externalDirectory = Path.Combine(Application.temporaryCachePath, externalName);
            Directory.CreateDirectory(externalDirectory);
            try
            {
                using DefaultAssemblyResolver writeResolver = new DefaultAssemblyResolver();
                writeResolver.AddSearchDirectory(externalDirectory);
                using AssemblyDefinition external = AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition(externalName, new Version(1, 0, 0, 0)), externalName, ModuleKind.Dll);
                TypeDefinition externalKind = new TypeDefinition(
                    "", "ExternalKind", TypeAttributes.Public | TypeAttributes.Sealed,
                    external.MainModule.ImportReference(typeof(Enum)));
                externalKind.Fields.Add(new FieldDefinition(
                    "value__",
                    FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                    external.MainModule.TypeSystem.Int32));
                external.MainModule.Types.Add(externalKind);
                external.Write(Path.Combine(externalDirectory, externalName + ".dll"));
                return new InternalsExposureTestImage(
                    candidate =>
                    {
                        configure?.Invoke(candidate);
                        candidate.Fields.Add(new FieldDefinition(
                            "Default",
                            FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.Literal
                            | FieldAttributes.HasDefault,
                            candidate.Module.ImportReference(externalKind)) { Constant = 1 });
                    },
                    writeResolver);
            }
            finally
            {
                Directory.Delete(externalDirectory, true);
            }
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
