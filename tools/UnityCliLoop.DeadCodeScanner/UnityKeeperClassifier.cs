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

        public static KeeperDecision Classify(ISymbol symbol)
        {
            string attributeReason = FindKeptAttributeReason(symbol);
            if (!string.IsNullOrEmpty(attributeReason))
            {
                return KeeperDecision.Keep(attributeReason);
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
