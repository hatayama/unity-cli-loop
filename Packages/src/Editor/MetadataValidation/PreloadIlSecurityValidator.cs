using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Reads IL directly from PE metadata before Assembly.Load so restricted-mode validation
    /// never needs to materialize the assembly just to inspect method bodies.
    /// </summary>
    public sealed class PreloadIlSecurityValidator : IPreloadAssemblySecurityValidator
    {
        private const char DecodedTypeNameSeparator = '|';
        private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeTable(singleByte: true);
        private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeTable(singleByte: false);

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

            foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
            {
                this.ValidateMethod(peReader, reader, methodHandle, result, seenViolations);
            }

            result.IsValid = result.Violations.Count == 0;
            return result;
        }

        private void ValidateMethod(
            PEReader peReader,
            MetadataReader reader,
            MethodDefinitionHandle methodHandle,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            MethodDefinition methodDefinition = reader.GetMethodDefinition(methodHandle);
            string declaringTypeName = this.GetTypeDefinitionFullName(reader, methodDefinition.GetDeclaringType());
            string methodName = reader.GetString(methodDefinition.Name);

            this.ValidateParameterTypes(reader, methodDefinition, declaringTypeName, methodName, result, seenViolations);
            this.ValidateMethodBody(peReader, reader, methodDefinition, declaringTypeName, methodName, result, seenViolations);
        }

        private void ValidateParameterTypes(
            MetadataReader reader,
            MethodDefinition methodDefinition,
            string declaringTypeName,
            string methodName,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            MetadataTypeNameDecoder decoder = new MetadataTypeNameDecoder(reader, this);
            MethodSignature<string> signature = methodDefinition.DecodeSignature(decoder, genericContext: null);

            foreach (string parameterTypeName in signature.ParameterTypes)
            {
                this.AddDecodedTypeViolations(
                    result,
                    seenViolations,
                    parameterTypeName,
                    dangerousTypeName => $"Dangerous parameter type detected before assembly load: {dangerousTypeName}",
                    dangerousTypeName => $"Parameter type '{dangerousTypeName}' exists in {declaringTypeName}.{methodName}");
            }

            this.AddDecodedTypeViolations(
                result,
                seenViolations,
                signature.ReturnType,
                dangerousTypeName => $"Dangerous return type detected before assembly load: {dangerousTypeName}",
                dangerousTypeName => $"Return type '{dangerousTypeName}' exists in {declaringTypeName}.{methodName}");
        }

        private void ValidateMethodBody(
            PEReader peReader,
            MetadataReader reader,
            MethodDefinition methodDefinition,
            string declaringTypeName,
            string methodName,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            if (methodDefinition.RelativeVirtualAddress == 0)
            {
                return;
            }

            MethodBodyBlock methodBody = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
            this.ValidateLocalVariableTypes(reader, methodBody, declaringTypeName, methodName, result, seenViolations);
            this.ValidateIlInstructions(reader, methodBody, declaringTypeName, methodName, result, seenViolations);
        }

        private void ValidateLocalVariableTypes(
            MetadataReader reader,
            MethodBodyBlock methodBody,
            string declaringTypeName,
            string methodName,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            if (methodBody.LocalSignature.IsNil)
            {
                return;
            }

            StandaloneSignature standaloneSignature = reader.GetStandaloneSignature(methodBody.LocalSignature);
            MetadataTypeNameDecoder decoder = new MetadataTypeNameDecoder(reader, this);
            ImmutableArray<string> localTypes = standaloneSignature.DecodeLocalSignature(decoder, genericContext: null);

            foreach (string localTypeName in localTypes)
            {
                this.AddDecodedTypeViolations(
                    result,
                    seenViolations,
                    localTypeName,
                    dangerousTypeName => $"Dangerous local variable type detected before assembly load: {dangerousTypeName}",
                    dangerousTypeName => $"Local variable type '{dangerousTypeName}' exists in {declaringTypeName}.{methodName}");
            }
        }

        private void ValidateIlInstructions(
            MetadataReader reader,
            MethodBodyBlock methodBody,
            string declaringTypeName,
            string methodName,
            SecurityValidationResult result,
            HashSet<string> seenViolations)
        {
            byte[] ilBytes = methodBody.GetILBytes();
            int offset = 0;

            while (offset < ilBytes.Length)
            {
                OpCode opCode = ReadOpCode(ilBytes, ref offset);

                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    int token = ReadInt32(ilBytes, offset);
                    EntityHandle methodHandle = MetadataTokens.EntityHandle(token);

                    if (this.TryGetMethodReference(methodHandle, reader, out string targetTypeName, out string targetMethodName) &&
                        DangerousApiCatalog.IsDangerousApi(targetTypeName, targetMethodName))
                    {
                        string apiName = $"{targetTypeName}.{targetMethodName}";
                        this.AddViolation(
                            result,
                            seenViolations,
                            apiName,
                            $"Dangerous IL method reference detected before assembly load: {apiName}",
                            $"IL {opCode.Name} references '{apiName}' in {declaringTypeName}.{methodName}");
                    }
                }

                if (opCode == OpCodes.Ldtoken && opCode.OperandType == OperandType.InlineTok)
                {
                    int token = ReadInt32(ilBytes, offset);
                    string referencedTypeName = this.TryGetTypeReference(MetadataTokens.EntityHandle(token), reader);
                    this.AddDecodedTypeViolations(
                        result,
                        seenViolations,
                        referencedTypeName,
                        dangerousTypeName => $"Dangerous IL type token detected before assembly load: {dangerousTypeName}",
                        dangerousTypeName => $"IL ldtoken references '{dangerousTypeName}' in {declaringTypeName}.{methodName}");
                }

                offset += GetOperandSize(opCode.OperandType, ilBytes, offset);
            }
        }

        private bool TryGetMethodReference(
            EntityHandle handle,
            MetadataReader reader,
            out string typeName,
            out string methodName)
        {
            typeName = string.Empty;
            methodName = string.Empty;

            if (handle.Kind == HandleKind.MemberReference)
            {
                MemberReference memberReference = reader.GetMemberReference((MemberReferenceHandle)handle);
                typeName = this.GetMemberReferenceParentTypeName(reader, memberReference.Parent);
                methodName = reader.GetString(memberReference.Name);
                return !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName);
            }

            if (handle.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinition methodDefinition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                typeName = this.GetTypeDefinitionFullName(reader, methodDefinition.GetDeclaringType());
                methodName = reader.GetString(methodDefinition.Name);
                return !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName);
            }

            if (handle.Kind == HandleKind.MethodSpecification)
            {
                MethodSpecification methodSpecification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return this.TryGetMethodReference(methodSpecification.Method, reader, out typeName, out methodName);
            }

            return false;
        }

        private string TryGetTypeReference(EntityHandle handle, MetadataReader reader)
        {
            if (handle.Kind == HandleKind.TypeReference ||
                handle.Kind == HandleKind.TypeDefinition ||
                handle.Kind == HandleKind.TypeSpecification)
            {
                return this.GetEntityTypeName(reader, handle);
            }

            return string.Empty;
        }

        private void AddDecodedTypeViolations(
            SecurityValidationResult result,
            HashSet<string> seenViolations,
            string decodedTypeName,
            System.Func<string, string> createMessage,
            System.Func<string, string> createDescription)
        {
            foreach (string typeName in ExpandDecodedTypeNames(decodedTypeName))
            {
                if (!DangerousApiCatalog.IsDangerousType(typeName))
                {
                    continue;
                }

                this.AddViolation(
                    result,
                    seenViolations,
                    typeName,
                    createMessage(typeName),
                    createDescription(typeName));
            }
        }

        private static IEnumerable<string> ExpandDecodedTypeNames(string decodedTypeName)
        {
            if (string.IsNullOrEmpty(decodedTypeName))
            {
                yield break;
            }

            string[] parts = decodedTypeName.Split(DecodedTypeNameSeparator);
            foreach (string part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    yield return part;
                }
            }
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
                Location = "il"
            });
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

        private static OpCode ReadOpCode(byte[] ilBytes, ref int offset)
        {
            byte first = ilBytes[offset++];
            if (first != 0xFE)
            {
                return SingleByteOpCodes[first];
            }

            byte second = ilBytes[offset++];
            return MultiByteOpCodes[second];
        }

        private static int ReadInt32(byte[] ilBytes, int offset)
        {
            return ilBytes[offset] |
                   (ilBytes[offset + 1] << 8) |
                   (ilBytes[offset + 2] << 16) |
                   (ilBytes[offset + 3] << 24);
        }

        private static int GetOperandSize(OperandType operandType, byte[] ilBytes, int offset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    int count = ReadInt32(ilBytes, offset);
                    return 4 + (count * 4);
                default:
                    return 0;
            }
        }

        private static OpCode[] BuildOpCodeTable(bool singleByte)
        {
            OpCode[] table = new OpCode[256];
            System.Reflection.FieldInfo[] fields = typeof(OpCodes).GetFields();

            foreach (System.Reflection.FieldInfo field in fields)
            {
                if (field.FieldType != typeof(OpCode))
                {
                    continue;
                }

                OpCode opcode = (OpCode)field.GetValue(null);
                ushort value = unchecked((ushort)opcode.Value);

                if (singleByte)
                {
                    if (value <= 0xFF)
                    {
                        table[value] = opcode;
                    }
                    continue;
                }

                if ((value & 0xFF00) == 0xFE00)
                {
                    table[value & 0xFF] = opcode;
                }
            }

            return table;
        }

        private sealed class MetadataTypeNameDecoder : ISignatureTypeProvider<string, object>
        {
            private readonly MetadataReader _reader;
            private readonly PreloadIlSecurityValidator _validator;

            public MetadataTypeNameDecoder(MetadataReader reader, PreloadIlSecurityValidator validator)
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
                List<string> flattenedTypeNames = new List<string>();
                AddFlattenedTypeName(flattenedTypeNames, genericType);
                foreach (string typeArgument in typeArguments)
                {
                    AddFlattenedTypeName(flattenedTypeNames, typeArgument);
                }

                return string.Join(DecodedTypeNameSeparator.ToString(), flattenedTypeNames);
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

            private static void AddFlattenedTypeName(List<string> flattenedTypeNames, string decodedTypeName)
            {
                if (string.IsNullOrEmpty(decodedTypeName))
                {
                    return;
                }

                string[] parts = decodedTypeName.Split(DecodedTypeNameSeparator);
                foreach (string part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        flattenedTypeNames.Add(part);
                    }
                }
            }
        }
    }
}
