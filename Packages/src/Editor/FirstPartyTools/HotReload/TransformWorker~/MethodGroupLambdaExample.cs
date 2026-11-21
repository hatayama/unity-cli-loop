using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Builds the lambda the skip reason for an inaccessible method group offers as its rewrite, with
// one lambda parameter per method parameter so the example compiles as written.
internal static class MethodGroupLambdaExample
{
    private const int LetterCount = 26;

    // Returns " (such as 'a => M(a)')", or an empty string when a parameter is ref, out, or in:
    // a lambda for those needs the modifier and an explicit type the reason cannot spell safely,
    // and an example that does not compile misleads more than no example.
    internal static string BuildSuffix(IMethodSymbol method)
    {
        return BuildSuffixCalling(method, method.Name, new HashSet<string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// The same suffix for an extension method named on a receiver ('value.M'), whose lambda calls
    /// it on that receiver ('a => value.M(a)'), with parameter names the receiver does not use.
    /// </summary>
    // Why the receiver: the delegate takes only the parameters after it, so a lambda calling the
    // method by name alone ('a => M(a)') would not convert to the delegate the group did.
    // Why skip the receiver's names: a parameter named like the receiver ('a => a.M(a)') would
    // shadow it, and the example would not compile.
    internal static string BuildSuffixOnReceiver(IMethodSymbol reducedMethod, ExpressionSyntax receiver)
    {
        HashSet<string> receiverNames = new HashSet<string>(
            receiver.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(name => name.Identifier.ValueText),
            StringComparer.Ordinal);
        return BuildSuffixCalling(reducedMethod, receiver + "." + reducedMethod.Name, receiverNames);
    }

    private static string BuildSuffixCalling(IMethodSymbol method, string callee, HashSet<string> reservedNames)
    {
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                return string.Empty;
            }
        }

        return " (such as '" + BuildLambda(callee, method.Parameters.Length, reservedNames) + "')";
    }

    private static string BuildLambda(string methodName, int parameterCount, HashSet<string> reservedNames)
    {
        List<string> names = new List<string>(parameterCount);
        for (int candidate = 0; names.Count < parameterCount; candidate++)
        {
            string name = ParameterName(candidate);
            if (!reservedNames.Contains(name))
            {
                names.Add(name);
            }
        }

        string arguments = string.Join(", ", names);
        string parameters = parameterCount == 1 ? arguments : "(" + arguments + ")";
        return parameters + " => " + methodName + "(" + arguments + ")";
    }

    // Why a suffix after z: a method with more than 26 parameters is legal, and the example must
    // still name each parameter once.
    private static string ParameterName(int index)
    {
        StringBuilder name = new StringBuilder();
        name.Append((char)('a' + (index % LetterCount)));
        if (index >= LetterCount)
        {
            name.Append(index / LetterCount);
        }

        return name.ToString();
    }
}
