using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Baseline maps from a parsed file: each member declaration of a kind, indexed by the syntax key
// WorkerSyntaxIndex builds for it. Kept apart from the key building itself so that neither file
// has to be read to change the other.
internal static class WorkerSyntaxMemberMaps
{
    internal static Dictionary<string, MethodDeclarationSyntax> BuildSyntaxMethodMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MethodDeclarationSyntax> map = new Dictionary<string, MethodDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MethodDeclarationSyntax methodDeclaration in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                string key = WorkerSyntaxIndex.BuildSyntaxMethodKey(typeMetadataName, methodDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = methodDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, VariableDeclaratorSyntax> BuildSyntaxFieldMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, VariableDeclaratorSyntax> map =
            new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (FieldDeclarationSyntax fieldDeclaration in typeDeclaration.Members
                .OfType<FieldDeclarationSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
                {
                    string key = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                    if (map.ContainsKey(key))
                    {
                        return null;
                    }

                    map[key] = variable;
                }
            }
        }

        return map;
    }

    internal static Dictionary<string, PropertyDeclarationSyntax> BuildSyntaxPropertyMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, PropertyDeclarationSyntax> map = new Dictionary<string, PropertyDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (PropertyDeclarationSyntax propertyDeclaration in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                string key = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataName, propertyDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = propertyDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, IndexerDeclarationSyntax> BuildSyntaxIndexerMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, IndexerDeclarationSyntax> map = new Dictionary<string, IndexerDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (IndexerDeclarationSyntax indexerDeclaration in typeDeclaration.Members.OfType<IndexerDeclarationSyntax>())
            {
                string key = WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataName, indexerDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = indexerDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, ConstructorDeclarationSyntax> BuildSyntaxConstructorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, ConstructorDeclarationSyntax> map =
            new Dictionary<string, ConstructorDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>())
            {
                string key = WorkerSyntaxIndex.BuildSyntaxConstructorKey(typeMetadataName, constructorDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = constructorDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, MemberDeclarationSyntax> BuildSyntaxOperatorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MemberDeclarationSyntax> map =
            new Dictionary<string, MemberDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                string key = WorkerSyntaxIndex.TryBuildSyntaxOperatorMemberKey(typeMetadataName, member);
                if (key == null)
                {
                    continue;
                }

                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = member;
            }
        }

        return map;
    }
    internal static Dictionary<string, EventDeclarationSyntax> BuildSyntaxEventMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, EventDeclarationSyntax> map =
            new Dictionary<string, EventDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (EventDeclarationSyntax eventDeclaration in typeDeclaration.Members
                .OfType<EventDeclarationSyntax>())
            {
                string key = WorkerSyntaxIndex.BuildSyntaxEventKey(typeMetadataName, eventDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = eventDeclaration;
            }
        }

        return map;
    }

    internal static Dictionary<string, VariableDeclaratorSyntax> BuildSyntaxEventFieldMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, VariableDeclaratorSyntax> map =
            new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (EventFieldDeclarationSyntax eventFieldDeclaration in typeDeclaration.Members
                .OfType<EventFieldDeclarationSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in eventFieldDeclaration.Declaration.Variables)
                {
                    // Why field key format: kind-change identity is type metadata + "::" + name,
                    // the same shape BuildSyntaxFieldKey already uses.
                    string key = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                    if (map.ContainsKey(key))
                    {
                        return null;
                    }

                    map[key] = variable;
                }
            }
        }

        return map;
    }
}
