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

internal static class ConstantLiteralFactory
{
    internal static ExpressionSyntax TryCreateConstantLiteral(object value, ITypeSymbol type)
    {
        if (value == null)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        if (type != null && type.TypeKind == TypeKind.Enum)
        {
            ExpressionSyntax underlyingLiteral = TryCreateNumericOrBoolLiteral(value);
            if (underlyingLiteral == null)
            {
                return null;
            }

            return SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                underlyingLiteral);
        }

        if (value is string text)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(text));
        }

        if (value is char character)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal(character));
        }

        return TryCreateNumericOrBoolLiteral(value);
    }

    internal static ExpressionSyntax TryCreateNumericOrBoolLiteral(object value)
    {
        if (value is bool flag)
        {
            return SyntaxFactory.LiteralExpression(
                flag ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
        }

        ExpressionSyntax integerLiteral = TryCreateInt32ThroughUInt64Literal(value);
        if (integerLiteral != null)
        {
            return integerLiteral;
        }

        ExpressionSyntax floatingLiteral = TryCreateFloatingLiteral(value);
        if (floatingLiteral != null)
        {
            return floatingLiteral;
        }

        return TryCreateDecimalOrSmallIntegerLiteral(value);
    }

    internal static ExpressionSyntax TryCreateInt32ThroughUInt64Literal(object value)
    {
        if (value is int intValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(intValue));
        }

        if (value is uint uintValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(uintValue));
        }

        if (value is long longValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(longValue));
        }

        if (value is ulong ulongValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(ulongValue));
        }

        return null;
    }

    internal static ExpressionSyntax TryCreateFloatingLiteral(object value)
    {
        if (value is float floatValue)
        {
            if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
            {
                return null;
            }

            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(floatValue));
        }

        if (value is double doubleValue)
        {
            if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
            {
                return null;
            }

            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(doubleValue));
        }

        return null;
    }

    internal static ExpressionSyntax TryCreateDecimalOrSmallIntegerLiteral(object value)
    {
        if (value is decimal decimalValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(decimalValue));
        }

        if (value is byte byteValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(byteValue));
        }

        if (value is sbyte sbyteValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(sbyteValue));
        }

        if (value is short shortValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(shortValue));
        }

        if (value is ushort ushortValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(ushortValue));
        }

        return null;
    }
}
