using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Post-compilation security validator using IL byte inspection.
    /// Scans call/callvirt/newobj/ldtoken opcodes to detect dangerous API usage
    /// that source-level scanning cannot catch (e.g. using aliases, fully-qualified names).
    /// </summary>
    internal sealed class IlSecurityValidator
    {
        private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeTable(singleByte: true);
        private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeTable(singleByte: false);

        public SecurityValidationResult Validate(Assembly assembly)
        {
            SecurityValidationResult result = new()            {
                IsValid = true,
                Violations = new List<SecurityViolation>()
            };

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, static t => t != null);
            }

            foreach (Type type in types)
            {
                ValidateTypeHierarchy(type, result);
                ValidateMethodReferences(type, result);
            }

            result.IsValid = result.Violations.Count == 0;
            return result;
        }

        private static void ValidateTypeHierarchy(Type type, SecurityValidationResult result)
        {
            ValidateReferencedType(
                type.BaseType,
                result,
                typeName => $"Inheriting from dangerous type: {typeName}",
                typeName => $"Type '{type.FullName}' inherits from blocked type '{typeName}'");

            foreach (Type iface in type.GetInterfaces())
            {
                ValidateReferencedType(
                    iface,
                    result,
                    typeName => $"Implementing dangerous interface: {typeName}",
                    typeName => $"Type '{type.FullName}' implements blocked interface '{typeName}'");
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in type.GetFields(flags))
            {
                ValidateReferencedType(
                    field.FieldType,
                    result,
                    typeName => $"Using dangerous type as field: {typeName}",
                    typeName => $"Field '{field.Name}' of type '{typeName}' in {type.FullName}");
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                ValidateReferencedType(
                    property.PropertyType,
                    result,
                    typeName => $"Using dangerous type as property: {typeName}",
                    typeName => $"Property '{property.Name}' of type '{typeName}' in {type.FullName}");

                foreach (System.Reflection.ParameterInfo indexParameter in property.GetIndexParameters())
                {
                    ValidateReferencedType(
                        indexParameter.ParameterType,
                        result,
                        typeName => $"Using dangerous type as property index parameter: {typeName}",
                        typeName => $"Property index parameter '{indexParameter.Name}' of type '{typeName}' in {type.FullName}.{property.Name}");
                }
            }

            foreach (EventInfo eventInfo in type.GetEvents(flags))
            {
                ValidateReferencedType(
                    eventInfo.EventHandlerType,
                    result,
                    typeName => $"Using dangerous type as event handler: {typeName}",
                    typeName => $"Event '{eventInfo.Name}' of type '{typeName}' in {type.FullName}");
            }
        }

        private static void ValidateMethodReferences(Type type, SecurityValidationResult result)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                ValidateMethodBody(method, result);
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(flags))
            {
                ValidateMethodBody(ctor, result);
            }
        }

        private static void ValidateMethodBody(MethodBase method, SecurityValidationResult result)
        {
            MethodBody body;
            try
            {
                body = method.GetMethodBody();
            }
            catch (InvalidOperationException)
            {
                // Abstract/extern methods have no body
                return;
            }
            if (body == null) return;

            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                string typeName = local.LocalType?.FullName;
                if (typeName != null && DangerousApiCatalog.IsDangerousType(typeName))
                {
                    result.Violations.Add(new SecurityViolation
                    {
                        Type = SecurityViolationType.DangerousApiCall,
                        ApiName = typeName,
                        Message = $"Using dangerous type as local variable: {typeName}",
                        Description = $"Local variable of type '{typeName}' in {method.DeclaringType?.FullName}.{method.Name}"
                    });
                }
            }

            foreach (System.Reflection.ParameterInfo param in method.GetParameters())
            {
                string typeName = param.ParameterType?.FullName;
                if (typeName != null && DangerousApiCatalog.IsDangerousType(typeName))
                {
                    result.Violations.Add(new SecurityViolation
                    {
                        Type = SecurityViolationType.DangerousApiCall,
                        ApiName = typeName,
                        Message = $"Using dangerous type as parameter: {typeName}",
                        Description = $"Parameter '{param.Name}' of type '{typeName}' in {method.DeclaringType?.FullName}.{method.Name}"
                    });
                }
            }

            byte[] il = body.GetILAsByteArray();
            if (il == null) return;

            ValidateIlCalls(method, il, result);
        }

        private static void ValidateReferencedType(
            Type referencedType,
            SecurityValidationResult result,
            Func<string, string> createMessage,
            Func<string, string> createDescription)
        {
            foreach (string typeName in ExpandReferencedTypes(referencedType))
            {
                if (!DangerousApiCatalog.IsDangerousType(typeName))
                {
                    continue;
                }

                result.Violations.Add(new SecurityViolation
                {
                    Type = SecurityViolationType.DangerousApiCall,
                    ApiName = typeName,
                    Message = createMessage(typeName),
                    Description = createDescription(typeName)
                });
            }
        }

        private static IEnumerable<string> ExpandReferencedTypes(Type referencedType)
        {
            if (referencedType == null)
            {
                yield break;
            }

            if (referencedType.HasElementType)
            {
                foreach (string typeName in ExpandReferencedTypes(referencedType.GetElementType()))
                {
                    yield return typeName;
                }

                yield break;
            }

            string fullName = referencedType.FullName;
            if (!string.IsNullOrEmpty(fullName))
            {
                yield return fullName;
            }

            if (!referencedType.IsGenericType)
            {
                yield break;
            }

            foreach (Type genericArgument in referencedType.GetGenericArguments())
            {
                foreach (string typeName in ExpandReferencedTypes(genericArgument))
                {
                    yield return typeName;
                }
            }
        }

        private static void ValidateIlCalls(MethodBase method, byte[] il, SecurityValidationResult result)
        {
            int offset = 0;
            Module module = method.Module;
            Type[] typeArgs = method.DeclaringType?.IsGenericType == true
                ? method.DeclaringType.GetGenericArguments()
                : null;
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            while (offset < il.Length)
            {
                OpCode opCode = ReadOpCode(il, ref offset);

                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    int token = ReadInt32(il, offset);

                    if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj ||
                        opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn)
                    {
                        MethodBase calledMethod;
                        try
                        {
                            calledMethod = module.ResolveMethod(token, typeArgs, methodArgs);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            offset += 4;
                            continue;
                        }
                        catch (ArgumentException)
                        {
                            offset += 4;
                            continue;
                        }

                        string declaringType = calledMethod.DeclaringType?.FullName;
                        string memberName = calledMethod.Name;

                        if (declaringType != null && DangerousApiCatalog.IsDangerousApi(declaringType, memberName))
                        {
                            result.Violations.Add(new SecurityViolation
                            {
                                Type = SecurityViolationType.DangerousApiCall,
                                ApiName = $"{declaringType}.{memberName}",
                                Message = $"Dangerous API call: {declaringType}.{memberName}",
                                Description = $"IL {opCode.Name} to '{declaringType}.{memberName}' in {method.DeclaringType?.FullName}.{method.Name}"
                            });
                        }
                    }
                }

                // ldtoken with InlineTok operand: detect typeof(DangerousType)
                if (opCode == OpCodes.Ldtoken && opCode.OperandType == OperandType.InlineTok)
                {
                    int token = ReadInt32(il, offset);

                    Type resolvedType;
                    try
                    {
                        resolvedType = module.ResolveType(token, typeArgs, methodArgs);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // ldtoken can also reference fields/methods, not just types
                        offset += GetOperandSize(opCode.OperandType, il, offset);
                        continue;
                    }
                    catch (ArgumentException)
                    {
                        // ldtoken can also reference fields/methods, not just types
                        offset += GetOperandSize(opCode.OperandType, il, offset);
                        continue;
                    }

                    string fullName = resolvedType.FullName;
                    if (fullName != null && DangerousApiCatalog.IsDangerousType(fullName))
                    {
                        result.Violations.Add(new SecurityViolation
                        {
                            Type = SecurityViolationType.DangerousApiCall,
                            ApiName = fullName,
                            Message = $"typeof() reference to dangerous type: {fullName}",
                            Description = $"typeof({fullName}) in {method.DeclaringType?.FullName}.{method.Name}"
                        });
                    }
                }

                offset += GetOperandSize(opCode.OperandType, il, offset);
            }
        }

        private static OpCode ReadOpCode(byte[] il, ref int offset)
        {
            byte first = il[offset++];
            if (first != 0xFE) return SingleByteOpCodes[first];
            byte second = il[offset++];
            return MultiByteOpCodes[second];
        }

        private static int ReadInt32(byte[] il, int offset)
        {
            return il[offset] | (il[offset + 1] << 8) | (il[offset + 2] << 16) | (il[offset + 3] << 24);
        }

        private static int GetOperandSize(OperandType operandType, byte[] il, int offset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    int count = ReadInt32(il, offset);
                    return 4 + (count * 4);
                default: return 0;
            }
        }

        private static OpCode[] BuildOpCodeTable(bool singleByte)
        {
            OpCode[] table = new OpCode[0x100];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode opCode) continue;
                ushort value = unchecked((ushort)opCode.Value);
                if (singleByte && value <= 0xFF)
                {
                    table[value] = opCode;
                }
                else if (!singleByte && (value & 0xFF00) == 0xFE00)
                {
                    table[value & 0xFF] = opCode;
                }
            }
            return table;
        }
    }
}
