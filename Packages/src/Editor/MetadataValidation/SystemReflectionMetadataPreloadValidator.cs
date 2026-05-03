using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Reads ECMA-335 metadata before Assembly.Load so load-time side effects never run before strict validation.
    /// </summary>
    public sealed class SystemReflectionMetadataPreloadValidator : IPreloadAssemblySecurityValidator
    {
        private const string ModuleInitializerAttributeName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

        public SecurityValidationResult Validate(byte[] assemblyBytes)
        {
            SecurityValidationResult result = new SecurityValidationResult
            {
                IsValid = true,
                Violations = new List<SecurityViolation>()
            };

            HashSet<string> seenViolations = new HashSet<string>(System.StringComparer.Ordinal);

            using MemoryStream stream = new MemoryStream(assemblyBytes, writable: false);
            using PEReader peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();

            this.ValidateTypeReferences(reader, result, seenViolations);
            this.ValidateTypeDefinitions(reader, result, seenViolations);
            this.ValidateMemberReferences(reader, result, seenViolations);
            this.ValidateCustomAttributes(reader, result, seenViolations);

            result.IsValid = result.Violations.Count == 0;
            return result;
        }

        private void ValidateTypeReferences(
            MetadataReader reader,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            foreach (TypeReferenceHandle handle in reader.TypeReferences)
            {
                string typeName = this.GetTypeReferenceFullName(reader, handle);
                if (!DangerousApiCatalog.IsDangerousType(typeName))
                {
                    continue;
                }

                this.AddViolation(
                    result,
                    seenViolations,
                    typeName,
                    $"Dangerous metadata reference detected before assembly load: {typeName}",
                    $"Metadata TypeReference contains '{typeName}'");
            }
        }

        private void ValidateTypeDefinitions(
            MetadataReader reader,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition = reader.GetTypeDefinition(handle);
                string declaringTypeName = this.GetTypeDefinitionFullName(reader, handle);

                if (!typeDefinition.BaseType.IsNil)
                {
                    string baseTypeName = this.GetEntityTypeName(reader, typeDefinition.BaseType);
                    if (DangerousApiCatalog.IsDangerousType(baseTypeName))
                    {
                        this.AddViolation(
                            result,
                            seenViolations,
                            baseTypeName,
                            $"Dangerous metadata inheritance detected before assembly load: {baseTypeName}",
                            $"Metadata base type '{baseTypeName}' exists on '{declaringTypeName}'");
                    }
                }

                foreach (InterfaceImplementationHandle interfaceHandle in typeDefinition.GetInterfaceImplementations())
                {
                    InterfaceImplementation interfaceImplementation = reader.GetInterfaceImplementation(interfaceHandle);
                    string interfaceTypeName = this.GetEntityTypeName(reader, interfaceImplementation.Interface);
                    if (!DangerousApiCatalog.IsDangerousType(interfaceTypeName))
                    {
                        continue;
                    }

                    this.AddViolation(
                        result,
                        seenViolations,
                        interfaceTypeName,
                        $"Dangerous metadata interface detected before assembly load: {interfaceTypeName}",
                        $"Metadata interface '{interfaceTypeName}' exists on '{declaringTypeName}'");
                }
            }
        }

        private void ValidateMemberReferences(
            MetadataReader reader,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            foreach (MemberReferenceHandle handle in reader.MemberReferences)
            {
                MemberReference memberReference = reader.GetMemberReference(handle);
                string parentTypeName = this.GetMemberReferenceParentTypeName(reader, memberReference.Parent);
                string memberName = reader.GetString(memberReference.Name);

                if (!DangerousApiCatalog.IsDangerousApi(parentTypeName, memberName))
                {
                    continue;
                }

                string apiName = $"{parentTypeName}.{memberName}";
                this.AddViolation(
                    result,
                    seenViolations,
                    apiName,
                    $"Dangerous metadata member detected before assembly load: {apiName}",
                    $"Metadata MemberReference contains '{apiName}'");
            }
        }

        private void ValidateCustomAttributes(
            MetadataReader reader,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            foreach (CustomAttributeHandle handle in reader.CustomAttributes)
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string attributeTypeName = this.GetCustomAttributeTypeName(reader, attribute.Constructor);
                if (attributeTypeName != ModuleInitializerAttributeName)
                {
                    continue;
                }

                this.AddViolation(
                    result,
                    seenViolations,
                    attributeTypeName,
                    $"Dangerous metadata attribute detected before assembly load: {attributeTypeName}",
                    $"Metadata CustomAttribute contains '{attributeTypeName}'");
            }
        }

        private string GetCustomAttributeTypeName(MetadataReader reader, EntityHandle constructorHandle)
        {
            if (constructorHandle.Kind == HandleKind.MemberReference)
            {
                MemberReference memberReference = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
                return this.GetMemberReferenceParentTypeName(reader, memberReference.Parent);
            }

            if (constructorHandle.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinition methodDefinition = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
                return this.GetTypeDefinitionFullName(reader, methodDefinition.GetDeclaringType());
            }

            return string.Empty;
        }

        private string GetMemberReferenceParentTypeName(MetadataReader reader, EntityHandle parentHandle)
        {
            if (parentHandle.Kind == HandleKind.TypeReference ||
                parentHandle.Kind == HandleKind.TypeDefinition ||
                parentHandle.Kind == HandleKind.TypeSpecification)
            {
                return this.GetEntityTypeName(reader, parentHandle);
            }

            return string.Empty;
        }

        private string GetEntityTypeName(MetadataReader reader, EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeReference)
            {
                return this.GetTypeReferenceFullName(reader, (TypeReferenceHandle)handle);
            }

            if (handle.Kind == HandleKind.TypeDefinition)
            {
                return this.GetTypeDefinitionFullName(reader, (TypeDefinitionHandle)handle);
            }

            if (handle.Kind == HandleKind.TypeSpecification)
            {
                return this.DecodeTypeSpecification(reader, (TypeSpecificationHandle)handle);
            }

            return string.Empty;
        }

        private string GetTypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle)
        {
            TypeReference typeReference = reader.GetTypeReference(handle);
            string typeName = reader.GetString(typeReference.Name);
            string namespaceName = reader.GetString(typeReference.Namespace);

            if (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                string parentTypeName = this.GetTypeReferenceFullName(reader, (TypeReferenceHandle)typeReference.ResolutionScope);
                return $"{parentTypeName}.{typeName}";
            }

            if (string.IsNullOrEmpty(namespaceName))
            {
                return typeName;
            }

            return $"{namespaceName}.{typeName}";
        }

        private string GetTypeDefinitionFullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(handle);
            string typeName = reader.GetString(typeDefinition.Name);
            TypeDefinitionHandle declaringTypeHandle = typeDefinition.GetDeclaringType();

            if (!declaringTypeHandle.IsNil)
            {
                string parentTypeName = this.GetTypeDefinitionFullName(reader, declaringTypeHandle);
                return $"{parentTypeName}.{typeName}";
            }

            string namespaceName = reader.GetString(typeDefinition.Namespace);
            if (string.IsNullOrEmpty(namespaceName))
            {
                return typeName;
            }

            return $"{namespaceName}.{typeName}";
        }

        private string DecodeTypeSpecification(MetadataReader reader, TypeSpecificationHandle handle)
        {
            TypeSpecification typeSpecification = reader.GetTypeSpecification(handle);
            MetadataTypeNameDecoder decoder = new MetadataTypeNameDecoder(reader, this);
            return typeSpecification.DecodeSignature(decoder, genericContext: null);
        }

        private void AddViolation(
            SecurityValidationResult result,
            HashSet<string> seenViolations,
            string apiName,
            string message,
            string description)
        {
            if (!seenViolations.Add($"{apiName}|{message}"))
            {
                return;
            }

            result.Violations.Add(new SecurityViolation
            {
                Type = SecurityViolationType.DangerousApiCall,
                ApiName = apiName,
                Message = message,
                Description = description,
                Location = "metadata"
            });
        }

        private sealed class MetadataTypeNameDecoder : ISignatureTypeProvider<string, object>
        {
            private readonly MetadataReader _reader;
            private readonly SystemReflectionMetadataPreloadValidator _validator;

            public MetadataTypeNameDecoder(MetadataReader reader, SystemReflectionMetadataPreloadValidator validator)
            {
                _reader = reader;
                _validator = validator;
            }

            public string GetArrayType(string elementType, ArrayShape shape)
            {
                return elementType;
            }

            public string GetByReferenceType(string elementType)
            {
                return elementType;
            }

            public string GetFunctionPointerType(MethodSignature<string> signature)
            {
                return string.Empty;
            }

            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            {
                return genericType;
            }

            public string GetGenericMethodParameter(object genericContext, int index)
            {
                return $"!!{index}";
            }

            public string GetGenericTypeParameter(object genericContext, int index)
            {
                return $"!{index}";
            }

            public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired)
            {
                return unmodifiedType;
            }

            public string GetPinnedType(string elementType)
            {
                return elementType;
            }

            public string GetPointerType(string elementType)
            {
                return elementType;
            }

            public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                return typeCode.ToString();
            }

            public string GetSZArrayType(string elementType)
            {
                return elementType;
            }

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                return _validator.GetTypeDefinitionFullName(reader, handle);
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                return _validator.GetTypeReferenceFullName(reader, handle);
            }

            public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            {
                return _validator.DecodeTypeSpecification(reader, handle);
            }
        }
    }
}
