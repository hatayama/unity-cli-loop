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

// Warns about an added field or added auto-property that keeps its default value because every
// method or accessor assigning it was skipped while one reading it was applied. The reload itself
// succeeds, so without this line the applied reader silently sees zero or null until the next compile.
internal static class AddedFieldSkippedWriterWarnings
{
    public const string FieldWarningFormat =
        "Added field '{0}' is assigned only in {1}, which this reload skipped, but {2} was applied "
        + "and reads it; the field keeps its default value until 'uloop compile'.";

    public const string AutoPropertyWarningFormat =
        "Added auto-property '{0}' is assigned only in {1}, which this reload skipped, but {2} was applied "
        + "and reads it; the property keeps its default value until 'uloop compile'.";

    private const int MaxListedWriters = 3;

    // Adds one warning per qualifying field or auto-property declared in unit. Writers and readers
    // are looked for in every unit of the group, because a method of another edited file may
    // assign the value.
    public static void AppendWarnings(
        WorkerSourceUnit unit,
        List<WorkerSourceUnit> transformUnits,
        List<TypeEmitState> allTypeEmitStates,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        List<WorkerSkipped> skipped)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        Dictionary<ISymbol, FieldUses> candidates =
            new Dictionary<ISymbol, FieldUses>(SymbolEqualityComparer.Default);
        CollectCandidateFields(unit, addedFieldCatalog, candidates);
        CollectCandidateAutoProperties(unit, addedFieldCatalog, addedPropertyCatalog, candidates);
        if (candidates.Count == 0)
        {
            return;
        }

        SkippedWriterUseSiteClassifier classifier = new SkippedWriterUseSiteClassifier(
            new HashSet<string>(skipped.Select(row => row.Method), StringComparer.Ordinal),
            new HashSet<SyntaxNode>(
                allTypeEmitStates.SelectMany(state => state.QueuedMethods).Select(queued => queued.MethodDeclaration)),
            addedPropertyCatalog);
        foreach (WorkerSourceUnit scannedUnit in transformUnits)
        {
            RecordUses(scannedUnit, candidates, classifier);
        }

        foreach (KeyValuePair<ISymbol, FieldUses> candidate in candidates)
        {
            if (candidate.Value.ShouldWarn)
            {
                unit.DeclarationDriftWarnings.Add(FormatWarning(candidate.Key, candidate.Value));
            }
        }
    }

    // Only a store-rewritten field without an initializer starts at its default value; a const
    // or an unavailable field never reaches an applied body in the first place.
    private static void CollectCandidateFields(
        WorkerSourceUnit unit,
        AddedFieldCatalog addedFieldCatalog,
        Dictionary<ISymbol, FieldUses> candidates)
    {
        foreach (TypeEmitState state in unit.TypeEmitStates)
        {
            foreach (VariableDeclaratorSyntax variable in state.TypeDeclaration.Members
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables))
            {
                if (!(unit.SemanticModel.GetDeclaredSymbol(variable) is IFieldSymbol fieldSymbol))
                {
                    continue;
                }

                if (StartsAtDefault(addedFieldCatalog, AddedFieldBodyScan.FormatAddedFieldKeyFromSymbol(fieldSymbol)))
                {
                    candidates[fieldSymbol] = new FieldUses();
                }
            }
        }
    }

    // An emitted added auto-property keeps its value in the same store as an added field, under
    // the property key, so the field's start-at-default rule applies to it unchanged.
    private static void CollectCandidateAutoProperties(
        WorkerSourceUnit unit,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        Dictionary<ISymbol, FieldUses> candidates)
    {
        foreach (TypeEmitState state in unit.TypeEmitStates)
        {
            foreach (PropertyDeclarationSyntax property in state.TypeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                IPropertySymbol propertySymbol = unit.SemanticModel.GetDeclaredSymbol(property);
                AddedPropertyBinding binding = addedPropertyCatalog.FindBySymbolOrNull(propertySymbol);
                if (binding != null && binding.IsAuto && StartsAtDefault(addedFieldCatalog, binding.PropertyKey))
                {
                    candidates[propertySymbol] = new FieldUses();
                }
            }
        }
    }

    private static bool StartsAtDefault(AddedFieldCatalog addedFieldCatalog, string fieldKey)
    {
        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(fieldKey);
        return binding != null && binding.IsStoreRewriteable && binding.Initializer == null;
    }

    private static void RecordUses(
        WorkerSourceUnit scannedUnit,
        Dictionary<ISymbol, FieldUses> candidates,
        SkippedWriterUseSiteClassifier classifier)
    {
        SemanticModel semanticModel = scannedUnit.SemanticModel;
        foreach (IdentifierNameSyntax identifier in scannedUnit.BindingRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            // nameof folds to a string constant, so it neither reads nor writes the value.
            if (NameofRules.IsInsideNameofArgument(identifier))
            {
                continue;
            }

            ISymbol symbol = ResolveValueSymbol(semanticModel, identifier);
            if (symbol == null || !candidates.TryGetValue(symbol, out FieldUses uses))
            {
                continue;
            }

            SkippedWriterUseSite site = classifier.Classify(semanticModel, identifier);
            if (IsWrite(identifier))
            {
                uses.RecordWriter(site.Kind == SkippedWriterUseSiteKind.Skipped ? site.Label : null);
                continue;
            }

            if (site.Kind == SkippedWriterUseSiteKind.Applied)
            {
                uses.RecordAppliedReader(site.Label);
            }
        }
    }

    private static ISymbol ResolveValueSymbol(SemanticModel semanticModel, IdentifierNameSyntax identifier)
    {
        IFieldSymbol fieldSymbol = AddedFieldBodyScan.TryGetFieldSymbol(semanticModel, identifier);
        if (fieldSymbol != null)
        {
            return fieldSymbol;
        }

        return semanticModel.GetSymbolInfo(identifier).Symbol as IPropertySymbol;
    }

    private static bool IsWrite(IdentifierNameSyntax identifier)
    {
        ExpressionSyntax target = identifier;
        if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
        {
            target = memberAccess;
        }

        SyntaxNode parent = target.Parent;
        switch (parent)
        {
            case AssignmentExpressionSyntax assignment:
                return assignment.Left == target;
            case PrefixUnaryExpressionSyntax prefix:
                return prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression);
            case PostfixUnaryExpressionSyntax postfix:
                return postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression);
            case ArgumentSyntax argument:
                // An in argument is read-only; counting it as a write would name a skipped
                // method that only reads the field as its writer.
                return argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                    || IsDeconstructionTarget(argument);
            case RefExpressionSyntax _:
                return true;
            default:
                return false;
        }
    }

    // (Field, other) = value assigns through a tuple on the left side.
    private static bool IsDeconstructionTarget(ArgumentSyntax argument)
    {
        SyntaxNode node = argument.Parent;
        while (node is TupleExpressionSyntax && node.Parent is ArgumentSyntax)
        {
            node = node.Parent.Parent;
        }

        return node is TupleExpressionSyntax && node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
    }

    private static string FormatWarning(ISymbol candidate, FieldUses uses)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            candidate is IPropertySymbol ? AutoPropertyWarningFormat : FieldWarningFormat,
            candidate.Name,
            FormatList(uses.SkippedWriters, MaxListedWriters),
            FormatList(uses.AppliedReaders, 1));
    }

    private static string FormatList(List<string> labels, int maxListed)
    {
        if (labels.Count <= maxListed)
        {
            return string.Join(", ", labels);
        }

        return string.Join(", ", labels.Take(maxListed))
            + string.Format(CultureInfo.InvariantCulture, " and {0} more", labels.Count - maxListed);
    }

    // How one candidate field or auto-property is used across the group, in source order.
    private sealed class FieldUses
    {
        private bool _hasOtherWriter;

        public List<string> SkippedWriters { get; } = new List<string>();

        public List<string> AppliedReaders { get; } = new List<string>();

        public bool ShouldWarn => !_hasOtherWriter && SkippedWriters.Count > 0 && AppliedReaders.Count > 0;

        // Null means a writer that is not a skipped method or accessor, which rules the warning out.
        public void RecordWriter(string skippedMethodLabel)
        {
            if (skippedMethodLabel == null)
            {
                _hasOtherWriter = true;
                return;
            }

            AddDistinct(SkippedWriters, skippedMethodLabel);
        }

        public void RecordAppliedReader(string methodLabel)
        {
            AddDistinct(AppliedReaders, methodLabel);
        }

        private static void AddDistinct(List<string> labels, string label)
        {
            if (!labels.Contains(label))
            {
                labels.Add(label);
            }
        }
    }
}
