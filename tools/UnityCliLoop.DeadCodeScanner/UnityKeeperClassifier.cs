using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Identifies symbols that can be invoked by Unity, serialization, or reflection instead of direct C# references.
    /// </summary>
    public static class UnityKeeperClassifier
    {
        private static readonly HashSet<string> KeptAttributeNames = new(StringComparer.Ordinal)
        {
            "UnityCliLoopTool",
            "UnityCliLoopToolAttribute",
            "InitializeOnLoad",
            "InitializeOnLoadAttribute",
            "InitializeOnLoadMethod",
            "InitializeOnLoadMethodAttribute",
            "RuntimeInitializeOnLoadMethod",
            "RuntimeInitializeOnLoadMethodAttribute",
            "MenuItem",
            "MenuItemAttribute",
            "SettingsProvider",
            "SettingsProviderAttribute",
            "SerializeField",
            "SerializeFieldAttribute",
            "JsonProperty",
            "JsonPropertyAttribute",
            "JsonIgnore",
            "JsonIgnoreAttribute",
            "Serializable",
            "SerializableAttribute"
        };

        private static readonly HashSet<string> KeptBaseTypeNames = new(StringComparer.Ordinal)
        {
            "MonoBehaviour",
            "ScriptableObject",
            "EditorWindow",
            "Editor",
            "PropertyDrawer",
            "AssetPostprocessor"
        };

        private static readonly HashSet<string> UnityCallbackMethodNames = new(StringComparer.Ordinal)
        {
            "Awake",
            "Start",
            "Update",
            "FixedUpdate",
            "LateUpdate",
            "OnEnable",
            "OnDisable",
            "OnDestroy",
            "OnGUI",
            "CreateGUI",
            "OnFocus",
            "Reset",
            "OnValidate"
        };

        private static readonly HashSet<string> AwaiterMemberNames = new(StringComparer.Ordinal)
        {
            "IsCompleted",
            "GetResult",
            "OnCompleted",
            "UnsafeOnCompleted"
        };

        public static KeeperDecision Classify(ISymbol symbol)
        {
            string attributeReason = FindKeptAttributeReason(symbol);
            if (!string.IsNullOrEmpty(attributeReason))
            {
                return KeeperDecision.Keep(attributeReason);
            }

            // Why: the C# compiler resolves init accessors by this exact type name, so Roslyn
            // reference search never sees call sites even though deleting the polyfill breaks builds.
            if (IsCompilerRequiredIsExternalInit(symbol))
            {
                return KeeperDecision.Keep(
                    "Type is the compiler-required IsExternalInit polyfill for init accessors.");
            }

            // Why: await expressions bind GetAwaiter/IsCompleted/GetResult/OnCompleted by name;
            // SymbolFinder therefore reports zero references for these members.
            if (IsCompilerRequiredAwaitPatternMember(symbol))
            {
                return KeeperDecision.Keep(
                    "Member is part of the compiler-bound awaiter pattern.");
            }

            // Why: Newtonsoft.Json discovers ShouldSerialize{Property} by name convention and
            // invokes it via reflection, so reference search never sees call sites.
            if (IsNewtonsoftShouldSerializeMethod(symbol))
            {
                return KeeperDecision.Keep(
                    "Method matches the Newtonsoft.Json ShouldSerialize{Property} naming convention.");
            }

            if (symbol is INamedTypeSymbol namedType && HasKeptBaseType(namedType))
            {
                return KeeperDecision.Keep("Type derives from a Unity entry-point base class.");
            }

            if (symbol is INamedTypeSymbol namedTypeWithKeptMember)
            {
                string memberReason = FindKeptMemberReason(namedTypeWithKeptMember);
                if (!string.IsNullOrEmpty(memberReason))
                {
                    return KeeperDecision.Keep(memberReason);
                }
            }

            if (symbol is IMethodSymbol methodSymbol && UnityCallbackMethodNames.Contains(methodSymbol.Name))
            {
                return KeeperDecision.Keep("Method name matches a Unity lifecycle callback.");
            }

            if (symbol is IFieldSymbol fieldSymbol && fieldSymbol.DeclaredAccessibility == Accessibility.Private)
            {
                string fieldAttributeReason = FindKeptAttributeReason(fieldSymbol);
                if (!string.IsNullOrEmpty(fieldAttributeReason))
                {
                    return KeeperDecision.Keep(fieldAttributeReason);
                }
            }

            return KeeperDecision.Scan();
        }

        private static bool IsCompilerRequiredIsExternalInit(ISymbol symbol)
        {
            if (symbol is not INamedTypeSymbol typeSymbol)
            {
                return false;
            }

            if (!string.Equals(typeSymbol.Name, "IsExternalInit", StringComparison.Ordinal))
            {
                return false;
            }

            string containingNamespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return string.Equals(
                containingNamespace,
                "System.Runtime.CompilerServices",
                StringComparison.Ordinal);
        }

        private static bool IsCompilerRequiredAwaitPatternMember(ISymbol symbol)
        {
            if (symbol is IMethodSymbol getAwaiterMethod
                && string.Equals(getAwaiterMethod.Name, "GetAwaiter", StringComparison.Ordinal))
            {
                return IsAwaiterType(getAwaiterMethod.ReturnType);
            }

            if (!AwaiterMemberNames.Contains(symbol.Name) || symbol.ContainingType == null)
            {
                return false;
            }

            return IsAwaiterType(symbol.ContainingType);
        }

        private static bool IsNewtonsoftShouldSerializeMethod(ISymbol symbol)
        {
            if (symbol is not IMethodSymbol methodSymbol)
            {
                return false;
            }

            if (methodSymbol.IsStatic || methodSymbol.Parameters.Length != 0)
            {
                return false;
            }

            if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            const string Prefix = "ShouldSerialize";
            if (!methodSymbol.Name.StartsWith(Prefix, StringComparison.Ordinal)
                || methodSymbol.Name.Length <= Prefix.Length)
            {
                return false;
            }

            // Why: Newtonsoft only invokes ShouldSerialize{PropertyName} when that member exists.
            // Keeping every ShouldSerialize* name would let dead methods silence the scanner.
            string propertyName = methodSymbol.Name.Substring(Prefix.Length);
            return methodSymbol.ContainingType.GetMembers(propertyName)
                .Any(member => member is IPropertySymbol || member is IFieldSymbol);
        }

        private static bool IsAwaiterType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.AllInterfaces.Any(IsCompilerServicesAwaiterInterface);
        }

        private static bool IsCompilerServicesAwaiterInterface(INamedTypeSymbol interfaceType)
        {
            if (!string.Equals(interfaceType.Name, "INotifyCompletion", StringComparison.Ordinal)
                && !string.Equals(interfaceType.Name, "ICriticalNotifyCompletion", StringComparison.Ordinal))
            {
                return false;
            }

            string containingNamespace = interfaceType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return string.Equals(
                containingNamespace,
                "System.Runtime.CompilerServices",
                StringComparison.Ordinal);
        }

        private static string FindKeptAttributeReason(ISymbol symbol)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                string attributeName = attribute.AttributeClass?.Name ?? string.Empty;
                if (KeptAttributeNames.Contains(attributeName))
                {
                    return $"Symbol has [{attributeName}].";
                }
            }

            return string.Empty;
        }

        private static string FindKeptMemberReason(INamedTypeSymbol typeSymbol)
        {
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                KeeperDecision memberDecision = ClassifyMember(member);
                if (memberDecision.IsKept)
                {
                    return $"Type contains a Unity or reflection entry-point member: {member.Name}.";
                }
            }

            return string.Empty;
        }

        private static KeeperDecision ClassifyMember(ISymbol member)
        {
            string attributeReason = FindKeptAttributeReason(member);
            if (!string.IsNullOrEmpty(attributeReason))
            {
                return KeeperDecision.Keep(attributeReason);
            }

            if (member is IMethodSymbol methodSymbol && UnityCallbackMethodNames.Contains(methodSymbol.Name))
            {
                return KeeperDecision.Keep("Method name matches a Unity lifecycle callback.");
            }

            if (member is IFieldSymbol fieldSymbol && fieldSymbol.DeclaredAccessibility == Accessibility.Private)
            {
                string fieldAttributeReason = FindKeptAttributeReason(fieldSymbol);
                if (!string.IsNullOrEmpty(fieldAttributeReason))
                {
                    return KeeperDecision.Keep(fieldAttributeReason);
                }
            }

            return KeeperDecision.Scan();
        }

        private static bool HasKeptBaseType(INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? baseType = typeSymbol.BaseType;
            while (baseType != null)
            {
                if (KeptBaseTypeNames.Contains(baseType.Name))
                {
                    return true;
                }

                baseType = baseType.BaseType;
            }

            return false;
        }
    }

    /// <summary>
    /// Explains why a symbol should be scanned or preserved despite missing direct references.
    /// </summary>
    public readonly struct KeeperDecision
    {
        public bool IsKept { get; }
        public string Reason { get; }

        private KeeperDecision(bool isKept, string reason)
        {
            IsKept = isKept;
            Reason = reason;
        }

        public static KeeperDecision Keep(string reason)
        {
            return new KeeperDecision(true, reason);
        }

        public static KeeperDecision Scan()
        {
            return new KeeperDecision(false, string.Empty);
        }
    }
}
