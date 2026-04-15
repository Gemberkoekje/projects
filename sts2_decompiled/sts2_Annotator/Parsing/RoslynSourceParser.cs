using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sts2Extractor.Parsing;

internal static class RoslynSourceParser
{
    public static bool TryGetClassInfo(string source, out string className, out string baseType)
    {
        ClassDeclarationSyntax classNode = GetPrimaryClass(source);
        if (classNode == null)
        {
            className = string.Empty;
            baseType = string.Empty;
            return false;
        }

        className = classNode.Identifier.ValueText;
        baseType = ExtractPrimaryBaseType(classNode);
        return className.Length > 0;
    }

    public static bool TryGetBaseConstructorArgs(string source, out string[] args)
    {
        ClassDeclarationSyntax classNode = GetPrimaryClass(source);
        if (classNode == null)
        {
            args = Array.Empty<string>();
            return false;
        }

        ConstructorDeclarationSyntax ctor = classNode.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(static c => c.Initializer != null && c.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer));

        if (ctor == null || ctor.Initializer == null || ctor.Initializer.ArgumentList == null)
        {
            args = Array.Empty<string>();
            return false;
        }

        args = ctor.Initializer.ArgumentList.Arguments
            .Select(static a => a.ToString().Trim())
            .Where(static a => a.Length > 0)
            .ToArray();

        return args.Length > 0;
    }

    public static IReadOnlyList<string> GetOverrideMethodNames(string source)
    {
        ClassDeclarationSyntax classNode = GetPrimaryClass(source);
        if (classNode == null)
        {
            return Array.Empty<string>();
        }

        List<string> names = classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Modifiers.Any(static m => m.IsKind(SyntaxKind.OverrideKeyword)))
            .Select(static method => method.Identifier.ValueText)
            .Where(static name => name.StartsWith("On", StringComparison.Ordinal)
                || name.StartsWith("Before", StringComparison.Ordinal)
                || name.StartsWith("After", StringComparison.Ordinal)
                || name.StartsWith("Modify", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names;
    }

    public static string ExtractMethodBodyOrExpression(string source, string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return string.Empty;
        }

        ClassDeclarationSyntax classNode = GetPrimaryClass(source);
        if (classNode == null)
        {
            return string.Empty;
        }

        MethodDeclarationSyntax method = classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, methodName, StringComparison.Ordinal));

        if (method == null)
        {
            return string.Empty;
        }

        if (method.Body != null)
        {
            return method.Body.ToFullString().Trim();
        }

        if (method.ExpressionBody != null)
        {
            return method.ExpressionBody.ToFullString().Trim() + ";";
        }

        return string.Empty;
    }

    private static ClassDeclarationSyntax GetPrimaryClass(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(static c => c.BaseList != null)
            ?? root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
    }

    private static string ExtractPrimaryBaseType(ClassDeclarationSyntax classNode)
    {
        if (classNode.BaseList == null || classNode.BaseList.Types.Count == 0)
        {
            return string.Empty;
        }

        BaseTypeSyntax baseType = classNode.BaseList.Types[0];
        string raw = baseType.Type.ToString().Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        int genericStart = raw.IndexOf('<');
        if (genericStart >= 0)
        {
            raw = raw.Substring(0, genericStart);
        }

        int namespaceSeparator = raw.LastIndexOf('.');
        if (namespaceSeparator >= 0 && namespaceSeparator < raw.Length - 1)
        {
            raw = raw.Substring(namespaceSeparator + 1);
        }

        return raw;
    }
}
