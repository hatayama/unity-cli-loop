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

/// <summary>
/// What: one Harmony accessor delegate field plus the statements that bind it in __BindAccessors.
/// </summary>
internal sealed class AccessorEntry
{
    public AccessorKind Kind { get; private set; }

    public string RegistryKey { get; private set; }

    public string DelegateFieldName { get; private set; }

    public IFieldSymbol FieldSymbol { get; private set; }

    public IMethodSymbol MethodSymbol { get; private set; }

    public IPropertySymbol PropertySymbol { get; private set; }

    public static AccessorEntry ForField(
        IFieldSymbol fieldSymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.FieldRef,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            FieldSymbol = fieldSymbol
        };
    }

    public static AccessorEntry ForMethod(
        IMethodSymbol methodSymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.MethodDelegate,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            MethodSymbol = methodSymbol
        };
    }

    public static AccessorEntry ForPropertyGetter(
        IPropertySymbol propertySymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.PropertyGetter,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            PropertySymbol = propertySymbol
        };
    }

    public static AccessorEntry ForPropertySetter(
        IPropertySymbol propertySymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.PropertySetter,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            PropertySymbol = propertySymbol
        };
    }

    public bool TryGetVisibilityFailure(out string reason)
    {
        foreach (ITypeSymbol typeSymbol in EnumerateSignatureTypes())
        {
            if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                reason = "accessor signature type is not visible from an external assembly: "
                    + typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return true;
            }
        }

        reason = null;
        return false;
    }

    public IEnumerable<ITypeSymbol> EnumerateSignatureTypes()
    {
        // Bind statements always emit typeof(ContainingType), including for static members.
        switch (Kind)
        {
            case AccessorKind.FieldRef:
                yield return FieldSymbol.ContainingType;
                yield return FieldSymbol.Type;
                yield break;
            case AccessorKind.MethodDelegate:
                yield return MethodSymbol.ContainingType;
                foreach (IParameterSymbol parameter in MethodSymbol.Parameters)
                {
                    yield return parameter.Type;
                }

                if (!MethodSymbol.ReturnsVoid)
                {
                    yield return MethodSymbol.ReturnType;
                }

                yield break;
            case AccessorKind.PropertyGetter:
            case AccessorKind.PropertySetter:
                yield return PropertySymbol.ContainingType;
                yield return PropertySymbol.Type;
                yield break;
        }
    }

    public FieldDeclarationSyntax EmitFieldDeclaration()
    {
        TypeSyntax fieldType = SyntaxFactory.ParseTypeName(BuildDelegateTypeDisplayString());
        return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(fieldType)
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(DelegateFieldName))))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    public StatementSyntax EmitBindStatement()
    {
        string statement = Kind switch
        {
            AccessorKind.FieldRef => BuildFieldRefBindStatement(),
            AccessorKind.MethodDelegate => BuildMethodDelegateBindStatement(
                MethodSymbol.Name,
                MethodSymbol),
            AccessorKind.PropertyGetter => BuildMethodDelegateBindStatement(
                PropertySymbol.GetMethod.Name,
                PropertySymbol.GetMethod),
            AccessorKind.PropertySetter => BuildMethodDelegateBindStatement(
                PropertySymbol.SetMethod.Name,
                PropertySymbol.SetMethod),
            _ => throw new InvalidOperationException("Unknown accessor kind.")
        };

        return SyntaxFactory.ParseStatement(statement);
    }

    private string BuildDelegateTypeDisplayString()
    {
        switch (Kind)
        {
            case AccessorKind.FieldRef:
                if (FieldSymbol.IsStatic)
                {
                    return "global::HarmonyLib.AccessTools.FieldRef<"
                        + TypeDisplay(FieldSymbol.Type) + ">";
                }

                return "global::HarmonyLib.AccessTools.FieldRef<"
                    + TypeDisplay(FieldSymbol.ContainingType) + ", "
                    + TypeDisplay(FieldSymbol.Type) + ">";
            case AccessorKind.MethodDelegate:
                return BuildFuncOrActionType(MethodSymbol);
            case AccessorKind.PropertyGetter:
                return BuildFuncOrActionType(PropertySymbol.GetMethod);
            case AccessorKind.PropertySetter:
                return BuildFuncOrActionType(PropertySymbol.SetMethod);
            default:
                throw new InvalidOperationException("Unknown accessor kind.");
        }
    }

    private static string BuildFuncOrActionType(IMethodSymbol methodSymbol)
    {
        List<string> typeArguments = new List<string>();
        foreach (ITypeSymbol parameterType in EnumerateDelegateParameterTypes(methodSymbol))
        {
            typeArguments.Add(TypeDisplay(parameterType));
        }

        if (methodSymbol.ReturnsVoid)
        {
            if (typeArguments.Count == 0)
            {
                return "global::System.Action";
            }

            return "global::System.Action<" + string.Join(", ", typeArguments) + ">";
        }

        typeArguments.Add(TypeDisplay(methodSymbol.ReturnType));
        return "global::System.Func<" + string.Join(", ", typeArguments) + ">";
    }

    private string BuildFieldRefBindStatement()
    {
        if (FieldSymbol.IsStatic)
        {
            // Why FieldInfo: the Type+name StaticFieldRefAccess overloads return ref F,
            // not a FieldRef`1 that __BindAccessors can store.
            return DelegateFieldName + " = global::HarmonyLib.AccessTools.StaticFieldRefAccess<"
                + TypeDisplay(FieldSymbol.Type)
                + ">(global::HarmonyLib.AccessTools.Field(typeof("
                + TypeDisplay(FieldSymbol.ContainingType) + "), \""
                + EscapeStringLiteral(FieldSymbol.Name) + "\"));";
        }

        return DelegateFieldName + " = global::HarmonyLib.AccessTools.FieldRefAccess<"
            + TypeDisplay(FieldSymbol.ContainingType) + ", "
            + TypeDisplay(FieldSymbol.Type) + ">(\""
            + EscapeStringLiteral(FieldSymbol.Name) + "\");";
    }

    private string BuildMethodDelegateBindStatement(string metadataName, IMethodSymbol methodSymbol)
    {
        string declaringType = TypeDisplay(methodSymbol.ContainingType);
        string delegateType = BuildFuncOrActionType(methodSymbol);
        List<ITypeSymbol> delegateParameterTypes = EnumerateDelegateParameterTypes(methodSymbol);
        // AccessTools.Method matches metadata parameters only; the instance receiver is not one.
        IReadOnlyList<ITypeSymbol> methodLookupTypes = methodSymbol.IsStatic
            ? delegateParameterTypes
            : delegateParameterTypes.GetRange(1, delegateParameterTypes.Count - 1);
        string typeArray = BuildTypeArrayLiteral(methodLookupTypes);
        // virtualCall must stay true for virtual/override/abstract instance members so a derived
        // override is dispatched; non-virtual private/internal targets keep false (exact method).
        bool virtualCall = !methodSymbol.IsStatic
            && (methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract);
        string virtualCallLiteral = virtualCall ? "true" : "false";
        // Why not null: Harmony then uses Func<> generic arguments including TResult as
        // DynamicMethod parameters, so Func<Host,T> becomes T(Host,T) and bind fails.
        string delegateArgs = BuildTypeArrayLiteral(delegateParameterTypes);
        return DelegateFieldName + " = global::HarmonyLib.AccessTools.MethodDelegate<"
            + delegateType + ">(global::HarmonyLib.AccessTools.Method(typeof("
            + declaringType + "), \"" + EscapeStringLiteral(metadataName) + "\", "
            + typeArray + "), null, " + virtualCallLiteral + ", " + delegateArgs + ");";
    }

    // Open-delegate parameter types: declaring type first for instance methods, then each
    // method parameter. Excludes Func TResult so Harmony arity matches the delegate Invoke.
    private static List<ITypeSymbol> EnumerateDelegateParameterTypes(IMethodSymbol methodSymbol)
    {
        List<ITypeSymbol> types = new List<ITypeSymbol>();
        if (!methodSymbol.IsStatic)
        {
            types.Add(methodSymbol.ContainingType);
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            types.Add(parameter.Type);
        }

        return types;
    }

    private static string BuildTypeArrayLiteral(IReadOnlyList<ITypeSymbol> types)
    {
        if (types.Count == 0)
        {
            return "new global::System.Type[] { }";
        }

        IEnumerable<string> typeofs = types.Select(type => "typeof(" + TypeDisplay(type) + ")");
        return "new global::System.Type[] { " + string.Join(", ", typeofs) + " }";
    }

    private static string TypeDisplay(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
