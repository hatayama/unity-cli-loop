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

internal static class TypeEmitPlanner
{
    internal static List<TypeEmitState> QueueAllTypeEmitStates(
            WorkerSourceUnit sourceUnit,
            WorkerTypeHome home,
            WorkerInput input,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            List<ShimTypeBuilder> shimTypes,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog,
            AddedPropertyCatalog addedPropertyCatalog,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<string> declarationDriftWarnings,
            List<WorkerRemovedMember> removedMembers,
            List<WorkerRemovedMethodSignature> removedMethodSignatures,
            ShimNameAllocator shimNames)
    {
        CompilationUnitSyntax root = sourceUnit.BindingRoot;
        SemanticModel semanticModel = sourceUnit.SemanticModel;
        BaselineSnapshotState baseline = sourceUnit.Baseline;
        List<TypeEmitState> typeEmitStates = new List<TypeEmitState>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            INamedTypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (typeSymbol == null)
            {
                continue;
            }

            string typeMetadataNameFromSyntax = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);

            TypeEmitState typeState = new TypeEmitState
            {
                SourceUnit = sourceUnit,
                TypeDeclaration = typeDeclaration,
                TypeSymbol = typeSymbol,
                TypeMetadataNameFromSyntax = typeMetadataNameFromSyntax,
                TargetAssembly = home.AssemblySymbol,
                AddedEvents = new AddedEventLookup(home, sourceUnit, semanticModel)
            };

            // Why the counterpart is resolved before anything is classified: every stage below
            // asks which members the assembly serving this type already holds, and a type a
            // retained artifact serves has to answer that the way a compiled one does - it is
            // the same question, asked of the assembly the domain really loaded the type from.
            // Why the artifact is only consulted after the patch target: a type the target
            // already holds is compiled, and a record left over from an earlier reload must not
            // take the classification of it away from the assembly the request named.
            typeState.CompiledType = home.FindCompiledType(typeSymbol)
                ?? RetainedBodyEditHome.Adopt(typeState, semanticModel);

            AddedPropertyClassifier.ClassifyAddedProperties(
                typeState,
                semanticModel,
                home,
                input,
                baseline,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedPropertyCatalog,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                shimNames);
            typeState.AddedMemberAccess = new AddedMemberAccessLookup(
                typeSymbol,
                typeState.CompiledType,
                addedPropertyCatalog);

            // Existing property setters/init and all indexer accessors with bodies stay Skipped.
            // Added properties were classified above and must not receive duplicate skip rows.
            (Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
                Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap) =
                baseline.GetAccessorBaselineMaps();
            UnsupportedMemberSkipCollector.AppendExplicitAccessorSkips(
                sourceUnit.Input.ProjectRelativePath,
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotPropertyMap,
                snapshotIndexerMap,
                plainCurrentPropertyMap,
                plainCurrentIndexerMap,
                addedMethodCatalog,
                addedPropertyCatalog);
            (Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
                Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
                Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap,
                Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap) =
                baseline.GetUnsupportedMemberBaselineMaps();

            // Adopt sets the home assembly only for a type a retained artifact serves; a type the
            // patch target itself holds is resolved by FindCompiledType and leaves it null.
            bool servedByRetainedArtifact = typeState.HomeAssemblyName != null;
            UnsupportedMemberSkipCollector.AppendUnsupportedMemberKindSkips(
                sourceUnit.Input.ProjectRelativePath,
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotConstructorMap,
                snapshotOperatorMap,
                snapshotEventMap,
                plainCurrentConstructorMap,
                plainCurrentOperatorMap,
                plainCurrentEventMap,
                servedByRetainedArtifact);

            QueueTypeMethods(
                typeState,
                semanticModel,
                home,
                input,
                baseline.HasBaseline,
                baseline.SnapshotMethodMap,
                baseline.PlainCurrentMethodMap,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimNames);
            typeEmitStates.Add(typeState);
        }

        return typeEmitStates;
    }

    internal static void QueueTypeMethods(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        ShimNameAllocator shimNames)
    {
        INamedTypeSymbol compiledType = typeState.CompiledType;
        if (compiledType == null)
        {
            OrdinaryMethodShimTypes.SkipAllMethodsOnUncompiledType(typeState, semanticModel, skipped, addedMethodCatalog);
            return;
        }

        AddedFieldClassifier.ClassifyAddedFields(
            typeState,
            semanticModel,
            compiledType,
            home,
            addedFieldCatalog);

        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            OrdinaryMethodQueue.QueueOrdinaryMethod(
                methodDeclaration,
                typeState,
                semanticModel,
                compiledType,
                input,
                hasBaseline,
                snapshotMethodMap,
                plainCurrentMethodMap,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimNames);
        }
    }
}
