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

// Warns about an added field that keeps its default value because every method assigning it was
// skipped while a method reading it was applied. The reload itself succeeds, so without this line
// the applied reader silently sees zero or null until the next compile.
internal static class AddedFieldSkippedWriterWarnings
{
    public const string WarningFormat =
        "Added field '{0}' is assigned only in {1}, which this reload skipped, but {2} was applied "
        + "and reads it; the field keeps its default value until 'uloop compile'.";

    private const int MaxListedWriters = 3;

    // Adds one warning per qualifying field declared in unit. Writers and readers are looked for
    // in every unit of the group, because a method of another edited file may assign the field.
    public static void AppendWarnings(
        WorkerSourceUnit unit,
        List<WorkerSourceUnit> transformUnits,
        List<TypeEmitState> allTypeEmitStates,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        Dictionary<IFieldSymbol, FieldUses> candidates = CollectCandidateFields(unit, addedFieldCatalog);
        if (candidates.Count == 0)
        {
            return;
        }

        HashSet<string> skippedLabels = new HashSet<string>(skipped.Select(row => row.Method), StringComparer.Ordinal);
        HashSet<SyntaxNode> queuedMethods = new HashSet<SyntaxNode>(
            allTypeEmitStates.SelectMany(state => state.QueuedMethods).Select(queued => queued.MethodDeclaration));
        foreach (WorkerSourceUnit scannedUnit in transformUnits)
        {
            RecordUses(scannedUnit, candidates, skippedLabels, queuedMethods);
        }

        foreach (KeyValuePair<IFieldSymbol, FieldUses> candidate in candidates)
        {
            if (candidate.Value.ShouldWarn)
            {
                unit.DeclarationDriftWarnings.Add(FormatWarning(candidate.Key.Name, candidate.Value));
            }
        }
    }

    // Only a store-rewritten field without an initializer starts at its default value; a const
    // or an unavailable field never reaches an applied body in the first place.
    private static Dictionary<IFieldSymbol, FieldUses> CollectCandidateFields(
        WorkerSourceUnit unit,
        AddedFieldCatalog addedFieldCatalog)
    {
        Dictionary<IFieldSymbol, FieldUses> candidates =
            new Dictionary<IFieldSymbol, FieldUses>(SymbolEqualityComparer.Default);
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

                AddedFieldBinding binding = addedFieldCatalog.FindOrNull(
                    AddedFieldBodyScan.FormatAddedFieldKeyFromSymbol(fieldSymbol));
                if (binding != null && binding.IsStoreRewriteable && binding.Initializer == null)
                {
                    candidates[fieldSymbol] = new FieldUses();
                }
            }
        }

        return candidates;
    }

    private static void RecordUses(
        WorkerSourceUnit scannedUnit,
        Dictionary<IFieldSymbol, FieldUses> candidates,
        HashSet<string> skippedLabels,
        HashSet<SyntaxNode> queuedMethods)
    {
        SemanticModel semanticModel = scannedUnit.SemanticModel;
        foreach (IdentifierNameSyntax identifier in scannedUnit.BindingRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            // nameof folds to a string constant, so it neither reads nor writes the field.
            if (NameofRules.IsInsideNameofArgument(identifier))
            {
                continue;
            }

            IFieldSymbol fieldSymbol = AddedFieldBodyScan.TryGetFieldSymbol(semanticModel, identifier);
            if (fieldSymbol == null || !candidates.TryGetValue(fieldSymbol, out FieldUses uses))
            {
                continue;
            }

            MethodDeclarationSyntax method = FindEnclosingMethod(identifier);
            string label = method == null
                ? null
                : WorkerMethodKeys.FormatMethodLabel(semanticModel.GetDeclaredSymbol(method));
            bool isSkippedMethod = label != null && skippedLabels.Contains(label);
            if (IsWrite(identifier))
            {
                uses.RecordWriter(isSkippedMethod ? label : null);
                continue;
            }

            if (!isSkippedMethod && method != null && queuedMethods.Contains(method))
            {
                uses.RecordAppliedReader(label);
            }
        }
    }

    // Null when the nearest enclosing member is not a method: a constructor, accessor or field
    // initializer that writes the field is not a skipped method, so it keeps the warning quiet.
    private static MethodDeclarationSyntax FindEnclosingMethod(SyntaxNode node)
    {
        return node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() as MethodDeclarationSyntax;
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
                return argument.RefKindKeyword.Kind() != SyntaxKind.None || IsDeconstructionTarget(argument);
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

    private static string FormatWarning(string fieldName, FieldUses uses)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            WarningFormat,
            fieldName,
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

    // How one candidate field is used across the group, in source order.
    private sealed class FieldUses
    {
        private bool _hasOtherWriter;

        public List<string> SkippedWriters { get; } = new List<string>();

        public List<string> AppliedReaders { get; } = new List<string>();

        public bool ShouldWarn => !_hasOtherWriter && SkippedWriters.Count > 0 && AppliedReaders.Count > 0;

        // Null means a writer that is not a skipped method, which rules the warning out.
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
