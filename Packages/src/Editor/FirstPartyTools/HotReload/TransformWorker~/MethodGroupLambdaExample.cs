using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;

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
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                return string.Empty;
            }
        }

        return " (such as '" + BuildLambda(method.Name, method.Parameters.Length) + "')";
    }

    private static string BuildLambda(string methodName, int parameterCount)
    {
        List<string> names = new List<string>(parameterCount);
        for (int index = 0; index < parameterCount; index++)
        {
            names.Add(ParameterName(index));
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
